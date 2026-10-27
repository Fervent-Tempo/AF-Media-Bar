// 隔离进程内检查实际悬浮宿主；不启动用户媒体、鼠标 Hook 或音频采样。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class FloatingBarHostChecks
{
    internal static async Task VerifyAsync()
    {
        var previous = SettingsManager.Current;
        using var icons = new AppIconService();
        using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
        using var switches = new AudioDeviceSwitchCoordinator(new NoDevices());
        using var battery = new BluetoothBatteryService(new(), new(), null!, null!);
        using var zOrder = new DesktopWidgetZOrderService();
        var audio = new AudioInteractionService(null!, null!, null!, null!, switches, null!);
        // 不调用 Start：没有计时器、消息窗口或电源订阅，宿主仅读取初始档位。
        var prune = new MemoryPruneCoordinator(null!, null!, null!, []);
        using var mouse = new NativeMouseInputMonitor(null!);
        using var log = new AppLogService(Path.Combine(Path.GetTempPath(), "AFMB-floating-check"));
        FloatingBarWindow? window = null;
        try
        {
            SettingsManager.Current = new AppSettings
            {
                Experiments = new() { FloatingBar = true },
                TaskbarExperience = TaskbarExperienceSettings.Default with { SpectrumVisible = false, PerformanceVisible = false }
            };
            var display = new DisplayMonitorInfo("test", "Test display", true,
                new Rect(0, 0, 1200, 800), new Rect(0, 0, 1200, 760), 96, 96);
            var limits = new FloatingBarLengthConstraints();
            // 未触发的命令与业务服务不构造；若布局错误地要求采样，本回归会失败。
            window = new FloatingBarWindow(battery, "test", display, null!, null!, audio, null!, null!, null!, mouse,
                prune, zOrder, appearance, new ScreenBackgroundSampler(), log, limits)
            { Title = "AFMB floating verification" };
            window.ApplySnapshot(MediaSnapshot.Disconnected with
            {
                IsConnected = true,
                IsPlaying = true,
                Title = "桌面小工具 Mixed title 2026 🎵",
                Artist = "AF Media Bar"
            });
            await window.ActivateAsync(CancellationToken.None);
            await Task.Delay(100);
            Assert.IsTrue(window.IsVisible);
            VerifyPureMoves(window);
            await VerifyContentResizePreservesPanelAsync(window);
            Assert.IsTrue(limits.Range.HasAvailableRange);
            var handle = new WindowInteropHelper(window).Handle;
            Assert.AreEqual(nint.Zero, AFMediaBar.Classes.Interop.NativeMethods.GetParent(handle));
            await VerifyFullscreenVisibilityAsync(window, handle);
            VerifyEdgeRegionsAndInteractionHold(window, handle);
            VerifyLegacyDockIsPersistedOnce(window);
            await VerifyEdgeAnimationInterruptionAsync(window, handle);
            var center = window.CenterPx;
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Orientation = LayoutOrientation.Vertical, Layer = FloatingBarLayer.Topmost };
            await Task.Delay(100);
            Assert.IsTrue(window.IsVerticalPresentation);
            Assert.AreEqual(center, window.CenterPx);
            Assert.IsTrue(window.ActualHeight > window.ActualWidth);
            VerifyPureMoves(window);
            await VerifyContentResizePreservesPanelAsync(window);
            Assert.AreNotEqual(0, AFMediaBar.Classes.Interop.NativeMethods.GetWindowLong(handle, -20) & 8);
            window.Suspend();
            Assert.IsFalse(limits.Range.HasAvailableRange);
            await Task.Delay(300);
            Assert.IsFalse(window.IsVisible);
            window.Resume();
            Assert.IsTrue(window.IsVisible);
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Orientation = LayoutOrientation.Horizontal };
            window.Dispose();
            window.Dispose();
            Assert.IsFalse(limits.Range.HasAvailableRange);
        }
        finally { window?.Dispose(); SettingsManager.Current = previous; }
    }

    private static async Task VerifyFullscreenVisibilityAsync(FloatingBarWindow window, nint handle)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var timer = (DispatcherTimer)typeof(FloatingBarWindow).GetField("_fullscreenTimer", Flags)!.GetValue(window)!;
        var fullscreen = typeof(FloatingBarWindow).GetField("_foregroundFullscreen", Flags)!;
        var applyVisibility = typeof(FloatingBarWindow).GetMethod("ApplyVisibility", Flags)!;
        var previous = SettingsManager.Current.FloatingBar;
        var wasObserving = timer.IsEnabled;
        timer.Stop();
        try
        {
            SettingsManager.Current.FloatingBar = previous with { OverlayTaskbar = true };
            // 注入已判定的全屏状态，避免测试依赖当前桌面；仍使用真实 HWND 和层级观察计时器。
            fullscreen.SetValue(window, true);
            applyVisibility.Invoke(window, null);
            await Task.Delay(600);
            Assert.IsFalse(AFMediaBar.Classes.Interop.NativeMethods.IsWindowVisible(handle),
                "层级维护不能重新显示因前台全屏而隐藏的悬浮窗口。");
            fullscreen.SetValue(window, false);
            applyVisibility.Invoke(window, null);
            Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.IsWindowVisible(handle), "退出全屏后必须恢复窗口。");
        }
        finally
        {
            fullscreen.SetValue(window, false);
            SettingsManager.Current.FloatingBar = previous;
            applyVisibility.Invoke(window, null);
            if (wasObserving) timer.Start();
        }
    }

    private static async Task VerifyContentResizePreservesPanelAsync(FloatingBarWindow window)
    {
        var resize = typeof(FloatingBarWindow).GetMethod("ResizeContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var apply = typeof(FloatingBarWindow).GetMethod("ApplyBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sizeTimer = (DispatcherTimer)typeof(FloatingBarWindow).GetField("_sizeTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var targetLength = typeof(FloatingBarWindow).GetField("_targetPrimary", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handle = new WindowInteropHelper(window).Handle;
        var center = window.CenterPx;
        var edge = window.DockEdge;
        Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var original));
        var originalLength = window.IsVerticalPresentation ? original.Bottom - original.Top : original.Right - original.Left;
        var transitions = 0;
        void PlacementChanged(object? sender, EventArgs args) => transitions++;
        void Resize(double length, bool immediate) => resize.Invoke(window, [new MediaBarSizeRequest(
            window.IsVerticalPresentation ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal,
            window.IsVerticalPresentation ? 48 : length, window.IsVerticalPresentation ? length : 44,
            "lyric-resize", false, immediate)]);
        int NativeLength()
        {
            Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var bounds));
            return window.IsVerticalPresentation ? bounds.Bottom - bounds.Top : bounds.Right - bounds.Left;
        }
        window.PlacementTransitionStarted += PlacementChanged;
        try
        {
            foreach (var dock in new[] { FloatingBarDockEdge.None, FloatingBarDockEdge.Left, FloatingBarDockEdge.Right,
                FloatingBarDockEdge.Top, FloatingBarDockEdge.Bottom })
            {
                window.SetFullPanelOpen(false);
                window.RestoreCenter(center, dock);
                apply.Invoke(window, [false]);
                window.SetFullPanelOpen(true);
                transitions = 0;
                foreach (var length in new[] { 300, 630, 250 })
                {
                    Resize(length, immediate: true);
                    Assert.AreEqual(length, NativeLength(), "歌词尺寸请求必须真正更新原生窗口。");
                    Assert.AreEqual(0, transitions, $"{dock} 的内容伸缩不能触发关闭完整面板的位置迁移。");
                }
                Resize(430, immediate: false);
                var deadline = Stopwatch.StartNew();
                while (sizeTimer.IsEnabled && deadline.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
                Assert.IsFalse(sizeTimer.IsEnabled, "常规尺寸动画必须完成。");
                // 竖排布局可以在过渡中重新申请长度，验证最终目标而不是中间请求。
                Assert.AreEqual((int)Math.Ceiling((double)targetLength.GetValue(window)!), NativeLength());
                Assert.AreEqual(0, transitions, "尺寸动画的中间帧也不能关闭完整面板。");
            }
            window.SetFullPanelOpen(false);
            window.RestoreCenter(center);
            apply.Invoke(window, [false]);
            window.SetFullPanelOpen(true);
            transitions = 0;
            window.RestoreCenter(center + new Vector(20, 20));
            apply.Invoke(window, [false]);
            Assert.IsTrue(transitions > 0, "真实位置改变仍须通知关闭依赖原位置的面板。");
        }
        finally
        {
            window.PlacementTransitionStarted -= PlacementChanged;
            window.SetFullPanelOpen(false);
            window.RestoreCenter(center, edge);
            Resize(originalLength, immediate: true);
            apply.Invoke(window, [false]);
        }
    }

    private static void VerifyPureMoves(FloatingBarWindow window)
    {
        var apply = typeof(FloatingBarWindow).GetMethod("ApplyBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var arguments = apply.GetParameters().Length == 0 ? null : new object[] { false };
        var dragging = typeof(FloatingBarWindow).GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        var center = window.CenterPx;
        var sizeMessages = 0;
        var moveMessages = 0;
        var settingsWrites = 0;
        nint Observe(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == AFMediaBar.Classes.Interop.NativeMethods.WM_WINDOWPOSCHANGING)
            {
                var change = Marshal.PtrToStructure<WindowPosition>(lParam);
                if ((change.Flags & 1) == 0) sizeMessages++;
                if ((change.Flags & 2) == 0) moveMessages++;
            }
            return nint.Zero;
        }
        void SettingsChanged(object? sender, SettingsChangedEventArgs args) => settingsWrites++;
        source.AddHook(Observe);
        SettingsManager.SettingsChanged += SettingsChanged;
        dragging.SetValue(window, true);
        try
        {
            for (var i = 0; i < 160; i++)
            {
                window.RestoreCenter(new Point(center.X + i % 70, center.Y + i % 20));
                apply.Invoke(window, arguments);
            }
            var count = moveMessages;
            apply.Invoke(window, arguments);
            Assert.AreEqual(0, sizeMessages, "Moving must not repeatedly resize the native window.");
            Assert.AreEqual(count, moveMessages, "An unchanged pixel position must not issue another move.");
            Assert.AreEqual(0, settingsWrites, "Movement must not write settings until release.");
        }
        finally
        {
            source.RemoveHook(Observe);
            SettingsManager.SettingsChanged -= SettingsChanged;
            dragging.SetValue(window, false);
            window.RestoreCenter(center);
            apply.Invoke(window, arguments);
        }
    }

    private static void VerifyEdgeRegionsAndInteractionHold(FloatingBarWindow window, nint handle)
    {
        // 通过现有宿主的终态入口驱动原生区域，避免移动真实鼠标或启动全局 Hook。
        var apply = typeof(FloatingBarWindow).GetMethod("ApplyBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fold = typeof(FloatingBarWindow).GetMethod("SetEdgeTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var observe = typeof(FloatingBarWindow).GetMethod("ObserveEdgePointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var center = window.CenterPx;
        var foreground = AFMediaBar.Classes.Interop.NativeMethods.GetForegroundWindow();
        var before = SettingsManager.Current.FloatingBar.Positions;
        foreach (var edge in new[] { FloatingBarDockEdge.Left, FloatingBarDockEdge.Right, FloatingBarDockEdge.Top, FloatingBarDockEdge.Bottom })
        {
            window.RestoreCenter(center, edge);
            apply.Invoke(window, [false]);
            var normalCenter = window.CenterPx;
            fold.Invoke(window, [true, true]);
            Assert.AreEqual(normalCenter, window.CenterPx, "折叠帧不得覆盖正常中心。");
            Assert.AreNotEqual(0, AFMediaBar.Classes.Interop.NativeMethods.GetWindowRgnBox(handle, out var region));
            Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var rect));
            Assert.AreEqual(6, edge is FloatingBarDockEdge.Left or FloatingBarDockEdge.Right ? region.Right - region.Left : region.Bottom - region.Top);
            Assert.IsTrue(rect.Left + region.Left >= window.Monitor.WorkArea.Left && rect.Left + region.Right <= window.Monitor.WorkArea.Right);
            Assert.IsTrue(rect.Top + region.Top >= window.Monitor.WorkArea.Top && rect.Top + region.Bottom <= window.Monitor.WorkArea.Bottom);
            using (window.KeepEdgeExpanded())
            {
                fold.Invoke(window, [false, true]);
                observe.Invoke(window, [null, EventArgs.Empty]);
                Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var expanded));
                Assert.IsTrue(expanded.Left >= window.Monitor.WorkArea.Left && expanded.Top >= window.Monitor.WorkArea.Top);
            }
            fold.Invoke(window, [true, true]);
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { EdgeCollapseEnabled = false };
            Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var restored));
            Assert.IsTrue(restored.Left >= window.Monitor.WorkArea.Left && restored.Top >= window.Monitor.WorkArea.Top,
                "关闭开关必须立即恢复可见位置。");
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { EdgeCollapseEnabled = true };
        }
        CollectionAssert.AreEqual(before.ToArray(), SettingsManager.Current.FloatingBar.Positions.ToArray(), "自动折叠不保存临时坐标。");
        Assert.AreEqual(foreground, AFMediaBar.Classes.Interop.NativeMethods.GetForegroundWindow(), "折叠与展开不能抢焦点。");
        window.RestoreCenter(center);
        apply.Invoke(window, [false]);
    }

    private static async Task VerifyEdgeAnimationInterruptionAsync(FloatingBarWindow window, nint handle)
    {
        var apply = typeof(FloatingBarWindow).GetMethod("ApplyBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fold = typeof(FloatingBarWindow).GetMethod("SetEdgeTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var center = window.CenterPx;
        window.RestoreCenter(center, FloatingBarDockEdge.Left);
        apply.Invoke(window, [false]);
        fold.Invoke(window, [true, false]);
        await Task.Delay(30);
        Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var interrupted));
        fold.Invoke(window, [false, false]);
        Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var reversing));
        if (MotionPolicy.ResolveCurrent().UseTransitions)
            Assert.AreEqual(interrupted.Left, reversing.Left, "反向动画从当前帧开始，不能跳到端点。");
        window.Suspend();
        await Task.Delay(MotionPolicy.ResolveCurrent().StandardDuration + TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(AFMediaBar.Classes.Interop.NativeMethods.IsWindowVisible(handle), "挂起后旧动画不能重新显示宿主。");
        window.Resume();
        Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(handle, out var resumed));
        Assert.AreEqual((int)window.Monitor.WorkArea.Left, resumed.Left, "恢复时先回到正常附着位置。");
        window.RestoreCenter(center);
        apply.Invoke(window, [false]);
    }

    private static void VerifyLegacyDockIsPersistedOnce(FloatingBarWindow window)
    {
        var apply = typeof(FloatingBarWindow).GetMethod("ApplyBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fold = typeof(FloatingBarWindow).GetMethod("SetEdgeTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var center = window.CenterPx;
        SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Positions = [new("test", 600, 380)] };
        window.RestoreCenter(center, FloatingBarDockEdge.Left);
        apply.Invoke(window, [false]);
        var writes = 0;
        void Changed(object? sender, SettingsChangedEventArgs args) { if (args.PropertyName == nameof(AppSettings.FloatingBar)) writes++; }
        SettingsManager.SettingsChanged += Changed;
        try
        {
            fold.Invoke(window, [true, true]);
            var saved = SettingsManager.Current.FloatingBar.Positions.Single();
            Assert.AreEqual(FloatingBarDockEdge.Left, saved.DockEdge);
            Assert.AreEqual(window.CenterPx.X, window.Monitor.WorkArea.Left + saved.CenterXDip);
            Assert.IsTrue(saved.CenterXDip >= 0);
            fold.Invoke(window, [false, true]);
            fold.Invoke(window, [true, true]);
            Assert.AreEqual(1, writes, "旧位置只回填一次附着信息，动画和后续收起不重复保存。");
            var requested = false;
            void Opened(object? sender, FullPanelOpenRequestEventArgs args)
            {
                Assert.IsTrue(AFMediaBar.Classes.Interop.NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var expanded));
                Assert.IsTrue(expanded.Left >= window.Monitor.WorkArea.Left && expanded.Top >= window.Monitor.WorkArea.Top,
                    "打开面板前应先恢复正常位置，再计算锚点。");
                requested = true;
            }
            window.OpenFullPanelRequested += Opened;
            try
            {
                typeof(FloatingBarWindow).GetMethod("RequestFullPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.IsTrue(requested);
            }
            finally { window.OpenFullPanelRequested -= Opened; }
        }
        finally
        {
            SettingsManager.SettingsChanged -= Changed;
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Positions = [] };
            window.RestoreCenter(center);
            apply.Invoke(window, [false]);
        }
    }

#pragma warning disable CS0649 // 这些字段由 Win32 写入。
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Handle, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }
#pragma warning restore CS0649

    private sealed class NoDevices : IAudioDeviceSwitchOperations
    {
        public int Version => 0;
        public int PolicyVersion => 0;
        public event EventHandler? Changed { add { } remove { } }
        public bool IsAllowed(AudioDeviceOption device) => true;
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AudioDeviceOption>>([]);
        public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult(new ApplicationAudioRoute(null, false, true));
        public Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent) =>
            throw new AssertFailedException("Host lifecycle test must not switch real devices.");
    }
}
