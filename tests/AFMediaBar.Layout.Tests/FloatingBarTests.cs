// 覆盖工作区预算、配置开关和真实工具窗口恢复；不加载用户设置。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class FloatingBarTests
{
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
    public void FloatingModeRequiresExperimentAndClosingItPreservesProfile()
    {
        var previous = SettingsManager.Current;
        using var context = new SettingsPageContext();
        var configuration = new LegacySettingsConfiguration(context);
        try
        {
            SettingsManager.Current = new AppSettings { WindowMode = WindowMode.FloatingBar };
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.Normalize().WindowMode);
            configuration.SetExperiments(new() { FloatingBar = true });
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode, "Enabling alone must not switch.");
            ((AFMediaBar.Classes.Abstractions.ISettingsConfiguration)configuration).SetWindowMode(WindowMode.FloatingBar);
            Assert.AreEqual(WindowMode.FloatingBar, SettingsManager.Current.Normalize().WindowMode);
            var profile = SettingsManager.Current.FloatingBar;
            configuration.SetExperiments(new());
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
            Assert.AreSame(profile, SettingsManager.Current.FloatingBar);
            SettingsManager.Current.WindowMode = WindowMode.DynamicIsland;
            Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.Normalize().WindowMode);
        }
        finally { SettingsManager.Current = previous; }
    }

    [TestMethod]
    public void PlacementResetUsesFloatingDefaultsAndLeavesTaskbarUntouched()
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
                FloatingBar = new() { Positions = [new("one", 400, 500)], MonitorDeviceIds = ["one"] }
            };
            SettingsManager.SetUserDefaults(new AppSettings { FloatingBar = new() { Positions = [new("two", 300, 200)], MonitorDeviceIds = ["two"] } });
            var editor = new LegacySettingsConfiguration(context);
            editor.Reset(SettingsResetScope.Layout);
            Assert.AreEqual(50, SettingsManager.Current.TaskbarBarManualPadding);
            Assert.AreEqual("two", SettingsManager.Current.FloatingBar.MonitorDeviceIds.Single());
            Assert.AreEqual(300d, SettingsManager.Current.FloatingBar.Positions.Single().CenterXDip);
        }
        finally { SettingsManager.Current = previous; SettingsManager.SetUserDefaults(previousDefaults); }
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
            using var registration = zOrder.Attach(handle, FloatingBarLayer.Desktop, () => true);
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
}
