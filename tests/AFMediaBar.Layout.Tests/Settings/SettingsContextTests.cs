// Checks monitor-only editing, unavailable hosts, and cancellation against fake platform snapshots without touching real taskbars.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsContextTests
{
    [TestMethod]
    public void EditingContextFollowsObservedRuntimeAndEnablingExperimentDoesNotSwitch()
    {
        SettingsMotionTests.RunSta(() =>
        {
            var previous = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new AppSettings();
                var reader = new Reader();
                using var service = new SettingsContextService(reader);
                Complete(service.RefreshAsync());
                SettingsManager.Current.Experiments = new() { FloatingBar = true };
                Assert.AreEqual(SettingsMode.Taskbar, service.Current.Mode);
                reader.Result = new(SettingsMode.FloatingBar, []);
                Complete(service.RefreshAsync());
                Assert.AreEqual(SettingsMode.FloatingBar, service.Current.Mode);
                Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode, "Only the committed environment determines the editor.");
                reader.Result = new(SettingsMode.Taskbar, []);
                Complete(service.RefreshAsync());
                Assert.AreEqual(SettingsMode.Taskbar, service.Current.Mode);
            }
            finally { SettingsManager.Current = previous; }
        });
    }
    [TestMethod]
    public void UnsupportedModesCannotExposeTaskbarPages()
    {
        foreach (var page in SettingsPageCatalog.ForMode(SettingsMode.Taskbar))
            Assert.IsTrue(Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(page.IconName, out var icon) && Enum.IsDefined(icon), page.IconName);
        foreach (var mode in new[] { SettingsMode.DynamicIsland, SettingsMode.DesktopCard, SettingsMode.FloatingBall })
        {
            Assert.IsFalse(SettingsPageCatalog.IsImplemented(mode));
            Assert.IsTrue(SettingsPageCatalog.ForMode(mode).All(page => page.IsGlobal));
            Assert.IsNull(SettingsPageCatalog.Find(SettingsPageKey.Components, mode));
        }
    }

    [TestMethod]
    public void InactivePageContextCancelsItsWorkWithIndependentHostStorage()
    {
        using var page = new SettingsPageContext();
        page.Initialize(new(SettingsMode.Taskbar, "second", LayoutOrientation.Vertical, true, true));
        var configuration = new LegacySettingsConfiguration(page);
        var token = configuration.CancellationToken;
        Assert.AreEqual(BarConfigurationTarget.Taskbar, configuration.Target);
        page.Deactivate();
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(configuration.IsActive);
        page.Activate();
        Assert.IsFalse(configuration.CancellationToken.IsCancellationRequested);
        Assert.AreEqual(LayoutOrientation.Vertical, configuration.Context.Orientation);
    }

    [TestMethod]
    public void UnsupportedAndInactiveContextsCannotOverwriteCurrentSettings()
    {
        using var page = new SettingsPageContext();
        page.Initialize(SettingsContext.Initial with { Mode = SettingsMode.DynamicIsland });
        var configuration = new LegacySettingsConfiguration(page);
        var before = configuration.Current.TaskbarExperience;
        var lyrics = configuration.Current.LyricsEnabled;
        var spectrum = configuration.Current.SpectrumComponent;
        configuration.SetTaskbarExperience(before with { HoverLayerEnabled = !before.HoverLayerEnabled });
        configuration.SetLyricsEnabled(!lyrics);
        configuration.SetSpectrum(spectrum with { SensitivityPercent = spectrum.SensitivityPercent == 100 ? 101 : 100 });
        Assert.AreEqual(before, configuration.Current.TaskbarExperience);
        Assert.AreEqual(lyrics, configuration.Current.LyricsEnabled);
        Assert.AreEqual(spectrum, configuration.Current.SpectrumComponent);
        page.Deactivate();
        page.Initialize(SettingsContext.Initial);
        configuration.SetTaskbarExperience(before with { HoverLayerEnabled = !before.HoverLayerEnabled });
        Assert.AreEqual(before, configuration.Current.TaskbarExperience);
    }

    [TestMethod]
    public void EditorUsesPrimaryEnabledTargetAndMonitorSelectionDoesNotChangeEnvironment()
    {
        SettingsMotionTests.RunSta(() =>
        {
            var reader = new Reader
            {
                Result = new(SettingsMode.Taskbar,
                [
                    new("second", "Second", false, LayoutOrientation.Vertical, false, true),
                    new("primary", "Primary", true, LayoutOrientation.Horizontal, true, true)
                ])
            };
            using var service = new SettingsContextService(reader);
            Complete(service.RefreshAsync());
            Assert.AreEqual("primary", service.Current.MonitorDeviceId);
            service.SelectMonitor("second");
            Assert.AreEqual("second", service.Current.MonitorDeviceId);
            Assert.AreEqual(LayoutOrientation.Vertical, service.Current.Orientation);
            Assert.AreEqual(LayoutOrientation.Horizontal, reader.Result.Monitors[1].Orientation);
            reader.Result = new(SettingsMode.Taskbar, [reader.Result.Monitors[1]]);
            Complete(service.RefreshAsync());
            Assert.AreEqual("primary", service.Current.MonitorDeviceId);
            service.Dispose();
            service.Dispose();
        });
    }

    [TestMethod]
    public void ClosedContextRejectsLateSnapshot()
    {
        SettingsMotionTests.RunSta(() =>
        {
            var pending = new TaskCompletionSource<SettingsEnvironmentSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reader = new Reader { Pending = pending.Task };
            var service = new SettingsContextService(reader);
            var refresh = service.RefreshAsync();
            var publications = 0;
            service.Changed += (_, _) => publications++;
            service.Dispose();
            pending.SetResult(new(SettingsMode.Taskbar, [new("late", "Late", true, LayoutOrientation.Vertical, false, true)]));
            Complete(refresh);
            Assert.AreEqual(0, publications);
            Assert.AreEqual(SettingsContext.Initial, service.Current);
        });
    }

    [TestMethod]
    public void CommittedModeRejectsOldEnvironmentAndFailedSwitchKeepsContext()
    {
        SettingsMotionTests.RunSta(() =>
        {
            var previous = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new AppSettings { Experiments = new() { FloatingBar = true } };
                var pending = new TaskCompletionSource<SettingsEnvironmentSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reader = new Reader { Pending = pending.Task };
                using var runtime = new BarRuntimeCoordinator();
                using var service = new SettingsContextService(reader, runtime);
                var oldRefresh = service.RefreshAsync();
                reader.Pending = null;
                reader.Result = new(SettingsMode.FloatingBar, [new("floating", "Floating", true, LayoutOrientation.Horizontal, false, true)]);
                Complete(runtime.SwitchAsync(WindowMode.FloatingBar, _ => Task.FromResult<IReadOnlyList<IBarRuntimeHost>>([])));
                Assert.AreEqual(SettingsMode.FloatingBar, service.Current.Mode);
                pending.SetResult(new(SettingsMode.Taskbar, [new("old", "Old", true, LayoutOrientation.Vertical, false, true)]));
                Complete(oldRefresh);
                SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(50));
                Assert.AreEqual("floating", service.Current.MonitorDeviceId);
                Complete(runtime.SwitchAsync(WindowMode.Taskbar, _ => throw new InvalidOperationException("prepare failed")));
                Assert.AreEqual(SettingsMode.FloatingBar, service.Current.Mode);
                Assert.AreEqual(WindowMode.FloatingBar, runtime.RunningMode);
            }
            finally { SettingsManager.Current = previous; }
        });
    }

    private static void Complete(Task task)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < until) SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(10));
        Assert.IsTrue(task.IsCompleted, "The asynchronous context query did not complete.");
        task.GetAwaiter().GetResult();
    }

    private sealed class Reader : ISettingsEnvironmentReader
    {
        public SettingsEnvironmentSnapshot Result { get; set; } = new(SettingsMode.Taskbar, []);
        public Task<SettingsEnvironmentSnapshot>? Pending { get; set; }
        public Task<SettingsEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken) => Pending ?? Task.FromResult(Result);
    }
}
