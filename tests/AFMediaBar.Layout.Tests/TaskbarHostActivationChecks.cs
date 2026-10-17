// 在隔离 WPF 进程中激活真实任务栏宿主；测试自有父 HWND，不触碰 Explorer 或音频设备。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

internal static class TaskbarHostActivationChecks
{
    internal static async Task VerifyAsync()
    {
        var original = SettingsManager.Current;
        using var icons = new AppIconService();
        using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
        using var switches = new AudioDeviceSwitchCoordinator(new NoDevices());
        var audio = new AudioInteractionService(null!, null!, null!, null!, switches, null!, () => false);
        var prune = new MemoryPruneCoordinator(null!, null!, null!, []);
        using var mouse = new NativeMouseInputMonitor(null!);
        using var monitor = new AudioMonitorService(null!, null!);
        using var probe = new TaskbarOccupiedAreaProbe();
        using var builder = new MediaSnapshotBuilder(null!);
        var catalog = new MediaSessionCatalog();
        using var media = new MediaSessionService(new MediaSourceRegistry([]), catalog,
            new MediaSessionSelectionService(catalog), builder, [], null!, prune, null!);
        using var updates = new UpdateService(null!, null!, null!, null!, null!);
        using var localization = new LocalizationService();
        var viewModel = new TaskbarWindowViewModel(media, updates, localization, null!);
        var parent = new Window
        {
            Width = 48,
            Height = 600,
            Left = 20,
            Top = 20,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Style = null
        };
        try
        {
            SettingsManager.Current = new AppSettings
            {
                Experiments = new() { VerticalTaskbar = true },
                TaskbarBarAvoidIcons = false,
                TaskbarExperience = new() { SpectrumVisible = false, PerformanceVisible = false }
            };
            parent.Show();
            var dock = new Dock(new WindowInteropHelper(parent).Handle);
            var actions = new Actions();
            using var runtime = new BarRuntimeCoordinator();
            foreach (var vertical in new[] { true, false })
            {
                parent.Width = vertical ? 48 : 600;
                parent.Height = vertical ? 600 : 48;
                await parent.Dispatcher.InvokeAsync(() => parent.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var window = new TaskbarWindow(dock, "test", actions, appearance,
                        new TaskbarOccupiedAreaService(probe), new TaskbarLengthConstraintsService(),
                        new GlobalInteractionRouter(media, audio), audio, monitor, null!, null!,
                        new ScreenBackgroundSampler(), mouse, prune, viewModel, startImmediately: false);
                    Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle,
                        "Prepared candidates have not created an HWND yet.");
                    var switched = await runtime.SwitchAsync(WindowMode.Taskbar,
                        _ => Task.FromResult<IReadOnlyList<IBarRuntimeHost>>([window]), force: true);
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.IsTrue(switched);
                    Assert.AreEqual(vertical, window.IsVerticalPresentation);
                    Assert.AreEqual(dock.Handle, GetParent(new WindowInteropHelper(window).Handle));
                    Assert.AreEqual(0, actions.Rebuilds,
                        "A newly created host must not interpret its pre-Show HWND as a destroyed old host.");
                    window.Suspend();
                    window.Resume();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.AreEqual(0, actions.Rebuilds);
                }
            }
        }
        finally { parent.Close(); SettingsManager.Current = original; }
    }

    private sealed class Actions : ITaskbarWindowHostActions
    {
        public int Rebuilds { get; private set; }
        public bool IsEnvironmentRecovering => false;
        public void RecreateTaskbarWindow() => Rebuilds++;
        public void RequestTaskbarHostReload() => Rebuilds++;
    }

    private sealed class Dock(nint handle) : ITaskbarDockService
    {
        private readonly TaskbarDockService _native = new(null!);
        public nint Handle => handle;
        public nint GetSelectedTaskbarHandle(string? target, out bool main) { main = true; return handle; }
        public double GetTaskbarDpiScale(nint taskbar) => GetDpiForWindow(taskbar) / 96d;
        public bool TryGetTaskbarRect(nint taskbar, out RECT rect) => GetWindowRect(taskbar, out rect);
        public bool IsTaskbarVertical(nint taskbar) => _native.IsTaskbarVertical(taskbar);
        public void DockWindow(nint window, nint taskbar) => _native.DockWindow(window, taskbar);
        public void UndockWindow(nint window) => _native.UndockWindow(window);
        public void SetWindowPosition(nint window, nint taskbar, int width, int height) => _native.SetWindowPosition(window, taskbar, width, height);
        public void ApplyInputRegion(nint window, IReadOnlyList<RECT> rects) => _native.ApplyInputRegion(window, rects);
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
            throw new AssertFailedException("Host activation must not switch real devices.");
    }
}
