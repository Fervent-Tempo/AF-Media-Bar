// 防止分屏写入、重置和真实持久化覆盖其他显示器或模式；静态设置在每例后恢复。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证每屏布局与正常位置的数据隔离。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MonitorBarProfileTests
{
    [TestMethod]
    public void SharedBatteryDemandUsesSelectedScreensAndIgnoresInactiveMode()
    {
        var settings = new AppSettings { TaskbarTargetMonitorDeviceIds = ["A", "B"] };
        var selected = MonitorBarProfilePolicy.Capture(settings, BarConfigurationTarget.Taskbar, "B");
        MonitorBarProfilePolicy.Store(settings, selected with
        { Resident = selected.Resident.Select(entry => entry.Component == TaskbarRestComponent.BluetoothBattery ? entry with { IsVisible = true } : entry).ToArray() });
        Assert.IsTrue(MonitorBarProfilePolicy.IsRequested(settings, TaskbarRestComponent.BluetoothBattery));
        settings.TaskbarTargetMonitorDeviceIds = ["A"];
        Assert.IsFalse(MonitorBarProfilePolicy.IsRequested(settings, TaskbarRestComponent.BluetoothBattery));
        settings.WindowMode = WindowMode.FloatingBar;
        settings.FloatingBar = settings.FloatingBar with { MonitorDeviceIds = ["B"] };
        Assert.IsFalse(MonitorBarProfilePolicy.IsRequested(settings, TaskbarRestComponent.BluetoothBattery));
        settings.WindowMode = WindowMode.Taskbar;
        settings.TaskbarTargetMonitorDeviceIds = [];
        settings.BluetoothBatteryWidget = settings.BluetoothBatteryWidget with { Enabled = true };
        var primary = MonitorBarProfilePolicy.Capture(settings, BarConfigurationTarget.Taskbar, "A");
        MonitorBarProfilePolicy.Store(settings, primary with
        { Resident = primary.Resident.Select(entry => entry.Component == TaskbarRestComponent.BluetoothBattery ? entry with { IsVisible = false } : entry).ToArray() });
        Assert.IsFalse(MonitorBarProfilePolicy.IsRequested(settings, TaskbarRestComponent.BluetoothBattery, ["A"]),
            "默认主屏有局部覆盖时，不能继续用旧全局开关请求采样。");
    }
    [TestMethod]
    public void FloatingSharedGeometryChangesCannotPublishOneScreensAlignment()
    {
        var previous = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new AppSettings();
            var root = SettingsManager.Current;
            root.FloatingBar = root.FloatingBar with { Profile = BarDisplaySettings.Capture(root) };
            MonitorBarProfilePolicy.Store(root, MonitorBarProfilePolicy.Capture(root, BarConfigurationTarget.FloatingBar, "A")
                with
            { BodyAlignment = TaskbarBarPosition.End });
            using var context = new SettingsPageContext();
            context.Initialize(new(SettingsMode.FloatingBar, "A", LayoutOrientation.Horizontal, false, true));
            var configuration = new LegacySettingsConfiguration(context);
            configuration.SetTaskbarPlacement(locked: true);
            configuration.SetLyricsFixedWidthDip(420);
            Assert.AreEqual(TaskbarBarPosition.Start, root.FloatingBar.Profile!.Position);
            Assert.AreEqual(420, root.FloatingBar.Profile.LyricsFixedWidthDip);
            configuration.Reset(SettingsResetScope.Spectrum);
            Assert.AreEqual(TaskbarBarPosition.Start, SettingsManager.Current.FloatingBar.Profile!.Position);
            Assert.AreEqual(TaskbarBarPosition.End, configuration.Current.Position);
            Assert.AreEqual(TaskbarBarPosition.Start,
                MonitorBarProfilePolicy.Project(SettingsManager.Current, BarConfigurationTarget.FloatingBar, "B").Position);
        }
        finally { SettingsManager.Current = previous; }
    }
    [TestMethod]
    public void FirstEditKeepsInactiveMetricPreferencesAndOtherScreensInherited()
    {
        var settings = new AppSettings();
        settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Cycle };
        var profile = MonitorBarProfilePolicy.Capture(settings, BarConfigurationTarget.Taskbar, "A");
        Assert.IsTrue(profile.Resident.Any(entry => ComponentLayoutPolicy.IsMetric(entry.Component)));
        Assert.AreEqual(0, settings.MonitorBarProfiles.Count, "读取不能创建记录。");
        var projected = MonitorBarProfilePolicy.Project(settings, BarConfigurationTarget.Taskbar, "A");
        var next = ComponentLayoutPolicy.SetVisible(projected, BarLayoutState.Resident, TaskbarRestComponent.Volume, true);
        MonitorBarProfilePolicy.SetLayout(settings, BarConfigurationTarget.Taskbar, "A", BarLayoutState.Resident, next.ComponentLayouts!.Resident!);
        bool Volume(BarConfigurationTarget target, string id) => ComponentLayoutPolicy.Resolve(
            MonitorBarProfilePolicy.Project(settings, target, id), BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Volume).IsVisible;
        Assert.IsTrue(Volume(BarConfigurationTarget.Taskbar, "a"));
        Assert.IsFalse(Volume(BarConfigurationTarget.Taskbar, "B"));
        Assert.IsFalse(Volume(BarConfigurationTarget.FloatingBar, "A"));
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Volume).IsVisible);
    }

    [TestMethod]
    public void ContentAndPlacementResetDoNotCrossScopesOrClearEachOther()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        try
        {
            SettingsManager.Current = new(); SettingsManager.SetUserDefaults(null);
            var root = SettingsManager.Current;
            var profile = MonitorBarProfilePolicy.Capture(root, BarConfigurationTarget.Taskbar, "A") with
            { BodyPrimaryDip = 400, BodyAlignment = TaskbarBarPosition.End, Detached = [new(TaskbarRestComponent.Spectrum, 200, 0)] };
            MonitorBarProfilePolicy.Store(root, profile);
            var b = MonitorBarProfilePolicy.Capture(root, BarConfigurationTarget.Taskbar, "B") with { BodyPrimaryDip = 900 };
            MonitorBarProfilePolicy.Store(root, b);
            using var context = new SettingsPageContext();
            context.Initialize(new(SettingsMode.Taskbar, "A", LayoutOrientation.Horizontal, false, true));
            var configuration = new LegacySettingsConfiguration(context);
            configuration.SetTaskbarExperience(ComponentLayoutPolicy.SetVisible(configuration.Current, BarLayoutState.Resident, TaskbarRestComponent.Volume, true));
            configuration.Reset(SettingsResetScope.Spectrum);
            Assert.AreEqual(TaskbarBarPosition.Start, SettingsManager.Current.Position, "重置局部参数不能将本屏对齐写入全局。");
            Assert.IsTrue(ComponentLayoutPolicy.Resolve(configuration.Current, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Volume).IsVisible);
            configuration.Reset(SettingsResetScope.RestState);
            var restored = MonitorBarProfilePolicy.Find(root, BarConfigurationTarget.Taskbar, "A")!;
            Assert.IsFalse(ComponentLayoutPolicy.Resolve(configuration.Current, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Volume).IsVisible);
            Assert.AreEqual(400d, restored.BodyPrimaryDip);
            Assert.AreEqual(1, restored.Detached.Count);
            configuration.SetTaskbarPlacement(reset: true);
            root = SettingsManager.Current;
            Assert.IsNull(MonitorBarProfilePolicy.Find(root, BarConfigurationTarget.Taskbar, "A")!.BodyPrimaryDip);
            Assert.AreEqual(900d, MonitorBarProfilePolicy.Find(root, BarConfigurationTarget.Taskbar, "B")!.BodyPrimaryDip);
        }
        finally { SettingsManager.Current = previous; SettingsManager.SetUserDefaults(previousDefaults); }
    }

    [TestMethod]
    public void CloneAndPersistentReloadPreserveBothModesWithoutAliasing()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            var directory = Path.Combine(Path.GetTempPath(), "AFMB-profile-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new SettingsPersistenceService(directory))
                {
                    store.Initialize();
                    foreach (var target in Enum.GetValues<BarConfigurationTarget>())
                        MonitorBarProfilePolicy.Store(SettingsManager.Current, MonitorBarProfilePolicy.Capture(SettingsManager.Current, target, "B") with
                        { Detached = [new(TaskbarRestComponent.Volume, -25.5, 700)], BodyPrimaryDip = 350 });
                    store.Flush();
                }
                using var loaded = new SettingsPersistenceService(directory);
                loaded.Initialize();
                var original = SettingsManager.Current;
                var clone = original.Clone();
                Assert.AreEqual(2, clone.MonitorBarProfiles.Count);
                Assert.AreNotSame(original.MonitorBarProfiles[0].Resident, clone.MonitorBarProfiles[0].Resident);
                Assert.AreNotSame(original.MonitorBarProfiles[0].Detached, clone.MonitorBarProfiles[0].Detached);
                Assert.AreEqual(-25.5, clone.MonitorBarProfiles[1].Detached[0].PrimaryDip);
                MonitorBarProfilePolicy.SetDetached(clone, BarConfigurationTarget.Taskbar, "B", TaskbarRestComponent.Volume, null);
                Assert.AreEqual(1, original.MonitorBarProfiles[0].Detached.Count);
            }
            finally
            {
                SettingsManager.Current = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            return Task.CompletedTask;
        });
    }
}
