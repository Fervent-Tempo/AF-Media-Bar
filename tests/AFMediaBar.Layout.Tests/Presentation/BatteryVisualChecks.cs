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
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Views.Pages;
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
            var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = 0, Y = 0 }, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            Assert.IsTrue(NativeMethods.GetMonitorInfo(monitor, ref info));
            flyout.ShowBatteryOverview(model, new(info.rcWork.Left + 300, info.rcWork.Bottom - 20, info.rcWork.Left + 340, info.rcWork.Bottom));
            flyout.Opacity = 0;
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(250));
            var panel = (FrameworkElement)flyout.FindName("BatteryPanel");
            var list = (ListBox)flyout.FindName("BatteryList");
            Assert.AreEqual(4, list.Items.Count);
            // 异步加载从一行增长为四行时，窗口必须重新贴靠而不能从旧位置向屏幕外增长。
            model.Devices.Clear(); model.Devices.Add(new(readings[0]));
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(50));
            foreach (var reading in readings.Skip(1)) model.Devices.Add(new(reading));
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
            NativeMethods.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(flyout).Handle, out var rect);
            Assert.IsTrue(rect.Bottom <= info.rcWork.Bottom && rect.Top >= info.rcWork.Top, "增长后的窗口必须留在工作区。");
            Assert.AreEqual(156d, list.ActualHeight, 1d, "最多显示三个 52 DIP 设备行。");
            var scroll = Descendants(list).OfType<ScrollViewer>().Single();
            var rail = Descendants(scroll).OfType<System.Windows.Controls.Primitives.ScrollBar>().Single();
            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsTrue(first.TranslatePoint(new Point(first.ActualWidth, 0), scroll).X <= rail.TranslatePoint(new Point(), scroll).X,
                "设备行不能占用滚动条的独立列。");
            scroll.ScrollToBottom(); SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(50));
            Assert.IsTrue(scroll.VerticalOffset > 0, "第四台设备必须可滚动查看。");
            Capture(panel, $"battery-{(dark ? "dark" : "light")}-overview-scrolled", dark, 144);
            scroll.ScrollToTop(); SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(50));
            Capture(panel, $"battery-{(dark ? "dark" : "light")}-overview", dark, 144);
            flyout.Dismiss();
        }
        VerifySettings(battery, localization);
        control.DisposePresentation();
    }
    private static void VerifySettings(BluetoothBatteryService battery, LocalizationService localization)
    {
        Application.Current.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        foreach (var resource in new[] { "SettingsDiagrams", "SettingsComponentResources" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative) });
        using var context = new SettingsPageContext();
        var configuration = new LegacySettingsConfiguration(context);
        using var editor = new BluetoothBatterySettingsViewModel(battery, configuration, localization);
        var page = new BluetoothBatteryPage(new WidgetsSettingsViewModel(null!, editor, null!, configuration, null!));
        var window = new Window { Content = page, Width = 820, Height = 900, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0, WindowStyle = WindowStyle.None };
        using var messages = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(messages);
        var trace = System.Diagnostics.PresentationTraceSources.DataBindingSource;
        var previous = trace.Switch.Level; trace.Switch.Level = System.Diagnostics.SourceLevels.Error; trace.Listeners.Add(listener);
        try
        {
            window.Show(); editor.Activate();
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(300));
            Assert.AreEqual(4, editor.DisplayDevices.Count);
            editor.ReorderDeviceCommand.Execute(new SettingsReorderRequest(editor.DisplayDevices[2], 0, editor.DisplayDevices));
            Assert.AreEqual("headset", SettingsManager.Current.BluetoothBatteryWidget.GetSelectedDevices()[0].Id);
            foreach (var dark in new[] { false, true })
            {
                var theme = dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
                new ApplicationAppearanceResources(Application.Current.Resources).Apply(SettingsManager.Current.Appearance, theme,
                    AccentColorPolicy.Build(Color.FromRgb(0, 120, 212), dark, false));
                var layout = (SettingsPageLayout)page.FindName("PageLayout");
                Assert.IsNotNull(layout.ScrollViewer);
                editor.ResetFillColorCommand.Execute(null);
                layout.ScrollViewer.ScrollToTop(); SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
                Capture(page, $"battery-{(dark ? "dark" : "light")}-settings", dark, 96);
                editor.UseCustomFillColor = true;
                var previousColor = editor.FillColor;
                editor.FillColorHex = "#42";
                Assert.AreEqual("#42", editor.FillColorHex, "未输入完整的色值应保留在编辑框中。");
                Assert.AreEqual(previousColor, editor.FillColor, "未输入完整的色值不能改变生效颜色。");
                editor.FillColorHex = "#42CBA6";
                SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(50));
                Assert.IsTrue(Descendants(page).OfType<BatteryPresenter>().All(p => p.Accent is SolidColorBrush brush && brush.Color == Color.FromRgb(66, 203, 166)));
                Capture(page, $"battery-{(dark ? "dark" : "light")}-settings-custom-color", dark, 96);
                layout.ScrollViewer.ScrollToBottom(); SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
                Capture(page, $"battery-{(dark ? "dark" : "light")}-settings-color-notifications", dark, 96);
                window.Width = 560;
                layout.ScrollViewer.ScrollToTop(); SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
                Capture(page, $"battery-{(dark ? "dark" : "light")}-settings-narrow", dark, 96);
                window.Width = 820;
            }
            listener.Flush(); Assert.AreEqual(string.Empty, messages.ToString(), "电量设置页绑定错误。");
        }
        finally { window.Close(); editor.Deactivate(); trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
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
