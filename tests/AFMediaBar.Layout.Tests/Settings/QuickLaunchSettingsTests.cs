// 验证快速启动的旧配置读取、真实持久化、选择及快照重置；不启动任何应用。
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class QuickLaunchSettingsTests
{
    [TestMethod]
    public async Task CancelledLaunchNeverReachesActivation()
    {
        var activation = new MediaSourceActivationService(new MediaSourceProcessResolver());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var entry = new QuickLaunchEntry("cancelled", "Cancelled", QuickLaunchTargetKind.Executable, "never-launch-this.exe");
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => activation.LaunchAsync(entry, cancellation.Token));
    }
    [TestMethod]
    public void SelectionSurvivesPersistenceReorderAndSnapshotReset()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-quick-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SettingsManager.SetUserDefaults(null);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"schemaVersion\":2,\"settings\":{\"quickLaunch\":{\"entries\":[]}}}");
            using (var persistence = new SettingsPersistenceService(directory))
            {
                persistence.Initialize();
                Assert.AreEqual(QuickLaunchInteractionMode.ListLaunch, SettingsManager.Current.QuickLaunch.InteractionMode);
                Assert.IsTrue(SettingsManager.Current.QuickLaunch.ShowApplicationIconOnHover);
                var a = new QuickLaunchEntry("a", "A", QuickLaunchTargetKind.Executable, "a.exe");
                var b = new QuickLaunchEntry("b", "B", QuickLaunchTargetKind.Executable, "b.exe");
                SettingsManager.Current.QuickLaunch = new([a, b])
                { InteractionMode = QuickLaunchInteractionMode.SelectThenLaunch, ShowApplicationIconOnHover = false, SelectedEntryId = "b" };
                SettingsManager.Current.QuickLaunch = SettingsManager.Current.QuickLaunch with { Entries = [b, a] };
                persistence.Flush();
            }
            SettingsManager.Current = new();
            using var reloaded = new SettingsPersistenceService(directory);
            reloaded.Initialize();
            var saved = SettingsManager.Current.Clone();
            Assert.AreEqual("b", QuickLaunchService.ResolveSelected(saved.QuickLaunch)!.Id);
            Assert.AreEqual(QuickLaunchInteractionMode.SelectThenLaunch, saved.QuickLaunch.InteractionMode);
            Assert.IsFalse(saved.QuickLaunch.ShowApplicationIconOnHover);
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.Current.QuickLaunch = QuickLaunchSettings.Default;
            SettingsManager.ResetComponents();
            Assert.AreEqual("b", QuickLaunchService.ResolveSelected(SettingsManager.Current.QuickLaunch)!.Id);
            SettingsManager.Current.QuickLaunch = SettingsManager.Current.QuickLaunch with { Entries = [saved.QuickLaunch.Entries![1]] };
            Assert.AreEqual("a", QuickLaunchService.ResolveSelected(SettingsManager.Current.QuickLaunch)!.Id);
            Assert.AreEqual("b", saved.QuickLaunch.SelectedEntryId);
        }
        finally
        {
            SettingsManager.SetUserDefaults(previousDefaults);
            SettingsManager.Current = previous;
            Directory.Delete(directory, true);
        }
    }
}
