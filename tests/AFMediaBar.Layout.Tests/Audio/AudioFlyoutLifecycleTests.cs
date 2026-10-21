// 在独立进程中验证音频浮窗的 HWND 创建、重复显示和退出边界；测试拥有窗口与 Dispatcher。
// 使用忙碌的空 ViewModel 跳过真实音频读取，不启动托盘、播放器或全局 Hook。
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>托盘音频浮窗显示重入与永久关闭的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioFlyoutLifecycleTests
{
    [TestMethod]
    public void HostCreatesFlyoutOnlyOnDemandAndCancelsPendingCreation()
    {
        if (RunIsolated(nameof(HostCreatesFlyoutOnlyOnDemandAndCancelsPendingCreation))) return;

        StaTest.Run(async _ =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            AudioControlFlyoutWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });

                var viewModel = (AudioControlViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AudioControlViewModel));
                viewModel.IsBusy = true;
                // 只驱动宿主的浮窗协调入口，避免创建真实托盘、媒体服务和全局 Hook。
                var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                var factoryCalls = 0;
                Func<AudioControlFlyoutWindow> factory = () =>
                {
                    factoryCalls++;
                    return window = CreateWindow(viewModel, appearance);
                };
                typeof(MainWindow).GetField("_audioControlFlyoutFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(host, factory);

                host.HideAudioControlFlyout();
                Assert.AreEqual(0, factoryCalls, "隐藏未打开的浮窗不能加载界面。");
                var canceled = host.ToggleAudioControlFlyoutAsync(null);
                Assert.IsFalse(canceled.IsCompleted);
                Assert.AreEqual(0, factoryCalls, "创建必须离开当前原生消息回调。");
                host.HideAudioControlFlyout();
                await canceled;
                Assert.AreEqual(0, factoryCalls, "切换到其他菜单应取消待创建的浮窗。");

                await Task.WhenAll(host.ToggleAudioControlFlyoutAsync(null), host.ToggleAudioControlFlyoutAsync(null));
                Assert.AreEqual(1, factoryCalls, "首次打开的重入应复用同一实例。");
                Assert.IsNotNull(window);
                Assert.IsTrue(window.IsVisible);
                var handle = new WindowInteropHelper(window).Handle;
                host.HideAudioControlFlyout();
                await host.ToggleAudioControlFlyoutAsync(null);
                Assert.AreEqual(1, factoryCalls);
                Assert.AreEqual(handle, new WindowInteropHelper(window).Handle);
                Assert.IsTrue(window.IsVisible);

                var closingHost = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                typeof(MainWindow).GetField("_audioControlFlyoutFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(closingHost, factory);
                var pending = closingHost.ToggleAudioControlFlyoutAsync(null);
                typeof(MainWindow).GetField("_isClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(closingHost, true);
                await pending;
                await closingHost.ToggleAudioControlFlyoutAsync(null);
                Assert.AreEqual(1, factoryCalls, "退出后不能完成待创建请求或接受新请求。");
            }
            finally
            {
                window?.Dispose();
                app.Shutdown();
            }
        });
    }

    [TestMethod]
    public void ToggleDefersWindowCreationCoalescesReentryAndStopsAfterDisposal()
    {
        if (RunIsolated(nameof(ToggleDefersWindowCreationCoalescesReentryAndStopsAfterDisposal))) return;

        StaTest.Run(async _ =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            AudioControlFlyoutWindow? window = null;
            AudioControlFlyoutWindow? pendingWindow = null;
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });

                // RefreshAsync 的忙碌分支同步返回，覆盖原生托盘回调不能依赖刷新必然异步的情况。
                var viewModel = (AudioControlViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AudioControlViewModel));
                viewModel.IsBusy = true;
                window = CreateWindow(viewModel, appearance);
                var sourceCount = 0;
                Task? reentrantToggle = null;
                window.SourceInitialized += (_, _) =>
                {
                    sourceCount++;
                    reentrantToggle = window.ToggleAsync(null);
                };

                var opening = window.ToggleAsync(null);
                Assert.IsFalse(opening.IsCompleted, "窗口创建必须离开当前消息回调。");
                Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
                await window.ToggleAsync(null);
                Assert.IsFalse(window.IsVisible, "重复请求不能抢先创建窗口。");
                await opening;
                Assert.IsNotNull(reentrantToggle);
                await reentrantToggle;
                Assert.IsTrue(window.IsVisible, "创建 HWND 时的重入不能隐藏刚打开的浮窗。");
                var handle = new WindowInteropHelper(window).Handle;
                Assert.AreNotEqual(nint.Zero, handle);
                Assert.AreSame(window, ((HwndSource)PresentationSource.FromVisual(window)).RootVisual);

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    window.Hide();
                    await window.ToggleAsync(null);
                    Assert.IsTrue(window.IsVisible);
                    Assert.AreEqual(handle, new WindowInteropHelper(window).Handle, "隐藏后应复用同一 HWND。");
                }
                Assert.AreEqual(1, sourceCount);
                window.Dispose();
                window.Dispose();
                await window.ToggleAsync(null);
                Assert.IsFalse(window.IsVisible);
                Assert.IsNull(PresentationSource.FromVisual(window));

                pendingWindow = CreateWindow(viewModel, appearance);
                var pendingSources = 0;
                pendingWindow.SourceInitialized += (_, _) => pendingSources++;
                var pending = pendingWindow.ToggleAsync(null);
                pendingWindow.Dispose();
                await pending;
                Assert.AreEqual(0, pendingSources, "退出前排队的请求不能创建 HWND。");
                Assert.IsFalse(pendingWindow.IsVisible);
            }
            finally
            {
                pendingWindow?.Dispose();
                window?.Dispose();
                app.Shutdown();
            }
        });
    }

    [TestMethod]
    public void ApplicationDetailsRouteInputAndClearRemovedOrHiddenSelection()
    {
        if (RunIsolated(nameof(ApplicationDetailsRouteInputAndClearRemovedOrHiddenSelection))) return;
        StaTest.Run(async dispatcher =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            using var switches = new AudioDeviceSwitchCoordinator(new DeviceOperations());
            AudioControlFlyoutWindow? window = null;
            var rows = new ObservableCollection<ApplicationAudioItemViewModel>();
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative) });
                // 只初始化本次导航使用的集合；不构造托盘、Hook 或真实音频服务。
                var vm = (AudioControlViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AudioControlViewModel));
                typeof(AudioControlViewModel).GetField("<AudioApplications>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, rows);
                var removed = (System.Collections.Specialized.NotifyCollectionChangedEventHandler)Delegate.CreateDelegate(
                    typeof(System.Collections.Specialized.NotifyCollectionChangedEventHandler), vm,
                    typeof(AudioControlViewModel).GetMethod("OnAudioApplicationsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);
                rows.CollectionChanged += removed;
                var muteWrites = 0;
                var volumeWrites = 0;
                for (var index = 0; index < 5; index++)
                {
                    var name = "App" + index;
                    var volume = new ApplicationVolumeItemViewModel(new(name, name, 50, false, false, null),
                        (_, _, _) => volumeWrites++, _ => { muteWrites++; return Task.CompletedTask; });
                    rows.Add(new(new(name, name, name, [], true, false), volume, switches));
                }
                vm.IsBusy = true;
                app.Resources["Loc.Common.OutputDevice"] = "输出设备";
                app.Resources["Loc.Common.InputDevice"] = "输入设备";
                window = CreateWindow(vm, appearance);
                await window.ToggleAsync(null);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var list = (ScrollViewer)window.FindName("ApplicationsScrollViewer");
                var detail = (ScrollViewer)window.FindName("ApplicationDetailScrollViewer");
                list.ScrollToVerticalOffset(44);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var offset = list.VerticalOffset;
                var cards = Descendants(list).OfType<Button>().Where(button => ReferenceEquals(button.Command, vm.ToggleAudioApplicationCommand)).ToArray();
                var opener = cards.First(button => ReferenceEquals(button.DataContext, rows[1]));
                var listCard = (Border)((Grid)opener.Parent).Parent;
                var blank = (UIElement)listCard.InputHitTest(new Point(2, 20));
                var blankClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
                blank.RaiseEvent(blankClick);
                Assert.IsTrue(blankClick.Handled, "卡片内边距空白也必须能打开详情。");
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreSame(rows[1], vm.SelectedAudioApplication);
                Assert.AreEqual(Visibility.Collapsed, list.Visibility);
                Assert.AreEqual(Visibility.Visible, detail.Visibility);
                var slider = Descendants(detail).OfType<Slider>().Single();
                slider.Value = 60;
                Assert.AreEqual(1, volumeWrites);
                var volumeWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                slider.RaiseEvent(volumeWheel);
                Assert.IsTrue(volumeWheel.Handled);
                Assert.AreEqual(62, rows[1].Volume!.VolumePercent);
                Assert.AreEqual(2, volumeWrites);
                var mute = Descendants(detail).OfType<Button>().Single(button => ReferenceEquals(button.Command, rows[1].Volume!.ToggleMuteCommand));
                InvokeButton(mute);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(1, muteWrites);
                Assert.AreSame(rows[1], vm.SelectedAudioApplication, "音量与静音不能触发详情返回。");
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                detail.RaiseEvent(wheel);
                Assert.IsTrue(wheel.Handled);
                Assert.AreSame(rows[1], vm.SelectedAudioApplication);
                var back = Descendants(detail).OfType<Button>().First(button => ReferenceEquals(button.Command, vm.ToggleAudioApplicationCommand));
                InvokeButton(back);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsNull(vm.SelectedAudioApplication);
                Assert.AreEqual(offset, list.VerticalOffset, 0.1);
                vm.ToggleAudioApplicationCommand.Execute(rows[2]);
                rows.RemoveAt(2);
                Assert.IsNull(vm.SelectedAudioApplication, "应用消失必须返回列表。");
                vm.ToggleAudioApplicationCommand.Execute(rows[0]);
                await window.ToggleAsync(null);
                Assert.IsNull(vm.SelectedAudioApplication, "隐藏复用窗口也必须清除详情。");
            }
            finally
            {
                window?.Dispose();
                foreach (var row in rows) row.Dispose();
                app.Shutdown();
            }
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

    private static void InvokeButton(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private sealed class DeviceOperations : IAudioDeviceSwitchOperations
    {
        public int Version => 0;
        public int PolicyVersion => 0;
        public event EventHandler? Changed { add { } remove { } }
        public bool IsAllowed(AudioDeviceOption device) => true;
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AudioDeviceOption>>([new("device", "device", "Device", true) { Direction = direction }]);
        public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult(new ApplicationAudioRoute(null, false, true));
        public Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent) =>
            throw new AssertFailedException("导航与加载不能写入设备。");
    }

    private static bool RunIsolated(string testName)
    {
        const string IsolationVariable = "AFMB_AUDIO_FLYOUT_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            // Application 的关闭状态属于进程；隔离实际 BAML 所需的应用资源，避免影响其他 STA 测试。
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(AudioFlyoutLifecycleTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(AudioFlyoutLifecycleTests).FullName}.{testName}");
            startInfo.Environment[IsolationVariable] = "1";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("Isolated audio flyout test timed out");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return true;
        }

        return false;
    }

    private static AudioControlFlyoutWindow CreateWindow(AudioControlViewModel viewModel, WindowAppearanceService appearance) =>
        new(viewModel, appearance)
        {
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
}
