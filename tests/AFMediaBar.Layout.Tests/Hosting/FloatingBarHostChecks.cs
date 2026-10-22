// 隔离进程内检查实际悬浮宿主；不启动用户媒体、鼠标 Hook 或音频采样。
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
            Assert.IsTrue(limits.Range.HasAvailableRange);
            var handle = new WindowInteropHelper(window).Handle;
            Assert.AreEqual(nint.Zero, AFMediaBar.Classes.Interop.NativeMethods.GetParent(handle));
            VerifyEdgeRegionsAndInteractionHold(window, handle);
            await VerifyEdgeAnimationInterruptionAsync(window, handle);
            var center = window.CenterPx;
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Orientation = LayoutOrientation.Vertical, Layer = FloatingBarLayer.Topmost };
            await Task.Delay(100);
            Assert.IsTrue(window.IsVerticalPresentation);
            Assert.AreEqual(center, window.CenterPx);
            Assert.IsTrue(window.ActualHeight > window.ActualWidth);
            VerifyPureMoves(window);
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
