// 经真实设置事件与已加载窗口验证即时材质更新；只替换原生调用，结束恢复全局状态。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Layout.Tests;

internal static class WindowBackdropRefreshChecks
{
    internal static void VerifyLiveSelection(Application app)
    {
        var original = SettingsManager.Current;
        var originalMain = app.MainWindow;
        var calls = new List<(nint Handle, int Attribute, int Value)>();
        var failMaterial = false;
        using var icons = new AppIconService();
        using var localization = new LocalizationService();
        using var context = new SettingsPageContext();
        var adapter = new NativeWindowBackdropAdapter(22621, (handle, attribute, value) =>
        {
            calls.Add((handle, attribute, value));
            return !(failMaterial && attribute == 38 && value != 1);
        }, (_, _) => true);
        using var service = new WindowAppearanceService(adapter, icons);
        var resourcesReady = false;
        using var coordinator = new ApplicationThemeCoordinator(app.Dispatcher, (_, _, _) =>
        {
            app.Resources["ApplicationBackgroundBrush"] = Brushes.DeepPink;
            resourcesReady = true;
        });
        coordinator.AppearanceResourcesApplied += () =>
        {
            Assert.IsTrue(resourcesReady, "Native reapplication must follow resource publication.");
            service.RequestRefresh();
        };
        var window = new FluentWindow
        {
            Left = -32000,
            Top = -32000,
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        try
        {
            SettingsManager.Current = new AppSettings();
            coordinator.Start();
            coordinator.Apply(SettingsManager.Current.Appearance);
            using var viewModel = new AppearanceViewModel(localization, new LegacySettingsConfiguration(context));
            service.Attach(window);
            window.Resources["ApplicationBackgroundBrush"] = Brushes.DeepPink;
            window.Show();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            foreach (var mode in new[] { ApplicationBackdropMode.Acrylic, ApplicationBackdropMode.MicaAlt, ApplicationBackdropMode.Mica, ApplicationBackdropMode.FluentSolid })
            {
                calls.Clear();
                resourcesReady = false;
                viewModel.BackdropMode = mode;
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.AreEqual(mode, SettingsManager.Current.Appearance.BackdropMode);
                Assert.IsTrue(calls.Any(call => call.Attribute == 38), "A loaded window must update before the next background operation, without closing it.");
                var expected = MotionPolicy.ResolveCurrent().UseDecorativeEffects ? mode switch
                {
                    ApplicationBackdropMode.Mica => 2,
                    ApplicationBackdropMode.Acrylic => 3,
                    ApplicationBackdropMode.MicaAlt => 4,
                    _ => 1
                } : 1;
                Assert.AreEqual(expected, calls.Last(call => call.Attribute == 38).Value);
            }
            calls.Clear();
            viewModel.BackdropMode = ApplicationBackdropMode.Mica;
            viewModel.BackdropMode = ApplicationBackdropMode.MicaAlt;
            viewModel.BackdropMode = ApplicationBackdropMode.Acrylic;
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            {
                Assert.AreEqual(3, calls.Last(call => call.Attribute == 38).Value);
                Assert.AreEqual(1, calls.Count(call => call.Attribute == 38 && call.Value != 1), "Rapid selections must coalesce to the final requested material.");
            }
            viewModel.ApplicationThemeMode = ApplicationThemeMode.Dark;
            viewModel.AccentColorMode = AccentColorMode.Custom;
            viewModel.AccentColorHex = "#7C3AED";
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(ApplicationBackdropMode.Acrylic, SettingsManager.Current.Appearance.BackdropMode);
            if (MotionPolicy.ResolveCurrent().UseDecorativeEffects) Assert.AreEqual(3, calls.Last(call => call.Attribute == 38).Value);
            viewModel.ApplicationThemeMode = ApplicationThemeMode.Light;
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (MotionPolicy.ResolveCurrent().UseDecorativeEffects) Assert.AreEqual(3, calls.Last(call => call.Attribute == 38).Value);

            failMaterial = true;
            viewModel.BackdropMode = ApplicationBackdropMode.Mica;
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(1, calls.Last(call => call.Attribute == 38).Value);
            Assert.AreEqual(255, ((SolidColorBrush)window.Background).Color.A, "A failed application must retain an opaque surface.");
            failMaterial = false;
            calls.Clear();
            viewModel.BackdropMode = ApplicationBackdropMode.Acrylic;
            window.Close();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(0, calls.Count, "Closing before the queued refresh must cancel its native work.");

            var second = new FluentWindow { Left = -32000, Top = -32000, Width = 1, Height = 1, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                service.Attach(second);
                second.Show();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                calls.Clear();
                viewModel.BackdropMode = ApplicationBackdropMode.MicaAlt;
                service.Dispose();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                service.RequestRefresh();
                Assert.AreEqual(0, calls.Count, "Disposal must reject a queued or late reapplication.");
            }
            finally { second.Close(); }
        }
        finally
        {
            window.Close();
            service.Dispose();
            coordinator.Dispose();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            SettingsManager.Current = original;
            app.MainWindow = originalMain;
        }
    }
    internal static void VerifyNativeSelection(Application app)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        var original = SettingsManager.Current;
        var originalMain = app.MainWindow;
        using var icons = new AppIconService();
        using var localization = new LocalizationService();
        using var context = new SettingsPageContext();
        using var service = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
        using var coordinator = new ApplicationThemeCoordinator(app.Dispatcher, (_, _, _) => { });
        coordinator.AppearanceResourcesApplied += service.RequestRefresh;
        var window = new FluentWindow { Left = -32000, Top = -32000, Width = 10, Height = 10, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            SettingsManager.Current = new AppSettings();
            coordinator.Start();
            coordinator.Apply(SettingsManager.Current.Appearance);
            using var viewModel = new AppearanceViewModel(localization, new LegacySettingsConfiguration(context));
            service.Attach(window);
            window.Show();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var handle = new WindowInteropHelper(window).Handle;
            foreach (var mode in new[] { ApplicationBackdropMode.Acrylic, ApplicationBackdropMode.MicaAlt, ApplicationBackdropMode.Mica, ApplicationBackdropMode.FluentSolid })
            {
                viewModel.BackdropMode = mode;
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.AreEqual(0, DwmGetWindowAttribute(handle, 38, out var actual, sizeof(int)));
                var expected = MotionPolicy.ResolveCurrent().UseDecorativeEffects ? mode switch
                {
                    ApplicationBackdropMode.Mica => 2,
                    ApplicationBackdropMode.Acrylic => 3,
                    ApplicationBackdropMode.MicaAlt => 4,
                    _ => 1
                } : 1;
                Assert.AreEqual(expected, actual, "The actual DWM attribute must follow the selected mode in the same window.");
                window.Width += 1;
                window.UpdateLayout();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.AreEqual(0, DwmGetWindowAttribute(handle, 38, out actual, sizeof(int)));
                Assert.AreEqual(expected, actual, "Resizing must retain the requested material.");
            }
        }
        finally
        {
            window.Close();
            service.Dispose();
            coordinator.Dispose();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            SettingsManager.Current = original;
            app.MainWindow = originalMain;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);

}
