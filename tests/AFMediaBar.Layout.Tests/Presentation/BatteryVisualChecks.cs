// 复用隔离窗口，用固定快照验证电量表面与封面位移；可选导出真实控件的预览图，不改用户配置。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class BatteryVisualChecks
{
    internal static void Verify(TaskBarMediaControl control)
    {
        using var log = new AppLogService(Path.Combine(Path.GetTempPath(), "AFMB-BatteryVisualLogs"));
        using var battery = new BluetoothBatteryService(new([]), new(), null!, log);
        using var localization = new LocalizationService();
        localization.Apply(InterfaceLanguage.SimplifiedChinese);
        var readings = new[]
        {
            new BluetoothBatterySnapshot("mouse", 82, BluetoothBatterySource.Hid, true, true, DateTimeOffset.UtcNow) { DeviceName = "INPHIC IN10", Kind = BatteryDeviceKind.Mouse, Transport = BatteryTransport.Receiver },
            new BluetoothBatterySnapshot("keyboard", 38, BluetoothBatterySource.Hid, true, true, DateTimeOffset.UtcNow) { DeviceName = "Wireless Keyboard", Kind = BatteryDeviceKind.Keyboard, Transport = BatteryTransport.Receiver, IsCharging = true },
            new BluetoothBatterySnapshot("headset", 16, BluetoothBatterySource.WindowsProperty, true, true, DateTimeOffset.UtcNow) { DeviceName = "HUAWEI FreeBuds 6i", Kind = BatteryDeviceKind.Headset, FreshnessKnown = false },
            new BluetoothBatterySnapshot("controller", null, null, true, false, null) { DeviceName = "DualSense Wireless Controller", Kind = BatteryDeviceKind.Controller }
        };
        // 唯一测试注入点：不启动平台读取循环，只交付不可变快照。
        typeof(BluetoothBatteryService).GetProperty(nameof(BluetoothBatteryService.Readings))!.SetValue(battery, readings);
        SettingsManager.Current.Experiments = SettingsManager.Current.Experiments with { BluetoothBattery = true };
        SettingsManager.Current.BluetoothBatteryWidget = new() { Enabled = true, DeviceId = "headset", PresentationMode = BatteryPresentationMode.Parallel,
            SelectedDevices = readings.Select(r => new BatterySelectedDevice(r.DeviceId, r.DeviceName)).ToArray() };
        control.BindBatterySource(battery);
        var host = Window.GetWindow(control);
        host.WindowStyle = WindowStyle.None; host.ResizeMode = ResizeMode.NoResize;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Invoke(string method, params object[] args) => MediaBarTestAccess.GetMethod(method, flags).Invoke(control, args);
        foreach (var dark in new[] { false, true })
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
            var foreground = dark ? Brushes.White : Brushes.Black;
            ((TextBlock)control.FindName("SongTitle")).Foreground = foreground;
            foreach (var style in Enum.GetValues<BatteryVisualStyle>())
            foreach (var vertical in new[] { false, true })
            {
                SettingsManager.Current.BluetoothBatteryWidget = SettingsManager.Current.BluetoothBatteryWidget with { VisualStyle = style, PresentationMode = BatteryPresentationMode.Parallel };
                control.ApplyLayout(WindowMode.Taskbar, vertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal, 100, 100, 44);
                control.ApplyPrimaryLength(580);
                control.Width = host.Width = vertical ? 44 : 580; control.Height = host.Height = vertical ? 580 : 44;
                control.ApplyTaskbarExperienceSettings();
                control.Measure(new Size(control.Width, control.Height)); control.Arrange(new Rect(0, 0, control.Width, control.Height)); control.UpdateLayout();
                var surfaces = Descendants(control).OfType<BatteryPresenter>().Select(p => (Border)p.Parent).Where(s => s.Visibility == Visibility.Visible).ToArray();
                Assert.AreEqual(4, surfaces.Length, "并列设备应各有独立布局与命中表面。");
                Assert.IsTrue(surfaces.All(s => s.ActualWidth > 0 && s.ActualHeight > 0));
                foreach (var surface in surfaces)
                {
                    var point = surface.TranslatePoint(new Point(), control);
                    Assert.IsTrue(point.X >= -1 && point.Y >= -1 && point.X + surface.ActualWidth <= control.Width + 1 && point.Y + surface.ActualHeight <= control.Height + 1,
                        $"{style}/{vertical}: device outside bar {point}, size {surface.RenderSize}, bar {control.RenderSize}");
                }
                Invoke("BuildArtworkZoomPushTransforms");
                Invoke("AnimateArtworkZoomPush", 20d, TimeSpan.Zero, null!);
                foreach (var surface in surfaces)
                {
                    var movement = surface.RenderTransform.Value;
                    Assert.AreEqual(20d, vertical ? movement.OffsetY : movement.OffsetX, .01, "每台电量设备必须随封面避让。");
                }
                Invoke("ResetArtworkZoomPush");
                Assert.IsTrue(surfaces.All(s => s.RenderTransform.Value.OffsetX == 0 && s.RenderTransform.Value.OffsetY == 0));
                Capture(control, $"battery-{(dark ? "dark" : "light")}-{style}-{(vertical ? "vertical" : "horizontal")}", dark, 144);
            }
            SettingsManager.Current.BluetoothBatteryWidget = SettingsManager.Current.BluetoothBatteryWidget with { PresentationMode = BatteryPresentationMode.Cycle };
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 44);
            control.Width = host.Width = 580; control.Height = host.Height = 44; control.ApplyTaskbarExperienceSettings();
            control.Measure(new Size(580, 44)); control.Arrange(new Rect(0, 0, 580, 44)); control.UpdateLayout();
            Assert.AreEqual(1, Descendants(control).OfType<BatteryPresenter>().Count(p => ((Border)p.Parent).Visibility == Visibility.Visible));
            Capture(control, $"battery-{(dark ? "dark" : "light")}-cycle", dark, 96);
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            using var model = new DeviceBatteryOverviewViewModel(battery, localization);
            using var flyout = new TaskbarCompactFlyoutWindow(appearance);
            flyout.ShowBatteryOverview(model, new(600, 600, 601, 601));
            flyout.Opacity = 0;
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(250));
            var panel = (FrameworkElement)flyout.FindName("BatteryPanel");
            Assert.AreEqual(4, ((ItemsControl)flyout.FindName("BatteryList")).Items.Count);
            Capture(panel, $"battery-{(dark ? "dark" : "light")}-overview", dark, 144);
            flyout.Dismiss();
        }
        control.DisposePresentation();
    }
    private static void Capture(FrameworkElement element, string name, bool dark, double dpi)
    {
        if (Environment.GetEnvironmentVariable("AFMB_LAYOUT_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var size = element.RenderSize;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : new SolidColorBrush(Color.FromRgb(245, 245, 245)), null, new Rect(size));
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi / 96), (int)Math.Ceiling(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
