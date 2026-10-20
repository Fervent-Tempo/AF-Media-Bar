// 在独立 WPF 进程中验证真实设备列表的输入路由，不发送全局输入或切换系统设备。
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>列表更新不写设备，设备模式任意区域的滚轮只表达一次意图。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioDeviceMenuInputTests
{
    [TestMethod]
    public void WholeMenuWheelAndRowClickHaveIndependentSingleDispatch()
    {
        const string isolation = "AFMB_AUDIO_DEVICE_MENU_ISOLATED";
        if (Environment.GetEnvironmentVariable(isolation) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(AudioDeviceMenuInputTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(AudioDeviceMenuInputTests).FullName}.{nameof(WholeMenuWheelAndRowClickHaveIndependentSingleDispatch)}");
            start.Environment[isolation] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); Assert.Fail("设备菜单输入验证超时。"); }
            Assert.AreEqual(0, process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }
        StaTest.Run(_ =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            TaskbarCompactFlyoutWindow? window = null;
            try
            {
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var name in new[] { "MotionResources", "SettingsAppearanceResources" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{name}.xaml", UriKind.Relative) });
                window = new(appearance);
                // 只准备菜单模式；不调用 ShowDevices，避免创建真实锚点窗口或激活桌面。
                typeof(TaskbarCompactFlyoutWindow).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, TaskbarCompactFlyoutMode.InputDevice);
                ((FrameworkElement)window.FindName("OutputDevicePanel")).Visibility = Visibility.Visible;
                var root = (FrameworkElement)window.Content;
                var list = (ListBox)window.FindName("OutputDeviceList");
                var devices = new[] { new AudioDeviceOption("a", "a", "Mic A", true) { Direction = AudioDeviceDirection.Input }, new AudioDeviceOption("b", "b", "Mic B", false) { Direction = AudioDeviceDirection.Input } };
                var clicks = 0;
                var wheels = 0;
                window.OutputDeviceSelected += _ => clicks++;
                window.OutputDeviceWheelRequested += _ => wheels++;
                window.UpdateDevices(devices, devices[0]);
                root.Measure(new Size(320, 440)); root.Arrange(new Rect(0, 0, 320, root.DesiredSize.Height)); root.UpdateLayout();
                Assert.AreEqual(0, clicks);
                var button = Descendants(list).OfType<Button>().First();
                var deviceName = Descendants(button).OfType<TextBlock>().First();
                foreach (var brush in new[] { Brushes.White, Brushes.Black })
                {
                    application.Resources["TextFillColorPrimaryBrush"] = brush;
                    root.UpdateLayout();
                    Assert.AreEqual(brush.Color, ((SolidColorBrush)button.Foreground).Color);
                    Assert.AreEqual(brush.Color, ((SolidColorBrush)deviceName.Foreground).Color);
                }
                window.ShowDeviceStatus("Unchanged");
                Assert.AreEqual(string.Empty, ((TextBlock)window.FindName("DeviceUnavailableText")).Text,
                    "普通结果只能进入提示，不能重新出现底部状态行。");
                foreach (var surface in new[] { root, (FrameworkElement)window.FindName("DeviceTitleText"), button })
                {
                    var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                    surface.RaiseEvent(wheel);
                    Assert.IsTrue(wheel.Handled);
                }
                Assert.AreEqual(3, wheels, "标题、列表行和根内边距每个事件只能推进一次。");
                window.SetOutputDevicePreview(devices[1]);
                Assert.AreSame(devices[1], list.SelectedItem);
                Assert.AreEqual(0, clicks, "更新候选不能触发立即提交。");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, clicks);
                Assert.AreEqual(3, wheels);
                window.UpdateDevices([], null);
                root.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                Assert.AreEqual(3, wheels, "空菜单不能回退到其他目标。");
                window.Dispose(); window.Dispose();
                window.UpdateDevices(devices, devices[0]);
                Assert.AreEqual(0, list.Items.Count);
                Assert.AreEqual(1, clicks);
            }
            finally { window?.Dispose(); application.Shutdown(); }
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
