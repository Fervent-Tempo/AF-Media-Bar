// 防止新三状态配置覆盖旧选择、切换模式丢序或参数重置误清布局；使用真实序列化与存储。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ComponentLayoutTests
{
    [TestMethod]
    public void ResidentTransportIsOptInAndEachButtonMovesIndependently()
    {
        var settings = new AppSettings();
        foreach (var component in ComponentLayoutPolicy.TransportComponents)
        {
            Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Single(entry => entry.Component == component).IsVisible);
            Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Idle).Any(entry => entry.Component == component));
            settings.TaskbarExperience = ComponentLayoutPolicy.SetVisible(settings, BarLayoutState.Resident, component, true);
            var order = ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Select(entry => entry.Component).ToArray();
            Assert.IsTrue(ComponentLayoutPolicy.TryMove(settings, BarLayoutState.Resident, component, 2, order, out var moved));
            settings.TaskbarExperience = moved;
            Assert.AreEqual(component, ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident)[2].Component);
            Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Hover).Single(entry => entry.Component == component).IsVisible);
        }
    }

    [TestMethod]
    public void LegacyGroupExpandsAtItsExplicitPositionAndUnconfiguredCycleIsLast()
    {
        var settings = new AppSettings();
        settings.PerformanceComponent = new([MetricKind.SystemMemory, MetricKind.SystemCpu], 1500, true)
        { DisplayMode = PerformanceDisplayMode.Parallel };
        settings.TaskbarExperience = settings.TaskbarExperience with
        { RestComponentOrder = [TaskbarRestComponent.OutputDevice, TaskbarRestComponent.Performance, TaskbarRestComponent.Spectrum], PerformanceVisible = true };
        var order = ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Select(entry => entry.Component).ToList();
        Assert.IsTrue(order.IndexOf(TaskbarRestComponent.OutputDevice) < order.IndexOf(TaskbarRestComponent.SystemMemory));
        Assert.IsTrue(order.IndexOf(TaskbarRestComponent.SystemCpu) < order.IndexOf(TaskbarRestComponent.Spectrum));
        Assert.IsFalse(order.Contains(TaskbarRestComponent.Performance));
        settings.TaskbarExperience = settings.TaskbarExperience with { RestComponentOrder = null };
        settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Cycle };
        Assert.AreEqual(TaskbarRestComponent.Performance, ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Last().Component);
    }

    [TestMethod]
    public void DisabledRowsKeepTheirPositionAcrossModesAndStatesRejectStaleMoves()
    {
        var settings = new AppSettings();
        settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Parallel };
        settings.TaskbarExperience = ComponentLayoutPolicy.SetVisible(settings, BarLayoutState.Resident, TaskbarRestComponent.SystemCpu, false);
        var expected = ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Select(entry => entry.Component).ToArray();
        Assert.IsTrue(ComponentLayoutPolicy.TryMove(settings, BarLayoutState.Resident, TaskbarRestComponent.SystemCpu, 2, expected, out var updated));
        settings.TaskbarExperience = updated;
        Assert.AreEqual(TaskbarRestComponent.SystemCpu, ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident)[2].Component);
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident)[2].IsVisible);
        Assert.IsFalse(ComponentLayoutPolicy.TryMove(settings, BarLayoutState.Resident, TaskbarRestComponent.SystemCpu, 3, expected, out _));
        settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Cycle };
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Any(entry => entry.Component == TaskbarRestComponent.SystemCpu));
        settings.PerformanceComponent = settings.PerformanceComponent with { DisplayMode = PerformanceDisplayMode.Parallel };
        Assert.AreEqual(TaskbarRestComponent.SystemCpu, ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident)[2].Component);
        CollectionAssert.AreEqual(new[] { TaskbarRestComponent.Artwork }, ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Idle).Where(entry => entry.IsVisible).Select(entry => entry.Component).ToArray());
    }

    [TestMethod]
    public void EmptyIdleAndIndependentTransportButtonsDoNotChangeOtherStates()
    {
        var settings = new AppSettings();
        settings.TaskbarExperience = settings.TaskbarExperience with { ComponentLayouts = new() { Idle = [] } };
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Idle).Any(entry => entry.IsVisible));
        settings.TaskbarExperience = ComponentLayoutPolicy.SetVisible(settings, BarLayoutState.Hover, TaskbarRestComponent.PreviousTrack, true);
        Assert.IsTrue(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Hover).Single(entry => entry.Component == TaskbarRestComponent.PreviousTrack).IsVisible);
        Assert.IsFalse(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Hover).Single(entry => entry.Component == TaskbarRestComponent.NextTrack).IsVisible);
        Assert.IsTrue(ComponentLayoutPolicy.Resolve(settings, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.MediaText).IsVisible);
    }

    [TestMethod]
    public void RealStorageAndResetKeepLayoutsIndependentFromComponentParameters()
    {
        StaTest.Run(_ =>
        {
            var old = SettingsManager.Current.Clone();
            var oldDefaults = SettingsManager.UserDefaults?.Clone();
            var directory = Path.Combine(Path.GetTempPath(), "afmb-layouts-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new SettingsPersistenceService(directory))
                {
                    store.Initialize();
                    SettingsManager.Current.TaskbarExperience = ComponentLayoutPolicy.SetVisible(SettingsManager.Current, BarLayoutState.Resident, TaskbarRestComponent.Spectrum, false);
                    foreach (var component in ComponentLayoutPolicy.TransportComponents)
                        SettingsManager.Current.TaskbarExperience = ComponentLayoutPolicy.SetVisible(SettingsManager.Current, BarLayoutState.Resident, component, true);
                    store.Flush();
                }
                using var loaded = new SettingsPersistenceService(directory);
                loaded.Initialize();
                var layout = SettingsManager.Current.TaskbarExperience.ComponentLayouts!;
                Assert.IsFalse(ComponentLayoutPolicy.Resolve(SettingsManager.Current, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Spectrum).IsVisible);
                Assert.AreNotSame(layout.Resident, SettingsManager.Current.Clone().TaskbarExperience.ComponentLayouts!.Resident);
                foreach (var component in ComponentLayoutPolicy.TransportComponents)
                    Assert.IsTrue(ComponentLayoutPolicy.Resolve(SettingsManager.Current, BarLayoutState.Resident).Single(entry => entry.Component == component).IsVisible);
                SettingsManager.SetUserDefaults(SettingsManager.Current.Clone());
                SettingsManager.Current.TaskbarExperience = ComponentLayoutPolicy.SetVisible(SettingsManager.Current, BarLayoutState.Resident, TaskbarRestComponent.PlayPause, false);
                SettingsManager.ResetRestState();
                Assert.IsTrue(ComponentLayoutPolicy.Resolve(SettingsManager.Current, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.PlayPause).IsVisible);
                SettingsManager.SetUserDefaults(null);
                SettingsManager.ResetComponents();
                Assert.IsFalse(ComponentLayoutPolicy.Resolve(SettingsManager.Current, BarLayoutState.Resident).Single(entry => entry.Component == TaskbarRestComponent.Spectrum).IsVisible);
                SettingsManager.ResetRestState();
                Assert.IsNull(SettingsManager.Current.TaskbarExperience.ComponentLayouts!.Resident);
                Assert.IsNull(SettingsManager.Current.TaskbarExperience.ComponentLayouts.Idle);
            }
            finally
            {
                SettingsManager.Current = old; SettingsManager.SetUserDefaults(oldDefaults);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            return Task.CompletedTask;
        });
    }
}
