// 覆盖工作区预算、配置开关和真实工具窗口恢复；不加载用户设置。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class FloatingBarTests
{
    [TestMethod]
    public void FourEdgeAnchorsSurviveResizeAndClipToTheirOwnWorkArea()
    {
        var work = new Size(1000, 800);
        foreach (var edge in new[] { FloatingBarDockEdge.Left, FloatingBarDockEdge.Right, FloatingBarDockEdge.Top, FloatingBarDockEdge.Bottom })
        {
            foreach (var size in new[] { new Size(240, 44), new Size(440, 48), new Size(48, 300) })
            {
                var bounds = FloatingBarGeometryPolicy.Resolve(work, size, new Point(500, 400));
                var anchored = FloatingBarEdgeCollapsePolicy.Anchor(bounds, work, edge);
                Assert.AreEqual(edge, FloatingBarEdgeCollapsePolicy.DetectEdge(anchored, work, size.Height > size.Width));
                var collapsed = FloatingBarEdgeCollapsePolicy.CollapsedBounds(anchored, edge);
                var visible = Rect.Intersect(collapsed, new Rect(work));
                Assert.AreEqual(6d, edge is FloatingBarDockEdge.Left or FloatingBarDockEdge.Right ? visible.Width : visible.Height);
                Assert.IsTrue(new Rect(work).Contains(visible));
            }
        }
        var corner = new Rect(0, 0, 200, 44);
        Assert.AreEqual(FloatingBarDockEdge.Top, FloatingBarEdgeCollapsePolicy.DetectEdge(corner, work, false));
        Assert.AreEqual(FloatingBarDockEdge.Left, FloatingBarEdgeCollapsePolicy.DetectEdge(corner, work, true));
    }

    [TestMethod]
    public void PointerActivationIsBoundedToMonitorAndExpandedStateHasHysteresis()
    {
        var work = new Size(1000, 800);
        var normal = new Rect(0, 200, 240, 44);
        Assert.IsTrue(FloatingBarEdgeCollapsePolicy.ShouldExpand(normal, work, new Point(70, 180), FloatingBarDockEdge.Left));
        Assert.IsFalse(FloatingBarEdgeCollapsePolicy.ShouldExpand(normal, work, new Point(-1, 220), FloatingBarDockEdge.Left), "相邻屏幕不能唤醒本屏折叠条。");
        Assert.IsFalse(FloatingBarEdgeCollapsePolicy.ShouldExpand(normal, work, new Point(20, 500), FloatingBarDockEdge.Left));
        var nearby = new Point(260, 220);
        Assert.IsTrue(FloatingBarEdgeCollapsePolicy.ShouldCollapse(normal, nearby, false));
        Assert.IsFalse(FloatingBarEdgeCollapsePolicy.ShouldCollapse(normal, nearby, true));
        Assert.IsTrue(FloatingBarEdgeCollapsePolicy.ShouldCollapse(normal, new Point(400, 400), true));
    }

    [TestMethod]
    public void ResizeAndOrientationKeepCenterUntilClampingIsNecessary()
    {
        var work = new Size(1000, 800);
        var center = new Point(500, 400);
        Assert.AreEqual(new Rect(300, 378, 400, 44), FloatingBarGeometryPolicy.Resolve(work, new Size(400, 44), center));
        Assert.AreEqual(new Rect(476, 200, 48, 400), FloatingBarGeometryPolicy.Resolve(work, new Size(48, 400), center));
        Assert.AreEqual(new Rect(0, 0, 1000, 800), FloatingBarGeometryPolicy.Resolve(work, new Size(2000, 900), new Point(-200, -100)));
        var edge = FloatingBarGeometryPolicy.Resolve(work, new Size(400, 44), new Point(990, 799));
        Assert.AreEqual(1000d, edge.Right);
        Assert.AreEqual(800d, edge.Bottom);
    }

    [TestMethod]
    public void FloatingModeIgnoresLegacyExperimentAndPreservesProfile()
    {
        var previous = SettingsManager.Current;
        using var context = new SettingsPageContext();
        var configuration = new LegacySettingsConfiguration(context);
        try
        {
            SettingsManager.Current = new AppSettings();
            Assert.IsTrue(SettingsManager.Current.FloatingBar.EdgeCollapseEnabled);
            configuration.SetExperiments(new() { FloatingBar = true });
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode, "Enabling alone must not switch.");
            ((AFMediaBar.Classes.Abstractions.ISettingsConfiguration)configuration).SetWindowMode(WindowMode.FloatingBar);
            Assert.AreEqual(WindowMode.FloatingBar, SettingsManager.Current.Normalize().WindowMode);
            var profile = SettingsManager.Current.FloatingBar;
            configuration.SetExperiments(new());
            Assert.AreEqual(WindowMode.FloatingBar, SettingsManager.Current.WindowMode);
            Assert.AreEqual(WindowMode.FloatingBar, SettingsManager.Current.Normalize().WindowMode);
            Assert.AreSame(profile, SettingsManager.Current.FloatingBar);
            var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""
                {"WindowMode":2,"Experiments":{"FloatingBar":false,"VerticalTaskbar":false},
                 "FloatingBar":{"Positions":[{"MonitorDeviceId":"old","CenterXDip":350,"CenterYDip":200}]}}
                """)!.Normalize();
            Assert.AreEqual(WindowMode.FloatingBar, old.WindowMode);
            Assert.IsTrue(old.FloatingBar.EdgeCollapseEnabled);
            Assert.AreEqual(FloatingBarDockEdge.None, old.FloatingBar.Positions.Single().DockEdge);
            Assert.AreEqual(350d, old.FloatingBar.Positions.Single().CenterXDip);
            SettingsManager.Current.WindowMode = WindowMode.DynamicIsland;
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.Normalize().WindowMode);
        }
        finally { SettingsManager.Current = previous; }
    }

    [TestMethod]
    public void PlacementResetUsesFloatingDefaultsAndLeavesTaskbarUntouched()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            var previousDefaults = SettingsManager.UserDefaults;
            using var context = new SettingsPageContext();
            context.Initialize(SettingsContext.Initial with { Mode = SettingsMode.FloatingBar });
            try
            {
                SettingsManager.Current = new AppSettings
                {
                    Experiments = new() { FloatingBar = true },
                    TaskbarBarManualPadding = 50,
                    FloatingBar = new() { OverlayTaskbar = true, Positions = [new("one", 400, 500)], MonitorDeviceIds = ["one"] }
                };
                SettingsManager.SetUserDefaults(new AppSettings { FloatingBar = new() { EdgeCollapseEnabled = false, Positions = [new("two", 300, 200) { DockEdge = FloatingBarDockEdge.Left }], MonitorDeviceIds = ["two"] } });
                var editor = new LegacySettingsConfiguration(context);
                using var localization = new LocalizationService();
                using var viewModel = new DisplayModesViewModel(new DisplayMonitorService(), localization, editor);
                var notifications = new List<string?>();
                viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
                editor.Reset(SettingsResetScope.Layout);
                Assert.AreEqual(50, SettingsManager.Current.TaskbarBarManualPadding);
                Assert.AreEqual("two", SettingsManager.Current.FloatingBar.MonitorDeviceIds.Single());
                Assert.AreEqual(300d, SettingsManager.Current.FloatingBar.Positions.Single().CenterXDip);
                Assert.IsFalse(SettingsManager.Current.FloatingBar.EdgeCollapseEnabled);
                Assert.AreEqual(FloatingBarDockEdge.Left, SettingsManager.Current.FloatingBar.Positions.Single().DockEdge);
                Assert.IsFalse(SettingsManager.Current.FloatingBar.OverlayTaskbar);
                Assert.IsTrue(notifications.Contains(nameof(DisplayModesViewModel.FloatingOverlayTaskbar)),
                    "重置必须通知页面刷新覆盖任务栏开关。");
                SettingsManager.SetUserDefaults(new AppSettings { FloatingBar = new() { OverlayTaskbar = true } });
                notifications.Clear();
                editor.Reset(SettingsResetScope.Layout);
                Assert.IsTrue(viewModel.FloatingOverlayTaskbar, "重置也必须恢复用户保存的开启值。");
                Assert.IsTrue(notifications.Contains(nameof(DisplayModesViewModel.FloatingOverlayTaskbar)));
            }
            finally { SettingsManager.Current = previous; SettingsManager.SetUserDefaults(previousDefaults); }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DesktopWidgetRestoresMinimizedWindowWithoutActivatingAndStopsAfterDetach()
    {
        StaTest.Run(async _ =>
        {
            using var zOrder = new DesktopWidgetZOrderService();
            var window = new Window
            {
                Width = 180,
                Height = 70,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                Content = new Button { Content = "Widget input" }
            };
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            using var registration = zOrder.Attach(handle, FloatingBarLayer.Desktop, () => true, false);
            try
            {
                var foreground = NativeMethods.GetForegroundWindow();
                NativeMethods.ShowWindow(handle, 6);
                await Task.Delay(600);
                Assert.IsFalse(NativeMethods.IsIconic(handle));
                Assert.IsTrue(NativeMethods.IsWindowVisible(handle));
                Assert.AreEqual(foreground, NativeMethods.GetForegroundWindow(), "Restoring must not steal focus.");
                Assert.IsNotNull(window.InputHitTest(new Point(80, 30)));
                registration.Dispose();
                NativeMethods.ShowWindow(handle, 6);
                await Task.Delay(600);
                Assert.IsTrue(NativeMethods.IsIconic(handle), "Retired widgets must not be resurrected.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void OverlayTaskbarDefaultsOffAndSurvivesNormalization()
    {
        Assert.IsFalse(new FloatingBarSettings().OverlayTaskbar, "旧配置缺字段时默认关闭，不能改变既有悬浮条行为。");
        var settings = new FloatingBarSettings { Layer = FloatingBarLayer.Desktop, OverlayTaskbar = true };
        Assert.IsTrue(settings.Normalize().OverlayTaskbar, "覆盖任务栏与窗口层级相互独立，Normalize 不能丢掉这个选择。");
    }
}
