using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 显示模式页的模式选择必须真的切换运行模式，并在重启后保持；未实现的两种模式只切换页面预览。
/// 未实现模式那两条用例原先与设置持久化测试放在一起，现随本主题一起搬到这个文件：它们保护的是同一条不变量
/// （未实现的模式不写 WindowMode），断言一字未改。
/// A display-mode selection on the settings page has to really switch the running mode and survive a restart; the two
/// unimplemented modes only move the page preview. The two unimplemented-mode cases used to sit with the settings
/// persistence tests and moved here with their subject: they guard the same invariant — an unimplemented mode never
/// writes WindowMode — and none of their assertions changed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DisplayModeSwitchTests
{
    [TestCleanup]
    public void TearDown() => SettingsManager.ResetAll();

    [TestMethod]
    public void SelectingDynamicIslandSwitchesTheRunningModeAndPublishesLayoutChange()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var published = new List<WindowMode>();
        EventHandler<LayoutSettingsChangedEventArgs> handler = (_, e) => published.Add(e.WindowMode);
        SettingsManager.LayoutSettingsChanged += handler;
        try
        {
            viewModel.SwitchToDynamicIslandModeCommand.Execute(null);
        }
        finally { SettingsManager.LayoutSettingsChanged -= handler; }

        Assert.AreEqual(DisplayModeSelection.DynamicIsland, viewModel.SelectedMode);
        Assert.IsTrue(viewModel.IsDynamicIslandMode);
        Assert.IsFalse(viewModel.IsUnimplementedMode, "灵动岛已实现，不得再被当成未实现模式。");
        Assert.IsTrue(viewModel.IsDynamicIslandHostingActive, "卡片上的“当前模式”芯片读真实运行模式，必须已经亮起。");
        Assert.IsFalse(viewModel.IsTaskbarHostingActive);
        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        CollectionAssert.AreEqual(new[] { WindowMode.DynamicIsland }, published.ToArray());
    }

    [TestMethod]
    public void SelectingTaskbarSwitchesBackFromDynamicIsland()
    {
        SettingsManager.Replace(new AppSettings { WindowMode = WindowMode.DynamicIsland });
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var published = new List<WindowMode>();
        EventHandler<LayoutSettingsChangedEventArgs> handler = (_, e) => published.Add(e.WindowMode);
        SettingsManager.LayoutSettingsChanged += handler;
        try
        {
            viewModel.SwitchToTaskbarModeCommand.Execute(null);
        }
        finally { SettingsManager.LayoutSettingsChanged -= handler; }

        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.IsTrue(viewModel.IsTaskbarMode);
        Assert.IsTrue(viewModel.IsTaskbarHostingActive);
        CollectionAssert.AreEqual(new[] { WindowMode.Taskbar }, published.ToArray());
    }

    [TestMethod]
    public void SelectingTheRunningModeAgainDoesNotPublishALayoutChange()
    {
        // 已经运行在灵动岛上时再点一次灵动岛卡片：高亮不动，也不得再广播一次——订阅方会据此重建承载窗口，
        // 重复广播等于白发一次重建。
        // Clicking the island card while the island is already running changes nothing and must not publish again: the
        // subscribers rebuild the hosting window, so a re-publish would rebuild it for nothing.
        SettingsManager.Replace(new AppSettings { WindowMode = WindowMode.DynamicIsland });
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var published = new List<WindowMode>();
        EventHandler<LayoutSettingsChangedEventArgs> handler = (_, e) => published.Add(e.WindowMode);
        SettingsManager.LayoutSettingsChanged += handler;
        try
        {
            viewModel.SwitchToDynamicIslandModeCommand.Execute(null);
        }
        finally { SettingsManager.LayoutSettingsChanged -= handler; }

        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        Assert.AreEqual(0, published.Count);
    }

    [TestMethod]
    public void PageHighlightStartsFromTheRealRunningMode()
    {
        SettingsManager.Replace(new AppSettings { WindowMode = WindowMode.DynamicIsland });
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());

        Assert.AreEqual(DisplayModeSelection.DynamicIsland, viewModel.SelectedMode);
        Assert.IsTrue(viewModel.IsDynamicIslandMode);
        Assert.IsFalse(viewModel.IsTaskbarMode);
        Assert.IsTrue(viewModel.IsDynamicIslandHostingActive);
        Assert.IsFalse(viewModel.IsUnimplementedMode);
    }

    [TestMethod]
    public void UnimplementedDisplayModeSelectionDoesNotChangeRuntimeModeOrTaskbarSettings()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var original = SettingsManager.Current.TaskbarExperience;

        viewModel.SwitchToFloatingBallModeCommand.Execute(null);
        viewModel.HoverLayerEnabled = false;

        Assert.IsTrue(viewModel.IsFloatingBallMode);
        Assert.IsTrue(viewModel.IsUnimplementedMode);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(original, SettingsManager.Current.TaskbarExperience, "高亮未实现模式时任务栏专属设置不接受写入。");

        viewModel.SwitchToDesktopCardModeCommand.Execute(null);

        Assert.IsTrue(viewModel.IsDesktopCardMode);
        Assert.IsTrue(viewModel.IsUnimplementedMode);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(original, SettingsManager.Current.TaskbarExperience, "高亮未实现模式时任务栏专属设置不接受写入。");
    }

    /// <summary>
    /// 灵动岛外观写在 <c>DynamicIslandSurface</c> 上，与任务栏外观互不影响；任务栏专属设置同样不会被它带着改。
    /// The island appearance lives on <c>DynamicIslandSurface</c> and leaves the taskbar appearance alone; the
    /// taskbar-only settings are not dragged along either.
    /// </summary>
    [TestMethod]
    public void IslandAppearanceWritesOnlyTheIslandSurface()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var taskbarSurface = SettingsManager.Current.TaskbarSurface;
        var taskbarExperience = SettingsManager.Current.TaskbarExperience;

        viewModel.IslandSurfaceStyle = PlayerSurfaceStyle.ThemeTint;
        viewModel.IslandSurfaceOpacityPercent = 55;
        viewModel.IslandSurfaceCornerRadiusDip = 9;

        Assert.AreEqual(PlayerSurfaceStyle.ThemeTint, SettingsManager.Current.DynamicIslandSurface.Style);
        Assert.AreEqual(55, SettingsManager.Current.DynamicIslandSurface.BackgroundOpacityPercent);
        Assert.AreEqual(9, SettingsManager.Current.DynamicIslandSurface.CornerRadiusDip);
        Assert.AreEqual(taskbarSurface, SettingsManager.Current.TaskbarSurface);
        Assert.AreEqual(taskbarExperience, SettingsManager.Current.TaskbarExperience);
    }

    private static DisplayMonitorInfo Monitor(string id, bool primary) =>
        new(id, id, primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 96, 96);

    private sealed class FakeDisplayMonitorService : IDisplayMonitorService
    {
        private readonly IReadOnlyList<DisplayMonitorInfo> _monitors =
            [Monitor("DISPLAY1", true), Monitor("DISPLAY2", false)];

        public event EventHandler? MonitorsChanged;

        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => _monitors;

        public void Refresh() => MonitorsChanged?.Invoke(this, EventArgs.Empty);

        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) =>
            DisplayTargetPolicy.ResolveFixed(_monitors, deviceId);

        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) =>
            ResolveFixedMonitor(fixedDeviceId);

        public bool IsForegroundWindowFullscreen() => false;
    }
}
