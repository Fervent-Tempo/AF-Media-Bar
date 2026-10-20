// 隔离进程内检查实际悬浮宿主；不启动用户媒体、鼠标 Hook 或音频采样。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
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
            Capture(window, "horizontal");
            Assert.IsTrue(limits.Range.HasAvailableRange);
            var handle = new WindowInteropHelper(window).Handle;
            Assert.AreEqual(nint.Zero, AFMediaBar.Classes.Interop.NativeMethods.GetParent(handle));
            var center = window.CenterPx;
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Orientation = LayoutOrientation.Vertical, Layer = FloatingBarLayer.Topmost };
            await Task.Delay(100);
            Assert.IsTrue(window.IsVerticalPresentation);
            Assert.AreEqual(center, window.CenterPx);
            Assert.IsTrue(window.ActualHeight > window.ActualWidth);
            VerifyPureMoves(window, "-vertical");
            Capture(window, "vertical");
            Assert.AreNotEqual(0, AFMediaBar.Classes.Interop.NativeMethods.GetWindowLong(handle, -20) & 8);
            window.Suspend();
            Assert.IsFalse(limits.Range.HasAvailableRange);
            await Task.Delay(300);
            Assert.IsFalse(window.IsVisible);
            window.Resume();
            Assert.IsTrue(window.IsVisible);
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Orientation = LayoutOrientation.Horizontal };
            if (Environment.GetEnvironmentVariable("AFMB_FLOATING_PREVIEW_HOLD") == "1") await Task.Delay(10000);
            window.Dispose();
            window.Dispose();
            Assert.IsFalse(limits.Range.HasAvailableRange);
        }
        finally { window?.Dispose(); SettingsManager.Current = previous; }
    }

    private static void VerifyPureMoves(FloatingBarWindow window, string suffix = "")
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
        var times = new List<double>();
        try
        {
            for (var i = 0; i < 160; i++)
            {
                window.RestoreCenter(new Point(center.X + i % 70, center.Y + i % 20));
                var tick = Stopwatch.GetTimestamp();
                apply.Invoke(window, arguments);
                times.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
            }
            var count = moveMessages;
            apply.Invoke(window, arguments);
            var directory = Environment.GetEnvironmentVariable("AFMB_FLOATING_MOVE_MEASURE_DIR");
            var name = Environment.GetEnvironmentVariable("AFMB_FLOATING_MOVE_MEASURE_NAME") ?? "after";
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                times.Sort();
                File.WriteAllText(Path.Combine(directory, name + suffix + ".json"), JsonSerializer.Serialize(new
                {
                    sampleCount = times.Count,
                    medianMs = times[times.Count / 2],
                    p95Ms = times[(int)(times.Count * .95)],
                    maxMs = times[^1],
                    sizeMessages,
                    moveMessages,
                    duplicateMoveMessages = moveMessages - count,
                    settingsWrites
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (name != "before")
            {
                Assert.AreEqual(0, sizeMessages, "Moving must not repeatedly resize the native window.");
                Assert.AreEqual(count, moveMessages, "An unchanged pixel position must not issue another move.");
            }
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

#pragma warning disable CS0649 // 这些字段由 Win32 写入。
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Handle, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }
#pragma warning restore CS0649

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AFMB_FLOATING_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

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
