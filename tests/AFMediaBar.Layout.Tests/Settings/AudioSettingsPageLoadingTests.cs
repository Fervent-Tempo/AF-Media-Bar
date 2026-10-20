// 在独立 WPF 进程中实际加载设置页，防止只读计数等绑定导致页面初始化失败。
using System.Diagnostics;
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using System.Windows.Controls;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Views.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设备分组设置页的真实 XAML 加载回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioSettingsPageLoadingTests
{
    [TestMethod]
    public void LoadsDeviceGroupsWithReadOnlyCounts()
    {
        const string isolation = "AFMB_AUDIO_SETTINGS_LOADING_ISOLATED";
        if (Environment.GetEnvironmentVariable(isolation) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(AudioSettingsPageLoadingTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(AudioSettingsPageLoadingTests).FullName}.{nameof(LoadsDeviceGroupsWithReadOnlyCounts)}");
            start.Environment[isolation] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); Assert.Fail("设置页加载超时。"); }
            Assert.AreEqual(0, process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }
        StaTest.Run(async _ =>
        {
            var previous = SettingsManager.Current;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                application.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                SettingsManager.Current = new AppSettings
                {
                    Interaction = GlobalInteractionSettings.Default with { PrimaryWheelAction = WheelAction.InputDevice }
                };
                // Host 可在后台线程启动；实验关闭时也会发布初始状态，不能跨线程触碰呈现对象。
                using var battery = new BluetoothBatteryService(new(), new(), null!, null!);
                var batteryStatus = new TextBlock();
                battery.Changed += (_, _) => batteryStatus.Text = "updated";
                await Task.Run(() => battery.StartAsync(CancellationToken.None));
                Assert.AreEqual("updated", batteryStatus.Text);
                await battery.StopAsync(CancellationToken.None);
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var name in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{name}.xaml", UriKind.Relative) });
                using var localization = new LocalizationService();
                using var context = new SettingsPageContext();
                var configuration = new LegacySettingsConfiguration(context);
                using var devices = new AudioDeviceSettingsViewModel(new Catalog(), configuration);
                using var components = new ComponentsSettingsViewModel(configuration, localization, new Catalog());
                using var batteryEditor = new BluetoothBatterySettingsViewModel(battery, configuration, localization);
                var widgetsModel = new WidgetsSettingsViewModel(components, batteryEditor, configuration);
                await widgetsModel.RefreshAsync();
                var widgets = new WidgetsSettingsPage(widgetsModel);
                widgets.Measure(new Size(820, 900));
                Assert.IsNotNull(widgets.Content);
                Assert.IsNull(components.SelectedSpectrumCaptureDevice!.DeviceId);
                Assert.AreEqual(2, components.SpectrumCaptureDevices.Count);
                components.SelectedSpectrumCaptureDevice = components.SpectrumCaptureDevices.Single(option => option.DeviceId == "old");
                await widgetsModel.RefreshAsync();
                Assert.AreEqual("old", components.SelectedSpectrumCaptureDevice!.DeviceId, "刷新离线设备列表不能把手动选择改回自动。");
                Assert.AreEqual("old", SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
                components.SelectedSpectrumCaptureDevice = components.SpectrumCaptureDevices.First();
                Assert.IsNull(SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
                using var viewModel = new InteractionViewModel(localization, new LegacySettingsConfiguration(context));
                await devices.RefreshAsync();
                var management = new DeviceManagementPage(devices);
                management.Measure(new Size(820, 900));
                Assert.IsNotNull(management.Content);
                var page = new InteractionPage(viewModel);
                page.Measure(new Size(820, 900));
                Assert.AreEqual(1, devices.OtherOutputDevices.Count);
                Assert.IsNotNull(page.Content);
                var audio = LogicalChildren(page).OfType<SettingsGroup>().Single(group => group.GroupId == "Audio.Settings.Advanced");
                var inputs = LogicalChildren(page).OfType<ComboBoxItem>().Where(item =>
                    item.Tag is WheelAction.InputDevice or TrayClickAction.OpenInputDeviceMenu or TrayWheelBehavior.SwitchInputDevice).ToArray();
                Assert.AreEqual(4, inputs.Length);
                Assert.AreEqual(Visibility.Visible, audio.Visibility);
                Assert.IsTrue(inputs.All(item => item.Visibility == Visibility.Visible), "输入选项应默认可用。");
                SettingsManager.Current.Experiments = new() { VerticalTaskbar = true, FloatingBar = true };
                await System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SettingsManager.Current.Experiments = new();
                await System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(inputs.All(item => item.Visibility == Visibility.Visible), "其他实验开关不应隐藏音频选项。");
                Assert.AreEqual(WheelAction.InputDevice, SettingsManager.Current.Interaction.PrimaryWheelAction);

                using var experiments = new ExperimentalViewModel(new LegacySettingsConfiguration(context));
                var entry = new ExperimentalPage(experiments);
                entry.Measure(new Size(820, 900));
                Assert.IsFalse(LogicalChildren(entry).Any(child => child is Frame or Page), "实验页不再嵌入完整设置页。");
                var navigations = 0;
                entry.AddHandler(SettingsNavigationButton.NavigateEvent, new RoutedEventHandler((_, args) =>
                {
                    Assert.IsInstanceOfType<SettingsNavigationEventArgs>(args);
                    navigations++;
                }));
                var links = LogicalChildren(entry).OfType<SettingsNavigationButton>().ToArray();
                Assert.AreEqual(6, links.Length);
                foreach (var link in links)
                {
                    var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(link);
                    ((System.Windows.Automation.Provider.IInvokeProvider)peer).Invoke();
                }
                await entry.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(links.Length, navigations, "每个设置入口都应转发一次导航意图。");
                await VerifyExperimentalBlankAreaWheelAsync(entry);
            }
            finally { SettingsManager.Current = previous; application.Shutdown(); }
        });
    }

    private static async Task VerifyExperimentalBlankAreaWheelAsync(ExperimentalPage page)
    {
        var navigation = new Wpf.Ui.Controls.NavigationView
        {
            Transition = Wpf.Ui.Animations.Transition.None,
            IsPaneOpen = false,
            IsBackButtonVisible = Wpf.Ui.Controls.NavigationViewBackButtonVisible.Collapsed
        };
        var window = new Window
        {
            Content = navigation,
            Width = 1200,
            Height = 420,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            window.Show();
            navigation.ReplaceContent(page);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var scroll = (ScrollViewer)page.FindName("PageScroll");
            Assert.IsTrue(scroll.ScrollableHeight > 0, "测试窗口必须产生真实溢出。");
            var notice = LogicalChildren(page).OfType<SettingsCallout>().First();
            var gaps = new[] { notice.TranslatePoint(new Point(12, notice.ActualHeight + 4), page),
                scroll.TranslatePoint(new Point(12, scroll.ViewportHeight / 2), page) };
            foreach (var gap in gaps)
            {
                scroll.ScrollToTop();
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var hit = page.InputHitTest(gap) as UIElement;
                Assert.IsNotNull(hit);
                hit.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent
                });
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(scroll.VerticalOffset > 0, "提示下方间距和宽窗口两侧留白均应支持原生滚轮路由。");
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalChildren(child)) yield return descendant;
        }
    }

    private sealed class Catalog : IAudioDeviceCatalog
    {
        public event EventHandler? Changed { add { } remove { } }
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, bool includeHidden = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioDeviceOption>>(direction == AudioDeviceDirection.Output ?
                [new("old", "old", "Old", false) { IsActive = false, Availability = AudioDeviceAvailability.Disabled }] : []);
    }
}
