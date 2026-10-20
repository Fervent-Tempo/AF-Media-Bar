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
using AFMediaBar.Classes.Utils;
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
        using var battery = new BluetoothBatteryService(new(), new(), null!, null!);
        var audio = new AudioInteractionService(null!, null!, null!, null!, switches, null!);
        var prune = new MemoryPruneCoordinator(null!, null!, null!, []);
        using var mouse = new NativeMouseInputMonitor(null!);
        using var monitor = new AudioMonitorService(null!, null!);
        using var probe = new TaskbarOccupiedAreaProbe();
        using var builder = new MediaSnapshotBuilder(null!);
        var catalog = new MediaSessionCatalog();
        using var media = new MediaSessionService(new MediaSourceRegistry([]), catalog,
            new MediaSessionSelectionService(catalog, new MediaSourceRegistry([])), builder, [], null!, prune, null!);
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
            var surfaceSessions = new TaskbarHostSurfaceSessions();
            foreach (var vertical in new[] { true, false })
            {
                parent.Width = vertical ? 48 : 600;
                parent.Height = vertical ? 600 : 48;
                await parent.Dispatcher.InvokeAsync(() => parent.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    GetWindowRect(dock.Handle, out var revealedRect);
                    var bounds = MonitorUtil.GetMonitor(dock.Handle).monitorArea;
                    var hiddenRect = vertical
                        ? new RECT
                        {
                            Left = (int)bounds.Right - 2,
                            Right = (int)bounds.Right + 46,
                            Top = revealedRect.Top,
                            Bottom = revealedRect.Bottom
                        }
                        : new RECT
                        {
                            Left = revealedRect.Left,
                            Right = revealedRect.Right,
                            Top = (int)bounds.Bottom - 2,
                            Bottom = (int)bounds.Bottom + 46
                        };
                    dock.ObservedRect = attempt == 0 ? hiddenRect : revealedRect;
                    var window = new TaskbarWindow(battery, dock, "test", actions, appearance,
                        new TaskbarOccupiedAreaService(probe), new TaskbarLengthConstraintsService(),
                        new GlobalInteractionRouter(media, audio), audio, monitor, null!, null!,
                        new ScreenBackgroundSampler(), mouse, prune, surfaceSessions, viewModel, null!, startImmediately: false);
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
                    if (attempt == 0)
                        Assert.IsFalse(window.IsVisible, "隐藏启动必须等待任务栏展开和安全定位。");
                    dock.ObservedRect = revealedRect;
                    // 自有父窗口不会产生跨进程 Shell WinEvent，验证定位轮询能独立恢复隐藏启动。
                    await WaitUntilAsync(() => window.IsVisible && window.MediaControl.IsHitTestVisible);
                    Assert.IsTrue(window.IsVisible && window.MediaControl.IsHitTestVisible,
                        "任务栏展开后必须自行恢复显示和交互，不能依赖手动重载或新的 Shell 事件。");
                    window.Suspend();
                    dock.ObservedRect = attempt == 1 ? hiddenRect : revealedRect;
                    window.Resume();
                    if (attempt == 1)
                        Assert.IsFalse(window.IsVisible, "恢复时仍收起的任务栏不能被 Show 绕过显隐门禁。");
                    dock.ObservedRect = revealedRect;
                    await WaitUntilAsync(() => window.IsVisible && window.MediaControl.IsHitTestVisible);
                    Assert.IsTrue(window.IsVisible && window.MediaControl.IsHitTestVisible,
                        "环境恢复时任务栏隐藏，也必须保留轮询并在展开后恢复。");
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.AreEqual(0, actions.Rebuilds);
                }
            }

            // 首次隐藏启动没有历史安全区，展开后必须由稳定事件启动探测，不能等待慢速定位轮询。
            SettingsManager.Current.TaskbarBarAvoidIcons = true;
            GetWindowRect(dock.Handle, out var initialRect);
            var monitorBounds = MonitorUtil.GetMonitor(dock.Handle).monitorArea;
            dock.ObservedRect = new RECT
            {
                Left = initialRect.Left,
                Right = initialRect.Right,
                Top = (int)monitorBounds.Bottom - 2,
                Bottom = (int)monitorBounds.Bottom + 46
            };
            var firstPlacement = new FirstPlacementProbe();
            var firstWindow = new TaskbarWindow(battery, dock, "test", actions, appearance,
                new TaskbarOccupiedAreaService(firstPlacement), new TaskbarLengthConstraintsService(),
                new GlobalInteractionRouter(media, audio), audio, monitor, null!, null!,
                new ScreenBackgroundSampler(), mouse, prune, surfaceSessions, viewModel, null!, startImmediately: false);
            Assert.IsTrue(await runtime.SwitchAsync(WindowMode.Taskbar,
                _ => Task.FromResult<IReadOnlyList<IBarRuntimeHost>>([firstWindow]), force: true));
            var firstHandle = new WindowInteropHelper(firstWindow).Handle;
            Assert.IsFalse(firstWindow.IsVisible);
            Assert.AreEqual(0, firstPlacement.Requests, $"任务栏收起期间不能探测占用区，首次请求={firstPlacement.FirstRequest}。");
            var slowTimer = (DispatcherTimer)typeof(TaskbarWindow).GetField("_timer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(firstWindow)!;
            var originalInterval = slowTimer.Interval;
            slowTimer.Interval = TimeSpan.FromMinutes(1);
            dock.ObservedRect = initialRect;
            typeof(TaskbarWindow).GetMethod("ObserveTaskbarMotion",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(firstWindow, [false]);
            Assert.AreEqual(0, firstPlacement.Requests, "运动尚未稳定时不能提前探测。");
            await firstPlacement.Requested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(firstWindow.IsVisible, "安全区尚未发布时保持隐藏。");
            firstPlacement.Complete();
            await WaitUntilAsync(() => firstWindow.IsVisible && firstWindow.MediaControl.IsHitTestVisible);
            Assert.IsTrue(firstWindow.IsVisible && firstWindow.MediaControl.IsHitTestVisible,
                "稳定事件和安全区发布必须直接恢复显示与输入。");
            Assert.AreEqual(firstHandle, new WindowInteropHelper(firstWindow).Handle, "首次显示不得靠重建宿主完成。");
            Assert.AreEqual(0, actions.Rebuilds);
            slowTimer.Interval = originalInterval;

            var active = (TaskbarWindow)runtime.Hosts.Single();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<TaskbarHostSurfaceHit>(TaskCreationOptions.RunContinuationsAsynchronously);
            dock.OnProbe = () =>
            {
                entered.TrySetResult();
                return release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            };
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                active.Suspend(); // 原生探测尚未完成时，UI 仍能挂起宿主。
            }
            finally { dock.OnProbe = null; release.TrySetResult(TaskbarHostSurfaceHit.Taskbar); }
            // 等宿主观察后台结果，防止断言先于迟到续体执行。
            var inFlight = typeof(TaskbarWindow).GetField("_surfaceProbeInFlight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await WaitUntilAsync(() => !(bool)inFlight.GetValue(active)!);
            Assert.IsFalse((bool)inFlight.GetValue(active)!, "后台探测必须有界完成。");
            Assert.AreEqual(0, actions.Rebuilds, "挂起之后的迟到异常样本不能重建宿主。");
            dock.SurfaceHit = TaskbarHostSurfaceHit.Taskbar;
            active.Resume();
            await WaitUntilAsync(() => actions.Rebuilds > 0);
            Assert.AreEqual(1, actions.Rebuilds, "连续异常样本应通过现有重载入口恢复。");
        }
        finally { parent.Close(); SettingsManager.Current = original; }
    }

    private sealed class FirstPlacementProbe : ITaskbarOccupiedAreaProbe
    {
        private Action<IReadOnlyList<TaskbarPrimaryRange>>? _complete;
        private IReadOnlyList<TaskbarPrimaryRange> _ranges = [];
        private bool _hasCompleted;
        public int Requests { get; private set; }
        public string? FirstRequest { get; private set; }
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Start(nint handle, RECT rect, LayoutOrientation orientation, double dpi, int padding,
            Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded, Action failed)
        {
            Requests++;
            FirstRequest ??= $"handle={handle}, rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})";
            var length = orientation == LayoutOrientation.Horizontal ? rect.Right - rect.Left : rect.Bottom - rect.Top;
            _ranges = [new(padding, length - padding)];
            if (_hasCompleted) succeeded(_ranges);
            else _complete = succeeded;
            Requested.TrySetResult();
        }

        public void Complete()
        {
            _hasCompleted = true;
            _complete!(_ranges);
            _complete = null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);
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
        /// <summary>区域中心被判定为谁的窗口；默认当作宿主自己绘制正常。/ Which window receives the region centre; the host paints normally by default.</summary>
        public TaskbarHostSurfaceHit SurfaceHit { get; set; } = TaskbarHostSurfaceHit.Host;
        /// <summary>已下发的输入区域，供回归断言"从未下发空几何"。/ Applied input regions, so regressions can assert no empty geometry was ever applied.</summary>
        public List<(IReadOnlyList<RECT> Rects, bool Redraw)> Regions { get; } = [];
        public RECT? ObservedRect { get; set; }
        public Func<TaskbarHostSurfaceHit>? OnProbe { get; set; }
        public nint GetSelectedTaskbarHandle(string? target, out bool main) { main = true; return handle; }
        public double GetTaskbarDpiScale(nint taskbar) => GetDpiForWindow(taskbar) / 96d;
        public bool TryGetTaskbarRect(nint taskbar, out RECT rect)
        {
            if (ObservedRect is { } observed) { rect = observed; return true; }
            return GetWindowRect(taskbar, out rect);
        }
        public bool IsTaskbarVertical(nint taskbar) => _native.IsTaskbarVertical(taskbar);
        public void DockWindow(nint window, nint taskbar) => _native.DockWindow(window, taskbar);
        public void UndockWindow(nint window) => _native.UndockWindow(window);
        public void SetWindowPosition(nint window, nint taskbar, int width, int height) => _native.SetWindowPosition(window, taskbar, width, height);
        public void ApplyInputRegion(nint window, IReadOnlyList<RECT> rects, bool redraw)
        {
            Regions.Add((rects.ToArray(), redraw));
            _native.ApplyInputRegion(window, rects, redraw);
        }

        public TaskbarHostSurfaceHit ProbeHostSurface(nint window, nint taskbar, RECT region) => OnProbe?.Invoke() ?? SurfaceHit;
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
