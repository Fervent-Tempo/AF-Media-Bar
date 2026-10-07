// Checks monitor-only editing, unavailable hosts, and cancellation against fake platform snapshots without touching real taskbars.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SettingsContextTests
{
    [TestMethod]
    public void UnsupportedModesCannotExposeTaskbarPages()
    {
        foreach (var mode in new[] { SettingsMode.DynamicIsland, SettingsMode.DesktopCard, SettingsMode.FloatingBall })
        {
            Assert.IsFalse(SettingsPageCatalog.IsImplemented(mode));
            Assert.IsTrue(SettingsPageCatalog.ForMode(mode).All(page => page.IsGlobal));
            Assert.IsNull(SettingsPageCatalog.Find(SettingsPageKey.Components, mode));
        }
    }

    [TestMethod]
    public void InactivePageContextCancelsItsWorkWithoutClaimingIndependentStorage()
    {
        using var page = new SettingsPageContext();
        page.Initialize(new(SettingsMode.Taskbar, "second", LayoutOrientation.Vertical, true, true));
        var configuration = new LegacySettingsConfiguration(page);
        var token = configuration.CancellationToken;
        Assert.IsFalse(configuration.HasIndependentProfiles);
        page.Deactivate();
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(configuration.IsActive);
        page.Activate();
        Assert.IsFalse(configuration.CancellationToken.IsCancellationRequested);
        Assert.AreEqual(LayoutOrientation.Vertical, configuration.Context.Orientation);
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
        public Task<SettingsEnvironmentSnapshot>? Pending { get; init; }
        public Task<SettingsEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken) => Pending ?? Task.FromResult(Result);
    }
}
