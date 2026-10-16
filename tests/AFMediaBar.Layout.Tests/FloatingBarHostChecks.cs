// 隔离进程内检查实际悬浮宿主；不启动用户媒体、鼠标 Hook 或音频采样。
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
        using var zOrder = new DesktopWidgetZOrderService();
        var audio = new AudioInteractionService(null!, null!, null!, null!, switches, null!, () => false);
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
            window = new FloatingBarWindow("test", display, null!, null!, audio, null!, null!, null!, mouse,
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
