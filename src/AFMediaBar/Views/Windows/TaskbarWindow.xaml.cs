// The media bar docked into the Explorer taskbar, ported from FluentFlyout's TaskbarWindow
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).

using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Components;
using AFMediaBar.Resources;
using Microsoft.Extensions.DependencyInjection;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 透明子窗口覆盖整个任务栏，媒体栏通过 Canvas 放置并用 SetWindowRgn 裁剪。
/// A transparent child window spans the whole taskbar; the media bar is placed on a canvas
/// and clipped with SetWindowRgn so the rest of the taskbar remains visible and click-through.
/// </summary>
public partial class TaskbarWindow : Window
{
    // physical px offsets from the taskbar edges
    private const int EdgePadding = 20;
    private static readonly TimeSpan EnvironmentChangeProbeCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TaskbarMotionProbeCooldown = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan TaskbarMotionSampleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan TaskbarHiddenTrimDelay = TimeSpan.FromSeconds(30);

    private readonly ITaskbarDockService _taskBarService;
    private readonly string _targetMonitorDeviceId;
    private readonly TaskbarOccupiedAreaService _occupiedAreaService;
    private readonly TaskbarLengthConstraintsService _lengthConstraints;
    public TaskbarWindowViewModel ViewModel { get; }
    private readonly ITaskbarWindowHostActions _hostActions;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _sizeAnimationTimer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly DispatcherTimer _outputDeviceApplyTimer;
    private readonly DispatcherTimer _quickLaunchApplyTimer;
    private readonly DispatcherTimer _volumeApplyTimer;
    private readonly DispatcherTimer _taskbarMotionSettleTimer;
    private readonly DispatcherTimer _taskbarHiddenTrimTimer;
    private bool _spectrumActive;
    private bool _spectrumRequestInFlight;
    private readonly GlobalInteractionRouter _interactionRouter;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly MediaSourceActivationService _sourceActivationService;
    private readonly SystemMetricsMonitorService _metricsMonitor;
    private readonly TaskbarCompactFlyoutWindow _compactFlyout;
    private readonly MemoryPruneCoordinator _memoryPruneCoordinator;
    private IDisposable? _metricsSubscription;
    private int _metricsSubscriptionGeneration;

    /// <summary>当前生效的后台剪枝档位；只在恢复计时器时用来判断该不该真正启动它们。
    /// The background prune level currently in effect, only consulted while restoring timers to decide whether they should really start.</summary>
    private MemoryPruneLevel _backgroundPruneLevel = MemoryPruneLevel.None;

    private IntPtr _lastTaskbarHandle;
    private IntPtr _windowHandle;
    private bool _positionUpdateInProgress;

    private bool _isClosing;
    private bool _isEnvironmentSuspended;
    private bool _isDetachedFromTaskbar;
    private bool _setupComplete;
    private TaskbarMotionState _taskbarMotionState;
    private IntPtr _taskbarLocationHook;
    private IntPtr _hookedTaskbarHandle;
    private WinEventProc? _taskbarLocationCallback;
    private bool _environmentLayoutQueued;
    private bool _isDragging;
    private bool _isDragPending;
    private bool _isTaskManagerClickPending;
    private DateTime _suppressContextMenuUntilUtc;
    private DateTime _skipOccupiedAreaProbeUntilUtc;
    private TaskbarSafeRangeSnapshot? _lastStableSafeRange;
    private readonly TaskbarSafeRangeExpansionTracker _safeRangeExpansion = new();
    private bool _hasSafePlacement;
    private WindowMode? _appliedWindowMode;
    private LayoutOrientation? _appliedOrientation;
    private double _appliedLengthScalePercent = double.NaN;
    private double _appliedThicknessScalePercent = double.NaN;
    private int _appliedMediaFontSizePercent = -1;
    private int _dragStartCursorPrimary;
    private int _dragStartBarPrimary;
    private int _dragPrimaryLimit;
    private double _sizeAnimationStart;
    private double _sizeAnimationTarget;
    private double _sizeAnimationProgress;
    private long _sizeAnimationLastTimestamp;
    private bool _applySettingsSizeImmediately;
    private MediaBarSizeRequest? _pendingSizeRequest;
    private MediaBarSizeRequest? _pendingRestExitSizeRequest;
    private MediaBarSizeRequest? _lastDesiredSizeRequest;
    private IntPtr _inputRegionWindowHandle;
    private RECT _lastInputRegion;
    private bool _hasInputRegion;
    /// <summary>最近一次定位读到的任务栏 DPI 缩放；区域扩展的 DIP→物理像素换算依赖它。/ DPI scale from the latest positioning pass; the region extension's DIP-to-physical conversion relies on it.</summary>
    private double _lastDpiScale = 1.0;
    /// <summary>原地放大激活期间临时扩展到媒体栏右侧的物理像素数；0 表示未扩展。基础矩形存于 _lastInputRegion。/ Physical pixels temporarily extending right of the bar while the in-place artwork zoom is active; 0 when not extended. The base rect lives in _lastInputRegion.</summary>
    private int _artworkZoomExtensionPx;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly AdaptiveForegroundSamplingSession _foregroundSamplingSession;
    private readonly float[] _spectrumBands = new float[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _spectrumWorkerBands = new float[SpectrumComponentSettings.MaximumBandCount];
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private IReadOnlyList<AudioDeviceOption> _outputDevices = Array.Empty<AudioDeviceOption>();
    private AudioDeviceOption? _pendingOutputDevice;
    private QuickLaunchEntry? _pendingQuickLaunch;
    private ApplicationVolumeSnapshot? _currentVolume;
    private int? _pendingVolume;
    private int _metricCycleIndex;
    private int _metricSampleCount;
    private IReadOnlyList<MetricKind> _subscribedMetricKinds = [];
    private TimeSpan? _subscribedMetricInterval;

    public event EventHandler? OpenFullPanelRequested;

    /// <summary>
    /// 创建任务栏媒体宿主并连接其基础设施动作。
    /// Creates the taskbar media host and connects its infrastructure actions.
    /// </summary>
    public TaskbarWindow(
        ITaskbarDockService taskBarService,
        string targetMonitorDeviceId,
        ITaskbarWindowHostActions hostActions,
        WindowAppearanceService appearanceService,
        TaskbarOccupiedAreaService occupiedAreaService,
        TaskbarLengthConstraintsService lengthConstraints,
        GlobalInteractionRouter interactionRouter,
        AudioInteractionService audioInteractionService,
        AudioMonitorService audioMonitorService,
        MediaSourceActivationService sourceActivationService,
        SystemMetricsMonitorService metricsMonitor,
        ScreenBackgroundSampler screenBackgroundSampler,
        NativeMouseInputMonitor mouseInputMonitor,
        MemoryPruneCoordinator memoryPruneCoordinator)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        ViewModel = App.Services.GetService<TaskbarWindowViewModel>();
        _targetMonitorDeviceId = targetMonitorDeviceId;
        _mouseInputMonitor = mouseInputMonitor;
        DataContext = ViewModel;
        ContextMenuHelper.AttachOutsideClickDismissal(PlayerMenu);
        appearanceService.Attach(PlayerMenu, this);
        PlayerMenu.OpenSettingsRequested += PlayerMenu_OpenSettingsRequested;
        PlayerMenu.OpenUpdateSettingsRequested += PlayerMenu_OpenUpdateSettingsRequested;
        PlayerMenu.ReloadTaskbarHostRequested += PlayerMenu_ReloadTaskbarHostRequested;
        PlayerMenu.QuickLaunchRequested += PlayerMenu_QuickLaunchRequested;
        // 组合滚轮结束时的合成点击必须被吞掉：静置层点击与右键菜单都要问同一个判定，它们分别属于控件与宿主。
        // The click synthesized when a chord wheel ends has to be swallowed: the rest-layer click and the context menu both ask the
        // same authority, and they live in the control and the host respectively.
        MediaControl.SuppressedClickSource = _mouseInputMonitor.ConsumeSuppressedClick;
        _compactFlyout = new TaskbarCompactFlyoutWindow(appearanceService);
        _compactFlyout.QuickLaunchSelected += MediaControl_QuickLaunchRequested;
        _compactFlyout.OutputDeviceSelected += MediaControl_OutputDeviceSelected;
        _compactFlyout.OutputDeviceWheelRequested += CompactFlyout_OutputDeviceWheelRequested;
        _compactFlyout.VolumeWheelRequested += CompactFlyout_VolumeWheelRequested;
        _compactFlyout.VolumeValueRequested += MediaControl_VolumeValueRequested;
        MediaControl.TogglePlayPauseRequested += MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested += MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested += MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested += MediaControl_ActivateSourceRequested;
        MediaControl.OpenFullPanelRequested += MediaControl_OpenFullPanelRequested;
        MediaControl.OutputDeviceMenuRequested += MediaControl_OutputDeviceMenuRequested;
        MediaControl.OutputDeviceWheelRequested += MediaControl_OutputDeviceWheelRequested;
        MediaControl.VolumeMenuRequested += MediaControl_VolumeMenuRequested;
        MediaControl.VolumeWheelRequested += MediaControl_VolumeWheelRequested;
        MediaControl.QuickLaunchMenuRequested += MediaControl_QuickLaunchMenuRequested;
        MediaControl.QuickLaunchWheelRequested += MediaControl_QuickLaunchWheelRequested;
        MediaControl.OpenTaskManagerRequested += MediaControl_OpenTaskManagerRequested;
        MediaControl.WheelRequested += MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged += MediaControl_DesiredSizeChanged;
        MediaControl.RestTransitionFinished += MediaControl_RestTransitionFinished;
        MediaControl.ArtworkZoomExtensionChanged += MediaControl_ArtworkZoomExtensionChanged;
        MediaControl.OutputDeviceInfoRequested += MediaControl_OutputDeviceInfoRequested;
        MediaControl.VolumeInfoRequested += MediaControl_VolumeInfoRequested;
        // 陈旧捕获看门狗挂在 Window 而不是 MediaControl 上：MediaControl 的预览处理器收不到 Popup 树内的输入路由，
        // 而陈旧捕获恰恰会把命中重定向进主树——窗口级的预览隧道无论如何都能看到每一次移动（见 HealStaleMediaControlCapture）。
        // 但窗口隧道也不是万能的：SubTree 捕获残留期间 WPF 把命中测试沙盒进捕获子树，连窗口隧道都收不到事件——
        // 因此再挂一条全局左键抬起的钩子事件（MouseInputMonitor_OnLeftButtonReleased），那是唯一不受 WPF 捕获
        // 影响的输入通道，「抬起」也是每次拖动尝试的确定性终点。
        // The stale-capture watchdog hooks the Window rather than MediaControl: the control's preview handlers never see input routed
        // inside Popup trees, while a stale capture redirects hit-testing into the main tree — the window-level tunnel observes every
        // move either way (see HealStaleMediaControlCapture). Yet the window tunnel is not omnipotent either: while a SubTree capture
        // lingers, WPF sandboxes hit-testing inside the captured subtree and even the window tunnel receives nothing — hence the extra
        // global left-release hook (MouseInputMonitor_OnLeftButtonReleased), the one input channel immune to WPF captures, and "release"
        // being the deterministic endpoint of every drag attempt.
        PreviewMouseMove += TaskbarWindow_PreviewMouseMove;
        _mouseInputMonitor.LeftButtonReleased += MouseInputMonitor_OnLeftButtonReleased;

        _taskBarService = taskBarService;
        _occupiedAreaService = occupiedAreaService;
        _occupiedAreaService.SafeRangesUpdated += OccupiedAreaService_SafeRangesUpdated;
        _lengthConstraints = lengthConstraints;
        _hostActions = hostActions;
        _interactionRouter = interactionRouter;
        _audioInteractionService = audioInteractionService;
        _audioMonitorService = audioMonitorService;
        _sourceActivationService = sourceActivationService;
        _metricsMonitor = metricsMonitor;
        _memoryPruneCoordinator = memoryPruneCoordinator;
        _foregroundSamplingSession = new AdaptiveForegroundSamplingSession(
            screenBackgroundSampler,
            Dispatcher,
            GetAdaptiveForegroundSampleBounds,
            MediaControl.ApplyAdaptiveForegroundDecision);

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += PositionTimer_Tick;
        _timer.Start();

        // Shell 展开期间 Background 优先级可能被布局与合成工作饿住，表现成任务栏已经出现而媒体栏迟到近一秒。
        // Input priority keeps the bounded stability probe responsive without running it at re-entrant Send priority.
        _taskbarMotionSettleTimer = new DispatcherTimer(DispatcherPriority.Input)
            { Interval = TaskbarMotionSampleInterval };
        _taskbarMotionSettleTimer.Tick += (_, _) => ObserveTaskbarMotion();
        _taskbarHiddenTrimTimer = new DispatcherTimer { Interval = TaskbarHiddenTrimDelay };
        _taskbarHiddenTrimTimer.Tick += (_, _) =>
        {
            _taskbarHiddenTrimTimer.Stop();
            if (!_isClosing && _taskbarMotionState.IsHidden && !_taskbarMotionState.IsMoving)
                _memoryPruneCoordinator.RequestTrim(MemoryTrimTrigger.TaskbarHidden);
        };

        // 后台剪枝：宿主自己订阅档位变化，按档位停掉或恢复自己的计时器与指标订阅。
        // 窗口由 MainWindow 构造（不是容器构造的），因此它不能作为参与者被注入，而是订阅协调器的事件——订阅在 OnClosed 里解除，
        // 否则每次重建任务栏（Explorer 重启）都会留下一个不会释放的窗口引用。
        // Background pruning: the host subscribes to the level changes itself and stops or restores its own timers and metrics subscription from them.
        // The window is built by MainWindow rather than by the container, so it cannot be injected as a participant; instead it subscribes to the
        // coordinator's event, and unsubscribes in OnClosed, because every taskbar rebuild after an Explorer restart would otherwise leave a window
        // reference behind that never gets released.
        _memoryPruneCoordinator.LevelChanged += MemoryPruneCoordinator_LevelChanged;
        _backgroundPruneLevel = _memoryPruneCoordinator.CurrentLevel;

        _sizeAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sizeAnimationTimer.Tick += (_, _) => AdvanceSizeAnimation();
        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _spectrumTimer.Tick += OnSpectrumTimerTick;
        _spectrumTimer.Start();

        // 窗口也可能在档位已经生效时被创建（屏幕关闭期间 Explorer 重建了任务栏）：那时不能等下一次档位变化，
        // 否则刚启动的计时器会一直转到一个没人看得见的窗口上。这里只走"停"的一侧，不触发恢复路径。
        // The window can also be created while a level already holds, when Explorer rebuilds the taskbar during a dark screen. It must not wait for the
        // next level change there, or the timers that just started would keep running for a window nobody can see. Only the stopping side runs here.
        if (IsBackgroundPruned)
        {
            ApplyBackgroundPruneLevel(_backgroundPruneLevel);
        }

        _outputDeviceApplyTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds) };
        _outputDeviceApplyTimer.Tick += async (_, _) =>
        {
            _outputDeviceApplyTimer.Stop();
            var pending = _pendingOutputDevice;
            _pendingOutputDevice = null;
            if (pending is not null && !_isClosing)
            {
                await _audioInteractionService.SetOutputDeviceAsync(pending);
                if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.OutputDevice))
                    _compactFlyout.Dismiss();
            }
        };
        _quickLaunchApplyTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds) };
        _quickLaunchApplyTimer.Tick += async (_, _) =>
        {
            _quickLaunchApplyTimer.Stop();
            var pending = _pendingQuickLaunch;
            _pendingQuickLaunch = null;
            if (pending is not null && !_isClosing && !_lastSnapshot.IsConnected)
            {
                if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch))
                    _compactFlyout.Dismiss();
                var result = await _sourceActivationService.LaunchAsync(pending);
                ShowQuickLaunchResult(result);
            }
        };
        _volumeApplyTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds) };
        _volumeApplyTimer.Tick += async (_, _) =>
        {
            _volumeApplyTimer.Stop();
            var pending = _pendingVolume;
            var volume = _currentVolume;
            _pendingVolume = null;
            if (pending is not null && volume is not null && !_isClosing)
            {
                await Task.Run(() => _audioInteractionService.SetApplicationVolume(volume.ProcessName, pending.Value));
                _currentVolume = volume with { VolumePercent = pending.Value, IsMuted = false };
            }
        };
        SettingsManager.ExtraFeaturesSettingsChanged += SettingsManager_ExtraFeaturesSettingsChanged;
        ApplyExtraFeaturesSettings();

        Loaded += Window_Loaded;

        Show();
    }

    /// <summary>初始化任务栏宿主窗口句柄和消息钩子。/ Initializes the taskbar-host handle and message hook.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        _windowHandle = source.Handle;
        source.AddHook(WindowProc);
    }

    /// <summary>任务栏正在自动隐藏/显示，或已经缩进屏幕边缘时，宿主不参与布局与输入。/ While the taskbar is moving or hidden at the screen edge, the host takes no part in layout or input.</summary>
    private bool IsTaskbarPresentationSuspended => _taskbarMotionState.IsMoving || _taskbarMotionState.IsHidden;

    private void RegisterTaskbarLocationHook(IntPtr taskbarHandle)
    {
        if (taskbarHandle == IntPtr.Zero ||
            taskbarHandle == _hookedTaskbarHandle && _taskbarLocationHook != IntPtr.Zero)
            return;

        UnregisterTaskbarLocationHook();
        _taskbarLocationCallback ??= (_, eventType, hwnd, objectId, _, _, _) =>
        {
            if (eventType != EVENT_OBJECT_LOCATIONCHANGE || hwnd != _lastTaskbarHandle || objectId != OBJID_WINDOW)
                return;

            Dispatcher.BeginInvoke(ObserveTaskbarMotion, DispatcherPriority.Send);
        };

        var threadId = GetWindowThreadProcessId(taskbarHandle, out var processId);
        _taskbarLocationHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _taskbarLocationCallback,
            unchecked((uint)processId),
            threadId,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_taskbarLocationHook != IntPtr.Zero)
            _hookedTaskbarHandle = taskbarHandle;
    }

    private void UnregisterTaskbarLocationHook()
    {
        if (_taskbarLocationHook == IntPtr.Zero)
            return;

        UnhookWinEvent(_taskbarLocationHook);
        _taskbarLocationHook = IntPtr.Zero;
        _hookedTaskbarHandle = IntPtr.Zero;
    }

    /// <summary>
    /// 观察 Shell 任务栏的物理矩形。只要矩形仍在变化，就冻结本窗口的尺寸、位置、采样和输入；连续两个样本稳定后再一次性恢复
    /// （含重新断言几何与显隐，因此调用方不需要区分"钩子触发"与"计时器采样"）。
    /// Observes the Shell taskbar's physical rectangle. Any continuing change freezes this window's size, position, sampling, and input;
    /// ordinary work resumes in one step only after two consecutive stable samples, geometry and visibility included, so callers do not have to
    /// tell hook events and timer samples apart.
    /// </summary>
    private void ObserveTaskbarMotion()
    {
        if (_isClosing || _isEnvironmentSuspended || _lastTaskbarHandle == IntPtr.Zero ||
            !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var taskbarRect))
        {
            return;
        }

        var previous = _taskbarMotionState;
        var orientation = _appliedOrientation ??
                          (taskbarRect.Bottom - taskbarRect.Top > taskbarRect.Right - taskbarRect.Left
                              ? LayoutOrientation.Vertical
                              : LayoutOrientation.Horizontal);
        var monitorBounds = MonitorUtil.GetMonitor(_lastTaskbarHandle).monitorArea;
        _taskbarMotionState = TaskbarMotionPolicy.Observe(previous, taskbarRect, monitorBounds, orientation);

        if (_taskbarMotionState.IsMoving)
        {
            _skipOccupiedAreaProbeUntilUtc = DateTime.UtcNow + TaskbarMotionProbeCooldown;
            _taskbarMotionSettleTimer.Stop();
            _taskbarMotionSettleTimer.Start();
        }
        else
        {
            _taskbarMotionSettleTimer.Stop();
        }

        if (previous.IsMoving == _taskbarMotionState.IsMoving && previous.IsHidden == _taskbarMotionState.IsHidden)
            return;

        ApplyTaskbarPresentationState(previous);
    }

    private void ApplyTaskbarPresentationState(TaskbarMotionState previous)
    {
        var suspended = IsTaskbarPresentationSuspended;
        MediaControl.ApplyHostVisibilitySuspension(suspended);
        MediaControl.IsHitTestVisible = !suspended;
        WindowHelper.SetInputTransparent(this, suspended);

        if (suspended)
        {
            // 运动开始后旧几何不再有资格恢复可见；必须等稳定矩形重新完成一次 PositionBar 才能显示。
            // Once motion starts the old geometry is no longer eligible for presentation; a stable rectangle must complete PositionBar once before reveal.
            _hasSafePlacement = false;
            _sizeAnimationTimer.Stop();
            _spectrumTimer.Stop();
            DisposeMetricsSubscription();
            _foregroundSamplingSession.Invalidate(clearDecision: false);
            _compactFlyout.Dismiss();
            PlayerMenu.IsOpen = false;
            if (_isDragging || _isDragPending)
            {
                _isDragging = false;
                _isDragPending = false;
                if (MediaControl.IsMouseCaptured)
                    MediaControl.ReleaseMouseCapture();
            }

            if (_taskbarMotionState.IsHidden && !_taskbarMotionState.IsMoving)
            {
                _taskbarHiddenTrimTimer.Stop();
                _taskbarHiddenTrimTimer.Start();
                if (!previous.IsHidden)
                    AppLogService.Current?.Info("Taskbar",
                        $"任务栏已自动隐藏，宿主暂停 / taskbar auto-hidden; host suspended: {_targetMonitorDeviceId}");
            }
            else
            {
                _taskbarHiddenTrimTimer.Stop();
            }

            // 显隐判据在 ApplyMediaBarVisibility 只有一处：收起、展开与稳定隐藏整段都不呈现宿主。
            // ApplyMediaBarVisibility is the sole visibility authority: the host is not presented anywhere in the hide, reveal, or settled-hidden span.
            ApplyMediaBarVisibility();
            return;
        }

        _taskbarHiddenTrimTimer.Stop();
        if (previous.IsHidden || previous.IsMoving)
            AppLogService.Current?.Info("Taskbar",
                $"任务栏动画稳定，宿主恢复 / taskbar motion settled; host resumed: {_targetMonitorDeviceId}");

        // 先恢复定位、输入区与长度，最后才显示窗口。反过来会先画出收起位置的旧帧，用户看到的就是屏幕边缘卡出一截。
        // Restore placement, input region, and length before making the window visible. Reversing the order paints one old hidden-position frame,
        // which is exactly the sliver seen stuck at the screen edge.
        ResumeOperationalTimers();

        if (_pendingSizeRequest is { } request && _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            ApplyDesiredSizeRequest(request, orientation);
        }
        else
        {
            // 收起期间窗口的位置、输入区域与长度夹取都没有更新；恢复前 MUST 用稳定矩形重新断言一次几何。
            // Placement, input region, and length clamping do not update while hidden; geometry MUST be asserted against the stable rectangle before reveal.
            UpdatePosition();
        }

        ApplyMediaBarVisibility();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCDESTROY)
        {
            // Explorer 会在 TaskbarCreated 到达 MainWindow 前销毁任务栏子 HWND；立即阻止媒体回调。
            // Explorer destroys taskbar child HWNDs before TaskbarCreated reaches MainWindow;
            // block media callbacks immediately so they cannot show an already closed Window.
            _isClosing = true;
            _windowHandle = IntPtr.Zero;
            _timer.Stop();
            _sizeAnimationTimer.Stop();
            _spectrumTimer.Stop();
            _taskbarMotionSettleTimer.Stop();
            _taskbarHiddenTrimTimer.Stop();
            UnregisterTaskbarLocationHook();
            DisposeMetricsSubscription();
            _outputDeviceApplyTimer.Stop();
            _quickLaunchApplyTimer.Stop();
            _volumeApplyTimer.Stop();
            _compactFlyout.Dismiss();
            _foregroundSamplingSession.Invalidate(clearDecision: false);
            return IntPtr.Zero;
        }

        if (_setupComplete && TaskbarHostMessagePolicy.IsEnvironmentChange(msg))
        {
            _foregroundSamplingSession.Invalidate(clearDecision: false);
            // Explorer 重排任务栏期间只使用保守区间，避免同步 UI Automation 探测与 Shell 互相等待。
            // Use the conservative range while Explorer rearranges the taskbar so synchronous
            // UI Automation probing cannot deadlock with the Shell during repeated display changes.
            _skipOccupiedAreaProbeUntilUtc = DateTime.UtcNow + EnvironmentChangeProbeCooldown;
            _occupiedAreaService.InvalidateCache();
            _sizeAnimationTimer.Stop();
            if (_lastDesiredSizeRequest is { } desiredSize)
                _pendingSizeRequest = desiredSize;
            if (!_environmentLayoutQueued)
            {
                _environmentLayoutQueued = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _environmentLayoutQueued = false;
                    if (_isClosing || _isEnvironmentSuspended)
                        return;

                    InvalidateMeasure();
                    InvalidateArrange();
                    InvalidateVisual();
                    UpdateLayout();
                    UpdatePosition();
                }, DispatcherPriority.ContextIdle);
            }
        }

        // Some interface mods (e.g. Nilesoft Shell, Windhawk) collect information from all
        // windows associated with the taskbar, which can freeze the widget and the whole
        // taskbar. Prevent the propagation of these messages, and stop the widget from
        // blocking the taskbar's message processing.
        if (TaskbarHostMessagePolicy.ShouldSuppressPropagation(msg))
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private async void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        if (_spectrumRequestInFlight) return;

        var bandCount = SettingsManager.Current.SpectrumComponent.Normalize().BandCount;
        // Only the visible spectrum samples audio. Its COM calls and FFT run on AudioMonitorService's worker;
        // this dispatcher callback only presents the completed frame, and never queues overlapping reads.
        if (_isClosing || IsTaskbarPresentationSuspended || _appliedOrientation != LayoutOrientation.Horizontal ||
            !MediaControl.IsSpectrumComponentVisible)
        {
            ClearSpectrum(bandCount);
            return;
        }

        _spectrumRequestInFlight = true;
        try
        {
            var available = await _audioMonitorService.GetSpectrumAsync(_spectrumWorkerBands, bandCount);
            if (!_isClosing && !IsTaskbarPresentationSuspended && _appliedOrientation == LayoutOrientation.Horizontal &&
                MediaControl.IsSpectrumComponentVisible && available)
            {
                Array.Copy(_spectrumWorkerBands, _spectrumBands, bandCount);
                _spectrumActive = true;
                MediaControl.ApplySpectrum(_spectrumBands.AsSpan(0, bandCount));
            }
            else if (!_isClosing)
            {
                ClearSpectrum(bandCount);
            }
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Audio", $"频谱采样失败 / spectrum sampling failed: {exception.Message}");
            if (!_isClosing) ClearSpectrum(bandCount);
        }
        finally
        {
            _spectrumRequestInFlight = false;
        }
    }

    private void ClearSpectrum(int bandCount)
    {
        if (!_spectrumActive) return;
        Array.Clear(_spectrumBands);
        MediaControl.ApplySpectrum(_spectrumBands.AsSpan(0, bandCount));
        _spectrumActive = false;
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || _isEnvironmentSuspended)
            return;

        ObserveTaskbarMotion();
        if (IsTaskbarPresentationSuspended)
            return;

        if (!_isDragging &&
            DateTime.UtcNow >= _skipOccupiedAreaProbeUntilUtc &&
            _pendingSizeRequest is { } request &&
            _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            ApplyDesiredSizeRequest(request, orientation);
        }

        UpdatePosition();
        _foregroundSamplingSession.RequestRefresh();
    }

    private void OccupiedAreaService_SafeRangesUpdated(object? sender, TaskbarSafeRangesUpdatedEventArgs e)
    {
        if (_isClosing || e.TaskbarHandle != _lastTaskbarHandle)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing && !_isEnvironmentSuspended && e.TaskbarHandle == _lastTaskbarHandle)
                UpdatePosition();
        }, DispatcherPriority.Input);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _setupComplete = true;
        Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
    }

    #region TaskBar Layout&Position

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarWindowHandle = interop.Handle;

            IntPtr taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                _targetMonitorDeviceId, out _);
            _lastTaskbarHandle = taskbarHandle;

            ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode,
                taskbarHandle);

            // If this window is created faster than the taskbar is loaded, taskbarHandle will be NULL;
            // UpdatePosition will re-attach once the taskbar appears.
            _taskBarService.DockWindow(taskbarWindowHandle, taskbarHandle);
            RegisterTaskbarLocationHook(taskbarHandle);
            ObserveTaskbarMotion();
            if (!IsTaskbarPresentationSuspended)
                CalculateAndSetPosition(taskbarHandle, taskbarWindowHandle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void UpdatePosition() => UpdatePositionCore(positionImmediately: false);

    private void UpdatePositionImmediately() => UpdatePositionCore(positionImmediately: true);

    private void UpdatePositionCore(bool positionImmediately)
    {
        // The connection transition owns one stable geometry interval. Repeated snapshot probes
        // must not replay placement while its text clip and widget offsets are moving.
        if (!positionImmediately && MediaControl.IsRestConnectionTransitionActive)
            return;
        if (_isClosing || _isEnvironmentSuspended || _isDragging || IsTaskbarPresentationSuspended ||
            _hostActions.IsEnvironmentRecovering)
        {
            // Explorer 正在恢复时不更新旧宿主位置。
            // Do not reposition the old host while Explorer is recovering.
            return;
        }

        if (!SettingsManager.Current.TaskbarBarEnabled)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                _targetMonitorDeviceId, out _);
            if (taskbarHandle != _lastTaskbarHandle)
            {
                _taskbarMotionState = default;
                _hasSafePlacement = false;
                RegisterTaskbarLocationHook(taskbarHandle);
            }

            _lastTaskbarHandle = taskbarHandle;

            ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode,
                taskbarHandle);

            if (interop.Handle == IntPtr.Zero)
            {
                // Our HWND was destroyed with the old taskbar; let MainWindow recreate the window.
                if (_hostActions.IsEnvironmentRecovering)
                    return;

                _timer.Stop();

                Dispatcher.BeginInvoke(_hostActions.RecreateTaskbarWindow, DispatcherPriority.Background);

                return;
            }

            // A live target change must use the host's stability-checked recreation path.
            // Direct reparenting remains valid only for an initially unattached HWND.
            var currentParent = GetParent(interop.Handle);
            if (currentParent != IntPtr.Zero && currentParent != taskbarHandle)
            {
                Dispatcher.BeginInvoke(_hostActions.RequestTaskbarHostReload, DispatcherPriority.Background);
                return;
            }

            if (currentParent != taskbarHandle)
            {
                _taskBarService.DockWindow(interop.Handle, taskbarHandle);
            }

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                if (positionImmediately)
                    CalculateAndSetPosition(taskbarHandle, interop.Handle);
                else
                    Dispatcher.BeginInvoke(() => { CalculateAndSetPosition(taskbarHandle, interop.Handle); },
                        DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr taskbarWindowHandle)
    {
        // Prevent overlapping updates - if a previous update is still running, skip this tick.
        if (_positionUpdateInProgress || IsTaskbarPresentationSuspended)
            return;
        _positionUpdateInProgress = true;

        try
        {
            // get DPI scaling
            double dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
            _lastDpiScale = dpiScale;

            // Guard against invalid DPI (e.g. stale handle during explorer restart)
            if (dpiScale <= 0)
                return;

            if (!_taskBarService.TryGetTaskbarRect(taskbarHandle, out RECT taskbarRect))
                return;

            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            if (taskbarWidth <= 0 || taskbarHeight <= 0)
                return;

            // Cover the whole taskbar with the child window (taskbar-relative coordinates)
            _taskBarService.SetWindowPosition(taskbarWindowHandle, taskbarHandle, taskbarRect,
                taskbarWidth, taskbarHeight);

            // Place the bar on the canvas and clip the window to it
            RECT barRect = PositionBar(taskbarRect, dpiScale);
            if (!_hasInputRegion || _inputRegionWindowHandle != taskbarWindowHandle ||
                !SameRect(_lastInputRegion, barRect))
            {
                ApplyBarInputRegion(taskbarWindowHandle, barRect);
                _inputRegionWindowHandle = taskbarWindowHandle;
                _lastInputRegion = barRect;
                _hasInputRegion = true;
            }
            // 首次占用区探测完成前宿主保持隐藏；发布事件会立即重跑定位并在安全几何落地后显示。
            // Keep the host hidden until its first occupancy probe completes; publication immediately repositions and reveals it after safe geometry lands.
            ApplyMediaBarVisibility();
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private RECT PositionBar(RECT taskbarRect, double dpiScale)
    {
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        var canvas = MediaControl.CurrentLayout?.Canvas;
        double barWidth = canvas?.Width ?? 300;
        double barHeight = canvas?.Height ?? 44;
        var orientation = _appliedOrientation ?? LayoutOrientation.Horizontal;
        var preferredRange = GetPreferredSafeRange(
            taskbarRect,
            orientation,
            dpiScale,
            (int)Math.Round((orientation == LayoutOrientation.Horizontal ? barWidth : barHeight) * dpiScale),
            out var safePlacementResolved);
        var maximumPrimary = preferredRange.Length / dpiScale;
        if (orientation == LayoutOrientation.Horizontal)
            _lengthConstraints.Update(this, MediaControl.MinimumPrimaryLength, maximumPrimary);
        else
            _lengthConstraints.Remove(this);
        if (DateTime.UtcNow >= _skipOccupiedAreaProbeUntilUtc && maximumPrimary > 0)
        {
            var currentPrimary = orientation == LayoutOrientation.Horizontal ? barWidth : barHeight;
            if (currentPrimary > maximumPrimary)
            {
                ApplyPrimaryLength(maximumPrimary);
                canvas = MediaControl.CurrentLayout?.Canvas;
                barWidth = canvas?.Width ?? barWidth;
                barHeight = canvas?.Height ?? barHeight;
            }
        }

        int physicalWidth = (int)Math.Round(barWidth * dpiScale);
        int physicalHeight = (int)Math.Round(barHeight * dpiScale);

        bool isVertical = _appliedOrientation == LayoutOrientation.Vertical;

        // 横轴最后一道保证：媒体栏 MUST NOT 比任务栏更高（竖向任务栏则是更宽）。`ResolveTaskbarThicknessScalePercent`
        // 正常时它本来就装得下——它按任务栏横轴尺寸与预设画布算出厚度上限；而它一旦读到错误的 DPI 或过期的任务栏矩形
        // （自动隐藏动画期间与多显示器切换时都可能），算出来的上限就偏大，媒体栏于是比任务栏高。这里把寄主的框直接夹到
        // 任务栏的横轴尺寸：宁可让内容在底部被裁掉，也绝不让顶边越过任务栏顶边——顶边越界正是用户看到的那种裁切。
        // Last-resort guarantee on the cross axis: the bar MUST NOT be taller than the taskbar (nor wider, on a vertical taskbar).
        // `ResolveTaskbarThicknessScalePercent` normally makes it fit — it derives the thickness ceiling from the taskbar's cross extent and the preset
        // canvas — but once it reads a wrong DPI or a stale taskbar rectangle (both possible during the auto-hide animation and on multi-monitor switches)
        // that ceiling comes out too large and the bar ends up taller than the taskbar. This clamps the host's own box to the taskbar's cross extent:
        // content being cut at the bottom is preferable to the top edge crossing the taskbar's top edge, which is exactly the clipping reported.
        if (isVertical)
            physicalWidth = Math.Min(physicalWidth, Math.Max(1, taskbarWidth));
        else
            physicalHeight = Math.Min(physicalHeight, Math.Max(1, taskbarHeight));

        int primaryLength = isVertical ? taskbarHeight : taskbarWidth;
        int primarySize = isVertical ? physicalHeight : physicalWidth;
        int crossLength = isVertical ? taskbarWidth : taskbarHeight;
        int crossSize = isVertical ? physicalWidth : physicalHeight;

        var placement = TaskbarBarPlacementCalculator.Calculate(
            primaryLength,
            primarySize,
            crossLength,
            crossSize,
            preferredRange,
            SettingsManager.Current.Position,
            SettingsManager.Current.TaskbarBarManualPadding,
            SettingsManager.Current.TaskbarBarCrossAxisOffsetDip,
            dpiScale,
            EdgePadding);
        var primaryPos = placement.Primary;
        var crossPos = placement.Cross;

        // Canvas coordinates and control size are DIPs, hence the dpiScale conversion
        Canvas.SetLeft(MediaControl, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(MediaControl, (isVertical ? primaryPos : crossPos) / dpiScale);
        MediaControl.Width = physicalWidth / dpiScale;
        MediaControl.Height = physicalHeight / dpiScale;
        // 只有实际画布坐标与尺寸都写入后，安全区间才算真正落地；单纯查询最大长度不能提前放行窗口显示。
        // A safe range counts as applied only after the actual canvas coordinates and size have landed; a maximum-length query must not reveal the window early.
        _hasSafePlacement = !SettingsManager.Current.TaskbarBarAvoidIcons || safePlacementResolved;

        return new RECT
        {
            Left = isVertical ? crossPos : primaryPos,
            Top = isVertical ? primaryPos : crossPos,
            Right = (isVertical ? crossPos : primaryPos) + physicalWidth,
            Bottom = (isVertical ? primaryPos : crossPos) + physicalHeight
        };
    }

    private static bool SameRect(RECT left, RECT right) =>
        left.Left == right.Left && left.Top == right.Top &&
        left.Right == right.Right && left.Bottom == right.Bottom;

    /// <summary>
    /// 应用媒体栏的窗口区域：基础矩形右侧叠加当前的原地放大扩展量。窗口本身铺满任务栏，扩展只改区域、不动窗口
    /// 几何。注意这只解决 HWND 层的 SetWindowRgn 裁切——WPF 层 MainBorder 的 ClipToBounds 由控制件在放大存续期间
    /// 自行解除（见 TaskBarMediaControl.ShowArtworkHoverZoom），两层缺一组件仍会被吃。
    /// Applies the media bar's window region: the base rect plus the current in-place zoom extension on its right. The window
    /// itself covers the whole taskbar, so the extension only widens the region without touching window geometry. Note this
    /// addresses only the HWND-layer SetWindowRgn clipping — the WPF layer's MainBorder ClipToBounds is released by the control
    /// itself while the zoom lasts (see TaskBarMediaControl.ShowArtworkHoverZoom); missing either layer still eats the widgets.
    /// </summary>
    private void ApplyBarInputRegion(IntPtr taskbarWindowHandle, RECT baseRect)
    {
        var effective = baseRect;
        if (_artworkZoomExtensionPx > 0)
            effective.Right += _artworkZoomExtensionPx;
        _taskBarService.ApplyInputRegion(taskbarWindowHandle, [effective]);
    }

    /// <summary>
    /// 原地放大激活/收回时重设区域扩展量并立即重应用区域。扩展带只有推挤出去的组件在用，交互语义与栏内一致；
    /// 该带会暂时覆盖媒体栏右侧的任务栏图标悬停（仅放大存续期间，指针在封面上）。重定位路径（CalculateAndSetPosition）
    /// 经 ApplyBarInputRegion 也会带上扩展量，任务栏几何变化不会把它冲掉。
    /// Resizes the region extension when the in-place zoom activates/reclaims and reapplies the region at once. Only the pushed
    /// widgets occupy the extension strip, keeping their in-bar interaction semantics; the strip temporarily overrides hovering of
    /// taskbar icons right of the bar (only while the zoom lasts and the pointer sits on the artwork). The repositioning path
    /// (CalculateAndSetPosition) routes through ApplyBarInputRegion too, so taskbar geometry changes never wash the extension out.
    /// </summary>
    private void MediaControl_ArtworkZoomExtensionChanged(object? sender, double extraDip)
    {
        if (_isClosing)
            return;

        // 原地放大只存在于横向任务栏；其它取向下事件不应到达，防御性地按 0 处理。
        // The in-place zoom exists on the horizontal taskbar only; under any other orientation the event should not arrive —
        // defensively treat it as 0.
        if (_appliedOrientation is not { } orientation || orientation != LayoutOrientation.Horizontal)
            extraDip = 0;

        // 放大激活时若拖动正在进行（含"待定"），立即取消并释放鼠标捕获：捕获会把命中测试重定向到拖动子树，
        // 而封面此刻已被移进 Popup（不在该子树内）——IsMouseOver 从此谎报、进出事件不再送达封面，"拖不动"
        // 的尝试也会把悬停状态机卡死。取消拖动 + 释放捕获，放大才能继续正确认指针。
        // If a drag is underway (including "pending") when the zoom activates, cancel it at once and release the mouse capture:
        // capture redirects hit-testing to the drag subtree while the artwork has just moved into the Popup — outside that
        // subtree — so IsMouseOver starts lying and enter/leave events stop reaching the cover; even a drag attempt that never
        // moves can wedge the hover state machine. Cancelling the drag and dropping the capture keeps the zoom tracking the
        // pointer correctly.
        if (extraDip > 0 && (_isDragging || _isDragPending))
            EndTaskbarDrag();

        var extensionPx = (int)Math.Ceiling(Math.Max(0, extraDip) * _lastDpiScale);
        if (extensionPx == _artworkZoomExtensionPx)
            return;

        _artworkZoomExtensionPx = extensionPx;
        if (_hasInputRegion && _inputRegionWindowHandle != IntPtr.Zero)
            ApplyBarInputRegion(_inputRegionWindowHandle, _lastInputRegion);
    }

    #endregion

    #region SMTC

    /// <summary>
    /// 在任务栏宿主上应用媒体快照并安排位置刷新。
    /// Applies a media snapshot to the taskbar host and schedules repositioning.
    /// </summary>
    /// <param name="snapshot">不可变媒体快照 / Immutable media snapshot.</param>
    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
            return;
        }

        if (!SettingsManager.Current.TaskbarBarEnabled || _isClosing || _isEnvironmentSuspended)
            return;

        var wasConnected = _lastSnapshot.IsConnected;
        _lastSnapshot = snapshot;
        if (snapshot.IsConnected && !wasConnected)
        {
            _pendingRestExitSizeRequest = null;
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            _compactFlyout.Dismiss();
        }

        if (!_timer.IsEnabled && !IsTaskbarPresentationSuspended)
            _timer.Start();

        // Delegate UI update to the original media control and its taskbar-only overlay.
        MediaControl.AllowRestConnectionTransition = !_isDragging && !IsTaskbarPresentationSuspended &&
            !_hostActions.IsEnvironmentRecovering;
        MediaControl.UpdateSongInfo(snapshot);
        MediaControl.ApplyAppearanceSettings();
        // 新宿主构造时仍是断开快照；若性能组件没有配置为“无媒体时保留”，构造阶段不会取得指标租约。
        // 重放当前媒体快照会改变控件算出的组件显隐，因此必须在显隐落地后同步一次租约。这里不强制续租，
        // 否则约 240 ms 一次的媒体快照会不断重置性能指标轮换与刷新间隔。
        // A new host is still disconnected while constructed; unless performance is kept without media, it acquires no metric lease then.
        // Replaying the current media snapshot changes the control-owned visibility decision, so synchronize the lease after that decision lands.
        // This path must not force renewal, or media snapshots arriving about every 240 ms would continually reset metric rotation and cadence.
        SynchronizeMetricsSubscription(SettingsManager.Current.PerformanceComponent.Normalize());

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        // 修改 Visibility 前在 UI 线程再次检查；Explorer 可能在媒体回调与显示步骤之间销毁子 HWND。
        // Recheck on the UI thread immediately before touching Window.Visibility. Explorer
        // can destroy the child HWND between a media callback and this presentation step.
        // "无媒体时完全隐藏"由控件判定（它知道静置层最后是否一个组件都没排），宿主只把结论落到窗口上：
        // 隐藏整条媒体栏 MUST 隐藏窗口本身，控件里做可见性只会留下一个仍然吃掉点击的透明子窗口。
        // "Hide completely without media" is decided by the control (it knows whether the rest layer ended up with no component at all)
        // and the host merely applies that verdict to the window: hiding the whole bar MUST hide the window itself, because a
        // control-level visibility change would leave a transparent child window that still swallows clicks.
        ApplyMediaBarVisibility();
    }

    #endregion

    /// <summary>
    /// 应用布局设置：根据窗口模式和布局方向更新媒体控件的布局。
    /// Apply layout settings: update media control layout based on window mode and orientation mode.
    /// </summary>
    /// <param name="windowMode">窗口模式 / Window mode</param>
    /// <param name="orientationMode">布局方向模式 / Layout orientation mode</param>
    public void ApplyLayoutSettings(WindowMode windowMode, LayoutOrientationMode orientationMode)
    {
        var taskbarHandle = _lastTaskbarHandle;
        if (taskbarHandle == IntPtr.Zero)
        {
            taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(
                _targetMonitorDeviceId, out _);
        }

        ApplyLayoutSettings(windowMode, orientationMode, taskbarHandle);
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
    }

    private void ApplyLayoutSettings(WindowMode windowMode, LayoutOrientationMode orientationMode, IntPtr taskbarHandle)
    {
        if (_isClosing)
            return;

        // 将 LayoutOrientationMode 转换为 LayoutOrientation
        // Convert LayoutOrientationMode to LayoutOrientation
        LayoutOrientation orientation;

        if (orientationMode == LayoutOrientationMode.Auto)
        {
            // 自动模式直接根据当前任务栏矩形判定，任务栏在左右边缘时为竖向。
            // Auto mode resolves the current taskbar rectangle; left/right docking is vertical.
            orientation = _taskBarService.IsTaskbarVertical(taskbarHandle)
                ? LayoutOrientation.Vertical
                : LayoutOrientation.Horizontal;
        }
        else
        {
            // 手动模式：直接映射
            // Manual mode: direct mapping
            orientation = orientationMode == LayoutOrientationMode.Horizontal
                ? LayoutOrientation.Horizontal
                : LayoutOrientation.Vertical;
        }

        // 应用布局到媒体控件
        // Apply layout to media control
        var orientationChanged = _appliedOrientation != orientation;
        var lengthScalePercent = SettingsManager.Current.LayoutLengthScalePercent;
        var thicknessScalePercent = ResolveTaskbarThicknessScalePercent(
            taskbarHandle,
            orientation,
            SettingsManager.Current.LayoutThicknessScalePercent);
        var mediaFontSizePercent = SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        var layoutChanged = _appliedWindowMode != windowMode ||
                            orientationChanged ||
                            !lengthScalePercent.Equals(_appliedLengthScalePercent) ||
                            !thicknessScalePercent.Equals(_appliedThicknessScalePercent) ||
                            mediaFontSizePercent != _appliedMediaFontSizePercent;
        if (layoutChanged)
        {
            var previousImmediate = _applySettingsSizeImmediately;
            _applySettingsSizeImmediately = true;
            try
            {
                MediaControl.ApplyLayout(windowMode, orientation, lengthScalePercent, thicknessScalePercent);
                MediaControl.ApplyAppearanceSettings();
                _appliedWindowMode = windowMode;
                _appliedOrientation = orientation;
                _appliedLengthScalePercent = lengthScalePercent;
                _appliedThicknessScalePercent = thicknessScalePercent;
                _appliedMediaFontSizePercent = mediaFontSizePercent;
                MediaControl.RefreshDesiredSize();
            }
            finally
            {
                _applySettingsSizeImmediately = previousImmediate;
            }
            // 横/竖布局会改变静置层性能组件是否存在；没有新媒体快照时也必须同步租约。
            // Horizontal/vertical layout changes can add or remove the rest-layer performance component, even when no new media snapshot follows.
            SynchronizeMetricsSubscription(SettingsManager.Current.PerformanceComponent.Normalize());
        }

        if (orientationChanged && IsLoaded)
        {
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Loaded);
        }

        // 此宿主只呈现任务栏模式；旧模式值由设置归一化处理。
        // This host renders the taskbar only; settings normalization handles legacy mode values.
    }

    private double ResolveTaskbarThicknessScalePercent(
        IntPtr taskbarHandle,
        LayoutOrientation orientation,
        double requestedPercent)
    {
        requestedPercent = Math.Clamp(requestedPercent, 70, 125);
        var dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
        if (dpiScale <= 0 || !_taskBarService.TryGetTaskbarRect(taskbarHandle, out var taskbarRect))
            return requestedPercent;

        var preset = LayoutPresets.GetLayout(WindowMode.Taskbar, orientation);
        var baseCrossSize = orientation == LayoutOrientation.Vertical
            ? preset.Canvas.Width
            : preset.Canvas.Height;
        var physicalCrossSize = orientation == LayoutOrientation.Vertical
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        var availableCrossSizeDip = physicalCrossSize / dpiScale;

        // 任务栏宿主会裁剪超出横轴的内容，因此仅限制实际渲染值，保留用户设置值。
        // The taskbar host clips cross-axis overflow, so cap only the rendered value and preserve the user's setting.
        var maximumPercent = Math.Max(70, availableCrossSizeDip / baseCrossSize * 100);
        return Math.Min(requestedPercent, maximumPercent);
    }

    /// <summary>
    /// 用最新会话列表重建右键菜单的"切换媒体源"子菜单；点击通过命令执行。
    /// Rebuilds the "switch media source" submenu from the latest session list; clicks run through commands.
    /// </summary>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing || _isEnvironmentSuspended)
            return;

        Dispatcher.Invoke(() => { PlayerMenu.ApplySessions(options); });
    }

    /// <summary>立即应用播放器与右键菜单外观。 / Immediately applies player and context-menu appearance.</summary>
    public void ApplyAppearanceSettings()
    {
        MediaControl.ApplyAppearanceSettings();
        if (SettingsManager.Current.Appearance.PlayerForegroundMode == PlayerForegroundMode.Automatic)
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
        else
            _foregroundSamplingSession.Invalidate(clearDecision: true);
    }

    private Int32Rect? GetAdaptiveForegroundSampleBounds()
    {
        if (_isClosing || _isEnvironmentSuspended || Visibility != Visibility.Visible ||
            SettingsManager.Current.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic)
        {
            return null;
        }

        return MediaControl.TryGetForegroundSampleBounds(out var bounds) ? bounds : null;
    }

    /// <summary>在全局左键点击位于菜单外时关闭右键菜单。 / Closes the context menu after a global left click outside it.</summary>
    public void CloseContextMenuIfOutside(int screenX, int screenY) =>
        ContextMenuHelper.CloseIfOutside(PlayerMenu, screenX, screenY);

    /// <summary>关闭任务栏媒体菜单。/ Closes the taskbar media menu.</summary>
    internal void ClosePlayerMenu() => PlayerMenu.IsOpen = false;

    /// <summary>此宿主所绑定的显示器稳定标识。/ Stable monitor identifier bound to this host.</summary>
    internal string TargetMonitorDeviceId => _targetMonitorDeviceId;

    /// <summary>返回当前媒体栏的屏幕 DIP 边界，供完整面板定位。 / Returns the current media-bar screen DIP bounds for full-panel placement.</summary>
    public Rect GetMediaBarScreenBounds()
    {
        var active = GetActiveControl();
        var point = active.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(active);
        return new Rect(
            point.X / dpi.DpiScaleX,
            point.Y / dpi.DpiScaleY,
            active.ActualWidth,
            active.ActualHeight);
    }

    /// <summary>返回音频弹窗所需的媒体栏物理像素边界。 / Returns media-bar physical-pixel bounds for the audio flyout.</summary>
    public TrayIconBounds GetMediaBarScreenPhysicalBounds()
    {
        var active = GetActiveControl();
        var point = active.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(active);
        return new TrayIconBounds(
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y),
            (int)Math.Round(point.X + active.ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(point.Y + active.ActualHeight * dpi.DpiScaleY));
    }

    public void ApplyExperienceSettings()
    {
        var previousImmediate = _applySettingsSizeImmediately;
        _applySettingsSizeImmediately = true;
        try
        {
            // 静置层设置里包含媒体文字字号，必须重新应用布局才能把新字号写进文本块；
            // 布局状态未变化时 ApplyLayoutSettings 不会做任何工作。
            // Rest-layer settings include the media font size, so the layout has to be re-applied before the text update;
            // ApplyLayoutSettings does nothing when the layout state is unchanged.
            var taskbarHandle = _lastTaskbarHandle;
            if (taskbarHandle == IntPtr.Zero)
                taskbarHandle = _taskBarService.GetSelectedTaskbarHandle(_targetMonitorDeviceId, out _);
            ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode,
                taskbarHandle);
            ApplyExtraFeaturesSettings();
            MediaControl.UpdateSongInfo(_lastSnapshot);
            // 改设置就可能改变静置层还剩几个组件，因此"完全隐藏"的结论必须跟着重算一次：
            // 只在快照变化时同步会让"把无媒体保留组件全部取消"这一步要等下一首歌才生效。
            // A settings change can change how many components the rest layer keeps, so the "hide completely" verdict has to be recomputed
            // here: synchronizing it on snapshot changes alone would delay "keep nothing while idle" until the next track.
            ApplyMediaBarVisibility();
        }
        finally
        {
            _applySettingsSizeImmediately = previousImmediate;
        }
        // 设置改动会先写入控件宽度；位置与窗口裁剪区域必须在本次 UI 更新内一起落地。
        UpdatePositionImmediately();
    }

    /// <summary>
    /// 把控件算出的"整条媒体栏是否应当隐藏"与任务栏运动状态一起落到窗口上，并只在稳定可见后补一次背景采样。
    /// 判据只有一处（<see cref="TaskbarHostVisibilityPolicy"/>）：收起、展开和稳定隐藏期间都隐藏宿主，可见矩形稳定后才恢复。
    /// Applies the control's whole-bar visibility verdict together with taskbar motion state, refreshing background sampling only after the host is
    /// stably visible. <see cref="TaskbarHostVisibilityPolicy"/> is the sole authority: the host remains hidden while the taskbar hides, reveals, or
    /// stays auto-hidden, and returns only after the visible rectangle settles.
    /// </summary>
    private void ApplyMediaBarVisibility()
    {
        if (_isClosing || _isEnvironmentSuspended)
        {
            return;
        }

        var hasSafePlacement = !SettingsManager.Current.TaskbarBarAvoidIcons || _hasSafePlacement;
        var resolved = hasSafePlacement
            ? TaskbarHostVisibilityPolicy.Resolve(_taskbarMotionState, MediaControl.ShouldHideTaskbarWindow)
            : TaskbarHostVisibility.Collapsed;
        var target = resolved == TaskbarHostVisibility.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (Visibility == target)
        {
            return;
        }

        var wasHidden = Visibility != Visibility.Visible;
        Visibility = target;
        // 运动期间不请求背景采样：那一刻窗口正在被父窗口带着走，采样矩形没有意义，恢复后由位置计时器补一次。
        // No background sampling is requested while the taskbar moves: the window is being carried by its parent at that moment, the sample
        // rectangle means nothing, and the position timer asks for one after the motion settles.
        if (wasHidden && target == Visibility.Visible && !_taskbarMotionState.IsMoving)
        {
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>安全停止任务栏宿主并解除 Explorer 停靠。/ Safely stops the taskbar host and detaches it from Explorer.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        _isClosing = true;
        SuspendForEnvironmentRecovery();
        DetachFromTaskbar();
    }

    /// <summary>
    /// 停止旧任务栏宿主的布局、动画和菜单更新，等待环境恢复时重建窗口。
    /// Stops layout, animation, and menu updates on an old taskbar host until it is recreated.
    /// </summary>
    internal void SuspendForEnvironmentRecovery()
    {
        if (_isEnvironmentSuspended)
            return;

        _isEnvironmentSuspended = true;
        UnregisterTaskbarLocationHook();
        _timer.Stop();
        _sizeAnimationTimer.Stop();
        _spectrumTimer.Stop();
        _taskbarMotionSettleTimer.Stop();
        _taskbarHiddenTrimTimer.Stop();
        DisposeMetricsSubscription();
        _outputDeviceApplyTimer.Stop();
        _quickLaunchApplyTimer.Stop();
        _volumeApplyTimer.Stop();
        _pendingOutputDevice = null;
        _pendingQuickLaunch = null;
        _pendingVolume = null;
        _foregroundSamplingSession.Invalidate(clearDecision: false);
        _pendingSizeRequest = null;
        PlayerMenu.IsOpen = false;
        _compactFlyout.Dismiss();
        MediaControl.ApplyHostVisibilitySuspension(true);
        MediaControl.IsHitTestVisible = false;
        WindowHelper.SetInputTransparent(this, true);
    }

    internal void ResumeAfterEnvironmentRecovery()
    {
        if (_isClosing || !_isEnvironmentSuspended)
            return;

        _isEnvironmentSuspended = false;
        RegisterTaskbarLocationHook(_lastTaskbarHandle);
        _taskbarMotionState = default;
        ObserveTaskbarMotion();
        if (!IsTaskbarPresentationSuspended)
            ResumeOperationalTimers();
    }

    /// <summary>
    /// 是否因为后台剪枝而停掉了本宿主自己的计时器与指标订阅。
    /// Whether this host stopped its own timers and metrics subscription because of background pruning.
    /// </summary>
    private bool IsBackgroundPruned => _backgroundPruneLevel >= MemoryPruneLevel.DisplayOff;

    /// <summary>
    /// 挡板：把协调器的档位变化转成宿主自己的动作，并跳过没有实际变化的通知。
    /// The adapter that turns a coordinator level change into this host's own actions, skipping notifications without an actual change.
    /// </summary>
    private void MemoryPruneCoordinator_LevelChanged(object? sender, MemoryPruneLevelChangedEventArgs e)
    {
        if (_isClosing || _backgroundPruneLevel == e.Level)
            return;

        _backgroundPruneLevel = e.Level;
        ApplyBackgroundPruneLevel(e.Level);
    }

    /// <summary>
    /// 按剪枝档位停掉或恢复任务栏宿主自己的计时器、指标订阅与频谱采集。
    /// Stops or restores this taskbar host's own timers, metrics subscription, and spectrum capture for the prune level.
    ///
    /// 屏幕熄灭或系统睡眠时，这一整套（1.5 s 定位、50 ms 频谱、性能指标订阅）都在为一个没人看得见的窗口工作，因此全部停掉；
    /// 恢复由档位变化触发，而档位变化又由显示器打开或唤醒的广播驱动，所以恢复是立刻的，不需要等任何轮询。
    /// While the display is dark or the system is suspending, this whole set — the 1.5 s repositioning, the 50 ms spectrum, the performance metrics
    /// subscription — works for a window nobody can see, so all of it stops. The restore comes from the level change, which the display-on or resume
    /// broadcast drives, so it is immediate and waits for no poll.
    /// </summary>
    /// <param name="level">剪枝档位。/ The prune level.</param>
    internal void ApplyBackgroundPruneLevel(MemoryPruneLevel level)
    {
        _backgroundPruneLevel = level;

        // 控件自己那几只计时器由宿主转达；控件不查询电源状态（所有权与依赖方向见 IMPLEMENTATION_CONSTRAINTS）。
        // The control's own timers are passed down by the host; the control never queries the power state, which keeps ownership and dependency
        // direction as IMPLEMENTATION_CONSTRAINTS requires.
        MediaControl.ApplyBackgroundPruneLevel(level);

        if (!IsBackgroundPruned)
        {
            ResumeOperationalTimers();
            return;
        }

        _timer.Stop();
        _spectrumTimer.Stop();
        DisposeMetricsSubscription();
        _pendingSizeRequest = null;
    }

    /// <summary>
    /// 恢复本宿主的常规节奏：定位、频谱、指标订阅与自适应前景采样。挡板状态（关闭中、环境恢复中、后台剪枝中）任一成立时什么都不做。
    /// Restores this host's ordinary cadence: repositioning, spectrum, metrics subscription, and adaptive foreground sampling. It does nothing while any
    /// blocker holds: closing, recovering the environment, or background pruning.
    /// </summary>
    private void ResumeOperationalTimers()
    {
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned || IsTaskbarPresentationSuspended)
            return;

        MediaControl.ApplyHostVisibilitySuspension(false);
        MediaControl.IsHitTestVisible = true;
        WindowHelper.SetInputTransparent(this, false);
        if (!_timer.IsEnabled)
            _timer.Start();
        if (!_spectrumTimer.IsEnabled)
            _spectrumTimer.Start();
        ApplyExtraFeaturesSettings();
        UpdatePosition();
        Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
    }

    internal void DetachFromTaskbar()
    {
        if (_isDetachedFromTaskbar || _windowHandle == IntPtr.Zero)
            return;

        _isDetachedFromTaskbar = true;
        _taskBarService.UndockWindow(_windowHandle);
    }

    private void MediaControl_TogglePlayPauseRequested(object? sender, EventArgs e) =>
        Execute(ViewModel.TogglePlayPauseCommand);

    private void MediaControl_SkipPreviousRequested(object? sender, EventArgs e) =>
        Execute(ViewModel.SkipPreviousCommand);

    private void MediaControl_SkipNextRequested(object? sender, EventArgs e) =>
        Execute(ViewModel.SkipNextCommand);

    private void MediaControl_ActivateSourceRequested(object? sender, EventArgs e) =>
        Execute(ViewModel.ActivateMediaSourceCommand);

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void MediaControl_OpenFullPanelRequested(object? sender, EventArgs e) =>
        OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

    private async void MediaControl_WheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
    {
        if (e.IsLeftButtonDown)
            EndTaskbarDrag();
        if (e.IsRightButtonDown)
        {
            _suppressContextMenuUntilUtc = DateTime.UtcNow.AddMilliseconds(450);
            PlayerMenu.IsOpen = false;
        }

        // 滚轮提示要回答"刚才发生了什么"：动作执行完立刻把结果写给媒体栏，用户不需要去别处确认。
        // The wheel tooltip answers "what just happened": the result is handed to the bar as soon as the action completes, so the
        // user needs no second place to check.
        var result =
            await _interactionRouter.ExecuteWheelAsync(e.Delta, e.IsShiftDown, e.IsLeftButtonDown, e.IsRightButtonDown);
        if (!_isClosing && result is { } wheelResult)
            MediaControl.SetWheelResult(wheelResult);
    }

    private async void MediaControl_OutputDeviceMenuRequested(object? sender, EventArgs e)
    {
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.OutputDevice))
        {
            _compactFlyout.Dismiss();
            return;
        }

        _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
        if (_isClosing) return;
        _compactFlyout.ShowOutputDevices(
            _outputDevices,
            _outputDevices.FirstOrDefault(device => device.IsDefault),
            MediaControl.GetOutputDeviceAnchor());
    }

    private async void MediaControl_OutputDeviceWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
        => await PreviewOutputDeviceAsync(e.Delta,
            updateFlyout: _compactFlyout.IsShowing(TaskbarCompactFlyoutMode.OutputDevice));

    /// <summary>
    /// 指针进入输出设备按钮时刷新提示；滚轮预览尚未落地时显示待应用候选，避免提示回退到旧设备。
    /// Refreshes the tooltip when the pointer enters the output-device button; while a wheel preview is still pending it
    /// shows that candidate so the tooltip cannot fall back to the previously applied device.
    /// </summary>
    private async void MediaControl_OutputDeviceInfoRequested(object? sender, EventArgs e)
    {
        if (_pendingOutputDevice is { } pending)
        {
            MediaControl.SetOutputDevicePreview(pending);
            return;
        }

        if (_isClosing) return;
        // 每次悬停都重新枚举，避免显示已被系统切换过的旧默认设备。
        // Re-enumerate on every hover so a default device changed elsewhere is not reported stale.
        _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
        if (_isClosing) return;
        var current = _outputDevices.FirstOrDefault(device => device.IsDefault) ?? _outputDevices.FirstOrDefault();
        if (current is null)
            MediaControl.SetOutputDeviceUnavailable();
        else
            MediaControl.SetOutputDevicePreview(current);
    }

    private async void CompactFlyout_OutputDeviceWheelRequested(int delta)
        => await PreviewOutputDeviceAsync(delta, updateFlyout: true);

    private async Task PreviewOutputDeviceAsync(int delta, bool updateFlyout)
    {
        if (_outputDevices.Count == 0) _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
        if (_outputDevices.Count == 0)
        {
            MediaControl.SetOutputDeviceUnavailable();
            return;
        }

        var current = _pendingOutputDevice is null
            ? _outputDevices.ToList().FindIndex(device => device.IsDefault)
            : _outputDevices.ToList().FindIndex(device => device.Id == _pendingOutputDevice.Id);
        var index = DeferredCircularSelection.Move(current, delta, _outputDevices.Count);
        if (index < 0) return;
        _pendingOutputDevice = _outputDevices[index];
        MediaControl.SetOutputDevicePreview(_pendingOutputDevice);
        if (updateFlyout) _compactFlyout.SetOutputDevicePreview(_pendingOutputDevice);
        _outputDeviceApplyTimer.Stop();
        _outputDeviceApplyTimer.Start();
    }

    private async void MediaControl_OutputDeviceSelected(AudioDeviceOption device)
    {
        _outputDeviceApplyTimer.Stop();
        _pendingOutputDevice = null;
        MediaControl.SetOutputDevicePreview(device);
        await _audioInteractionService.SetOutputDeviceAsync(device);
    }

    private async void MediaControl_VolumeMenuRequested(object? sender, EventArgs e)
    {
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.Volume))
        {
            _compactFlyout.Dismiss();
            return;
        }

        _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing) return;
        _compactFlyout.ShowVolume(
            _lastSnapshot.SourceName,
            _currentVolume?.VolumePercent,
            MediaControl.GetVolumeAnchor());
    }

    /// <summary>
    /// 在指定屏幕锚点处打开紧凑菜单（输出设备或当前应用音量）。托盘图标点击走这条入口：菜单内容与按钮点开时完全一致，
    /// 只有锚点不同，因此不会出现两份会各自漂移的实现。
    /// Opens a compact menu (output device or current application volume) at the given screen anchor. The tray icon's click uses
    /// this entry: the menu content is identical to the button-opened one and only the anchor differs, so there is no second
    /// implementation to drift.
    /// </summary>
    /// <param name="mode">要打开的面板：输出设备或当前应用音量。/ Panel to open: output device or current application volume.</param>
    /// <param name="anchor">菜单锚点；为空时退回媒体栏上的对应按钮。/ Menu anchor, falling back to the matching media-bar button when null.</param>
    public async Task ShowCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? anchor)
    {
        if (_isClosing || mode is not (TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.Volume))
            return;

        if (_compactFlyout.IsShowing(mode))
        {
            _compactFlyout.Dismiss();
            return;
        }

        if (mode == TaskbarCompactFlyoutMode.OutputDevice)
        {
            _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
            if (_isClosing) return;
            _compactFlyout.ShowOutputDevices(
                _outputDevices,
                _outputDevices.FirstOrDefault(device => device.IsDefault),
                anchor ?? MediaControl.GetOutputDeviceAnchor());
            return;
        }

        _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing) return;
        _compactFlyout.ShowVolume(
            _lastSnapshot.SourceName,
            _currentVolume?.VolumePercent,
            anchor ?? MediaControl.GetVolumeAnchor());
    }

    private async void MediaControl_VolumeWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
        => await PreviewVolumeAsync(e.Delta, updateFlyout: _compactFlyout.IsShowing(TaskbarCompactFlyoutMode.Volume));

    /// <summary>
    /// 指针进入音量按钮时刷新提示；已有延迟应用候选时显示该候选，避免提示回退到已应用值。
    /// Refreshes the tooltip when the pointer enters the volume button; a pending deferred candidate is shown instead so the
    /// tooltip cannot fall back to the applied value.
    /// </summary>
    private async void MediaControl_VolumeInfoRequested(object? sender, EventArgs e)
    {
        if (_pendingVolume is { } pending)
        {
            MediaControl.SetVolumePreview(pending);
            return;
        }

        if (_isClosing) return;
        var volume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing) return;
        _currentVolume = volume;
        if (volume is null)
            MediaControl.SetVolumeUnavailable();
        else
            MediaControl.SetVolumePreview(volume.VolumePercent);
    }

    private async void CompactFlyout_VolumeWheelRequested(int delta)
        => await PreviewVolumeAsync(delta, updateFlyout: true);

    private async Task PreviewVolumeAsync(int delta, bool updateFlyout)
    {
        _currentVolume ??= await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_currentVolume is null)
        {
            MediaControl.SetVolumeUnavailable();
            return;
        }

        var steps = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine) * (delta > 0 ? 1 : -1);
        var value = Math.Clamp((_pendingVolume ?? _currentVolume.VolumePercent) + steps * 2, 0, 100);
        QueueVolume(value);
        MediaControl.SetVolumePreview(value);
        if (updateFlyout) _compactFlyout.SetVolumePreview(value);
    }

    private void MediaControl_VolumeValueRequested(int value)
    {
        // 菜单内拖动滑杆同样要刷新按钮提示，与滚轮预览保持一致。
        // Dragging the slider inside the menu refreshes the button tooltip too, matching the wheel preview.
        MediaControl.SetVolumePreview(value);
        QueueVolume(value);
    }

    private void QueueVolume(int value)
    {
        if (_currentVolume is null) return;
        _pendingVolume = Math.Clamp(value, 0, 100);
        _volumeApplyTimer.Stop();
        _volumeApplyTimer.Start();
    }

    private async void MediaControl_QuickLaunchRequested(QuickLaunchEntry entry)
    {
        _quickLaunchApplyTimer.Stop();
        _pendingQuickLaunch = null;
        var result = await _sourceActivationService.LaunchAsync(entry);
        ShowQuickLaunchResult(result);
    }

    private async void PlayerMenu_QuickLaunchRequested(QuickLaunchEntry entry)
    {
        var result = await _sourceActivationService.LaunchAsync(entry);
        if (_isClosing) return;
        ShowQuickLaunchResult(result, GetMediaBarScreenPhysicalBounds());
    }

    private void ShowQuickLaunchResult(QuickLaunchResult result, TrayIconBounds? anchor = null)
    {
        if (_isClosing || result == QuickLaunchResult.Success) return;
        _compactFlyout.ShowQuickLaunch(
            SettingsManager.Current.QuickLaunch.Entries ?? [],
            anchor ?? MediaControl.GetQuickLaunchAnchor());
        _compactFlyout.ShowQuickLaunchStatus(DescribeQuickLaunchResult(result));
    }

    /// <summary>
    /// 取快速启动的失败原因：文案在写进菜单的这一刻按当前界面语言解析，不缓存到字段里，因此界面上不会留下上一种语言
    /// 的那一句，也不需要额外的刷新路径。
    /// Resolves the quick-launch failure reason: the sentence is resolved in the active interface language at the moment it
    /// is handed to the menu and is never cached in a field, so no line is left behind in the previous language and no
    /// extra refresh path is needed.
    /// </summary>
    /// <param name="result">失败的启动结果。/ The failed launch result.</param>
    private static string DescribeQuickLaunchResult(QuickLaunchResult result) =>
        Translations.Get(result == QuickLaunchResult.InvalidTarget
            ? "Shell.QuickLaunch.Status.InvalidTarget"
            : "Shell.QuickLaunch.Status.Failed");

    private void MediaControl_QuickLaunchMenuRequested(object? sender, EventArgs e)
    {
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch))
        {
            _compactFlyout.Dismiss();
            return;
        }

        _compactFlyout.ShowQuickLaunch(
            SettingsManager.Current.QuickLaunch.Entries ?? [],
            MediaControl.GetQuickLaunchAnchor());
    }

    private void MediaControl_QuickLaunchWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
    {
        var entries = SettingsManager.Current.QuickLaunch.Entries ?? [];
        if (entries.Count == 0) return;
        var current = _pendingQuickLaunch is null
            ? 0
            : entries.ToList().FindIndex(entry => entry.Id == _pendingQuickLaunch.Id);
        var index = DeferredCircularSelection.Move(current, e.Delta, entries.Count);
        if (index < 0) return;
        _pendingQuickLaunch = entries[index];
        MediaControl.SetQuickLaunchPreview(_pendingQuickLaunch);
        _quickLaunchApplyTimer.Stop();
        _quickLaunchApplyTimer.Start();
    }

    private void MediaControl_OpenTaskManagerRequested(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarWindow] Could not open Task Manager: {ex}");
        }
    }

    private void SettingsManager_ExtraFeaturesSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ApplyExtraFeaturesSettings);

    private void ApplyExtraFeaturesSettings()
    {
        var spectrum = SettingsManager.Current.SpectrumComponent.Normalize();
        _spectrumTimer.Interval = TimeSpan.FromMilliseconds(1000d / spectrum.RefreshRateHz);
        var performance = SettingsManager.Current.PerformanceComponent.Normalize();
        MediaControl.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
        MediaControl.ApplyTaskbarExperienceSettings();
        // 是否订阅指标采样按控件算出的性能组件显隐决定：静置层显隐的唯一判据在 TaskbarRestLayoutPolicy 里，
        // 宿主自行读一遍 PerformanceVisible 会在"无媒体时不保留性能组件"的状态下继续空转采样。
        // Whether to subscribe for metric sampling follows the performance component's visibility as the control computed it: the only authority on
        // rest-layer visibility is TaskbarRestLayoutPolicy, and reading PerformanceVisible here instead would keep sampling for nothing while the
        // performance component is not kept without media.
        SynchronizeMetricsSubscription(performance);
    }

    private void SynchronizeMetricsSubscription(PerformanceComponentSettings performance)
    {
        var metrics = (performance.Metrics ?? [MetricKind.SystemMemory]).Distinct().ToArray();
        var interval = TimeSpan.FromMilliseconds(performance.RefreshIntervalMilliseconds);
        var shouldSubscribe = !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned &&
                              !IsTaskbarPresentationSuspended && MediaControl.IsPerformanceComponentVisible;
        var configurationChanged = _metricsSubscription is not null &&
                                   (!_subscribedMetricKinds.SequenceEqual(metrics) ||
                                    _subscribedMetricInterval != interval);
        var transition = MetricPresentationPolicy.ResolveSubscriptionTransition(
            shouldSubscribe,
            _metricsSubscription is not null,
            configurationChanged);
        if (transition == MetricSubscriptionTransition.None)
            return;

        if (transition == MetricSubscriptionTransition.Unsubscribe)
        {
            DisposeMetricsSubscription();
            return;
        }

        if (transition == MetricSubscriptionTransition.Renew)
            DisposeMetricsSubscription();
        _metricCycleIndex = 0;
        _metricSampleCount = 0;
        var generation = _metricsSubscriptionGeneration;
        _metricsSubscription = _metricsMonitor.Subscribe(
            metrics,
            interval,
            snapshot => ApplyMetricsSnapshot(generation, snapshot));
        _subscribedMetricKinds = metrics;
        _subscribedMetricInterval = interval;
    }

    private void ApplyMetricsSnapshot(int generation, SystemMetricsSnapshot snapshot)
    {
        if (generation != _metricsSubscriptionGeneration || _isClosing || _isEnvironmentSuspended ||
            IsTaskbarPresentationSuspended)
            return;
        var settings = SettingsManager.Current.PerformanceComponent.Normalize();
        var metrics = settings.Metrics ?? [MetricKind.SystemMemory];
        if (metrics.Count == 0)
            return;
        _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, metrics.Count - 1);
        _metricSampleCount++;
        _metricCycleIndex = MetricPresentationPolicy.Advance(_metricCycleIndex, _metricSampleCount, metrics.Count);
        MediaControl.ApplyPerformanceText(MetricPresentationPolicy.Format(metrics[_metricCycleIndex], snapshot),
            settings.OpenTaskManagerOnClick);
        _foregroundSamplingSession.RequestRefresh();
    }

    private void DisposeMetricsSubscription()
    {
        _metricsSubscriptionGeneration++;
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
        _subscribedMetricKinds = [];
        _subscribedMetricInterval = null;
    }

    private static void ExecuteWithParameter(System.Windows.Input.ICommand command, object parameter)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private void MediaControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            SettingsManager.Current.TaskbarBarPositionLocked ||
            e.OriginalSource is DependencyObject source && IsMediaAction(source))
        {
            return;
        }

        // 原地放大存续期间禁止拖动，原因有两层：①封面元素住在 Popup 里、右侧内容带着推挤变换，拖动与这些瞬态
        // 状态交叠会触发各种怪异行为（拖动中收拢、推挤残留、位置基准漂移）；②拖动逻辑会捕获鼠标并把命中测试
        // 重定向到拖动子树，而封面已在子树之外——"拖不动"的尝试也会把封面的悬停判定卡死（抬起有
        // ResyncArtworkHoverAfterRelease 兜底，捕获残留另有窗口级看门狗 HealStaleMediaControlCapture 随鼠标移动
        // 与全局左键抬起强制释放）。放大是悬停态、几秒即收，牺牲这几秒的可拖性换取行为可预期。
        // No dragging while the in-place artwork zoom is live, for two reasons: ① the artwork element lives in its Popup and the
        // right-hand content carries push transforms, and dragging interleaved with those transient states triggers all kinds of
        // odd behaviour (collapsing mid-drag, leftover pushes, drifting position bases); ② the drag logic captures the mouse and
        // redirects hit-testing to the drag subtree, which the artwork has just left — even a blocked drag attempt can wedge the
        // cover's hover verdicts (the release backstops via ResyncArtworkHoverAfterRelease, and any leftover capture is force-
        // released by the window-level watchdog HealStaleMediaControlCapture on mouse moves and global left releases). The zoom
        // is a hover state lasting seconds; sacrificing draggability for those seconds buys predictable behaviour.
        if (MediaControl.IsArtworkZoomActive)
            return;

        // 性能组件是可点击控件而不是可拖动的空白：命中它时不开拖动，否则这次点击的抬起事件会被拖动路径标记为已处理，
        // 组件上声明的 MouseLeftButtonUp 永远不会执行，点击打开任务管理器就永远不生效。
        // The performance component is a clickable control rather than draggable background: a hit on it must not start a
        // drag, because the drag path marks the matching mouse-up as handled and the component's own MouseLeftButtonUp then
        // never runs, which is what kept "open Task Manager on click" from ever working.
        if (e.OriginalSource is DependencyObject performanceSource &&
            SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick &&
            MediaControl.IsPerformanceComponentClick(performanceSource))
        {
            _isTaskManagerClickPending = true;
            return;
        }

        var taskbarHandle = _lastTaskbarHandle;
        if (taskbarHandle == IntPtr.Zero ||
            !_taskBarService.TryGetTaskbarRect(taskbarHandle, out var taskbarRect) ||
            !GetCursorPos(out var cursor))
        {
            return;
        }

        var dpiScale = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
        if (dpiScale <= 0)
            return;

        var isVertical = _appliedOrientation == LayoutOrientation.Vertical;
        var primaryLength = isVertical
            ? taskbarRect.Bottom - taskbarRect.Top
            : taskbarRect.Right - taskbarRect.Left;
        var canvas = MediaControl.CurrentLayout?.Canvas;
        var primarySizeDip = isVertical ? canvas?.Height ?? 168 : canvas?.Width ?? 300;
        var primarySize = (int)Math.Round(primarySizeDip * dpiScale);

        _dragStartCursorPrimary = isVertical ? cursor.Y : cursor.X;
        _dragStartBarPrimary = (int)Math.Round((isVertical
            ? Canvas.GetTop(GetActiveControl())
            : Canvas.GetLeft(GetActiveControl())) * dpiScale);
        _dragPrimaryLimit = Math.Max(0, primaryLength - primarySize);
        _isDragPending = true;
        Mouse.Capture(GetActiveControl(), CaptureMode.SubTree);
    }

    private void MediaControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isTaskManagerClickPending && e.LeftButton != MouseButtonState.Pressed)
            _isTaskManagerClickPending = false;

        if (!_isDragging && !_isDragPending)
            return;

        if (e.LeftButton != MouseButtonState.Pressed || SettingsManager.Current.TaskbarBarPositionLocked)
        {
            EndTaskbarDrag();
            return;
        }

        if (!GetCursorPos(out var cursor))
            return;

        var cursorPrimary = _appliedOrientation == LayoutOrientation.Vertical ? cursor.Y : cursor.X;
        if (_isDragPending)
        {
            var dpiScale = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
            if (Math.Abs(cursorPrimary - _dragStartCursorPrimary) < 4 * Math.Max(1, dpiScale))
                return;
            _isDragPending = false;
            _isDragging = true;
        }

        var targetPrimary = Math.Clamp(
            _dragStartBarPrimary + cursorPrimary - _dragStartCursorPrimary,
            0,
            _dragPrimaryLimit);
        SettingsManager.Current.Position = TaskbarBarPosition.Start;

        // 偏移的基准 MUST 与放置时的加法基准一致（空闲区间起点），否则媒体栏会整体偏离鼠标；
        // 自动避让打开且区间起点不在最左边时，这个差值就是"拖不动"的来源。
        // The offset's base MUST match the base the placement adds it to (the free range's start), otherwise the bar misses the mouse
        // by that difference; with "avoid icons" on and a range that does not start at the left edge, that is exactly what makes
        // dragging feel broken.
        var dragDpiScale = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
        if (dragDpiScale <= 0 ||
            !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var dragTaskbarRect))
        {
            return;
        }

        var dragIsVertical = _appliedOrientation == LayoutOrientation.Vertical;
        var dragCanvas = MediaControl.CurrentLayout?.Canvas;
        var dragPrimarySize = (int)Math.Round(
            (dragIsVertical ? dragCanvas?.Height ?? 168 : dragCanvas?.Width ?? 300) * dragDpiScale);
        var dragRange = GetPreferredSafeRange(
            dragTaskbarRect,
            _appliedOrientation ?? LayoutOrientation.Horizontal,
            dragDpiScale,
            dragPrimarySize,
            out _);
        SettingsManager.Current.TaskbarBarManualPadding = TaskbarBarPlacementCalculator.ResolveManualPadding(
            targetPrimary,
            dragRange.Start,
            dragRange.End,
            dragPrimarySize);

        var windowHandle = new WindowInteropHelper(this).Handle;
        if (_lastTaskbarHandle != IntPtr.Zero && windowHandle != IntPtr.Zero)
            CalculateAndSetPosition(_lastTaskbarHandle, windowHandle);
        e.Handled = true;
    }

    private void MediaControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // 兜底释放遗留的 SubTree 捕获：拖动按下时的 Mouse.Capture(MediaControl, SubTree) 若因竞态遗留（被拦下的
        // 按下、事件乱序），命中测试会被锁死在捕获子树内——已换树进 Popup 的封面在子树之外，从此收不到任何
        // 鼠标事件，悬停彻底失聪。正常路径的捕获在抬起时也已结束，此处是纯修复层，不抢合法控件的捕获
        // （那类捕获挂在子元素上，IsMouseCaptured 只对 MediaControl 本身成立）。
        // Backstop against a leftover SubTree capture: the Mouse.Capture(MediaControl, SubTree) taken on a drag press can
        // outlive its release path through races (a blocked press, out-of-order events), and while it lasts hit-testing is
        // locked inside the captured subtree — the artwork, re-parented into its Popup, sits outside it and never receives
        // another mouse event, killing hover outright. Legitimate captures have ended by button-up, so this is a pure repair
        // layer that never steals a control's own capture (those hang on children; IsMouseCaptured holds only for MediaControl).
        // Releases routed inside a Popup tree never reach this handler; those are the window-level watchdog's beat
        // (HealStaleMediaControlCapture — now driven both by mouse moves and the global left-release hook, the one channel
        // a lingering capture cannot silence).
        if (MediaControl.IsMouseCaptured)
            MediaControl.ReleaseMouseCapture();

        if (_isTaskManagerClickPending)
        {
            _isTaskManagerClickPending = false;
            // 指针可能已经移到别的元素上，因此抬起时重新判定命中，而不是相信按下时的结论。
            // The pointer may have moved onto another element, so the hit is re-evaluated on release instead of trusting the
            // verdict from the press.
            if (e.OriginalSource is DependencyObject source && MediaControl.IsPerformanceComponentClick(source))
            {
                MediaControl.RequestOpenTaskManager();
                e.Handled = true;
            }

            ResyncArtworkHoverAfterRelease();
            return;
        }

        // 按下期间鼠标捕获（拖动逻辑或被按下的按钮）会让封面的命中判定失真——被拦下的拖动尝试也会留下 stale 状态。
        // 抬起时捕获已释放，按真实指针位置重同步原地放大；非放大状态下是无操作。
        // Mouse capture during the press (the drag logic or the pressed button) skews the artwork's hit verdicts — even a
        // blocked drag attempt leaves stale state behind. Capture is released by the time we get here, so re-sync the in-place
        // zoom from the real pointer position; a no-op when the zoom is not live.
        ResyncArtworkHoverAfterRelease();

        if (!_isDragging && !_isDragPending)
            return;

        // 「待定」状态下的抬起仍是一次普通单击：封面、标题与歌词区的单击处理（打开播放器菜单、执行点击绑定）
        // 必须继续运行，因此这次抬起 MUST NOT 被标记为已处理；只有真正拖动过才吞掉它，避免拖动结束后又触发一次单击。
        // A release from the pending state is still an ordinary click: the artwork/title/lyric click handlers (opening the player
        // menu, running the click binding) must keep running, so this release MUST NOT be marked handled. Only a real drag
        // swallows it, which is what keeps a drag from ending in an extra click.
        if (_isDragging)
        {
            EndTaskbarDrag();
            e.Handled = true;
            return;
        }

        EndTaskbarDrag();
    }

    private void MediaControl_DesiredSizeChanged(object? sender, MediaBarSizeRequestEventArgs eventArgs)
    {
        var request = eventArgs.Request;
        if (_isClosing || _appliedOrientation is not { } orientation)
            return;

        _lastDesiredSizeRequest = request;
        if (!MediaControl.IsRestConnectionTransitionActive)
            _pendingRestExitSizeRequest = null;
        if (_isDragging || IsTaskbarPresentationSuspended || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
        {
            _pendingSizeRequest = request;
            return;
        }

        if (MediaControl.IsRestConnectionTransitionActive && orientation == LayoutOrientation.Horizontal)
        {
            _sizeAnimationTimer.Stop();
            if (MediaControl.IsRestConnectionTransitionEntering)
            {
                _pendingRestExitSizeRequest = null;
                ApplyDesiredSizeRequest(request with { SkipTransition = true }, orientation);
            }
            else
            {
                _pendingRestExitSizeRequest = request;
            }
            return;
        }

        ApplyDesiredSizeRequest(request, orientation);
    }

    private void MediaControl_RestTransitionFinished(object? sender, EventArgs e)
    {
        if (_isClosing)
            return;

        var appliedSize = false;
        if (_pendingRestExitSizeRequest is { } request && _appliedOrientation is { } orientation)
        {
            _pendingRestExitSizeRequest = null;
            if (_isDragging || IsTaskbarPresentationSuspended)
                _pendingSizeRequest = request;
            else
            {
                ApplyDesiredSizeRequest(request with { SkipTransition = true }, orientation);
                appliedSize = true;
            }
        }
        if (!appliedSize)
            UpdatePositionImmediately();
        ApplyMediaBarVisibility();
    }

    private void ApplyDesiredSizeRequest(MediaBarSizeRequest request, LayoutOrientation orientation)
    {
        var maximum = GetAvailablePrimaryLengthDip(orientation);
        var minimum = orientation == LayoutOrientation.Horizontal
            ? MediaControl.MinimumPrimaryLength
            : 1;
        if (orientation == LayoutOrientation.Horizontal)
            _lengthConstraints.Update(this, minimum, maximum);
        else
            _lengthConstraints.Remove(this);
        var target = TaskbarExperiencePolicy.ResolvePrimaryLength(
            request.PrimaryLength,
            minimum,
            maximum > 0 ? maximum : double.PositiveInfinity,
            TaskbarLengthMode.FollowContent,
            request.PrimaryLength);
        var motion = MotionPolicy.ResolveCurrent();

        var current = MediaControl.CurrentLayout is { } layout
            ? orientation == LayoutOrientation.Horizontal ? layout.Canvas.Width : layout.Canvas.Height
            : target;
        // 歌词换行等离散内容切换必须立即落到目标长度：过渡动画期间新内容按旧长度渲染会被省略号截断。
        // Discrete content switches such as a lyric line change must land immediately: while the length animates the new content
        // renders at the previous length and gets clipped with an ellipsis.
        if (_applySettingsSizeImmediately || request.SkipTransition || !motion.UseContinuousMotion || Math.Abs(target - current) < LayoutSizeCalculator.MinimumChangeDip)
        {
            _sizeAnimationTimer.Stop();
            ApplyPrimaryLength(target);
            if (!_applySettingsSizeImmediately)
                UpdatePositionImmediately();
            return;
        }

        _sizeAnimationStart = current;
        _sizeAnimationTarget = target;
        _sizeAnimationProgress = 0;
        _sizeAnimationLastTimestamp = Stopwatch.GetTimestamp();
        _sizeAnimationTimer.Start();
    }

    private void PlayerMenu_OpenSettingsRequested(object? sender, EventArgs e) =>
        ViewModel.RaiseOpenSettingsRequested();

    private void PlayerMenu_OpenUpdateSettingsRequested(object? sender, EventArgs e) =>
        ViewModel.RaiseOpenUpdateSettingsRequested();

    private void PlayerMenu_ReloadTaskbarHostRequested(object? sender, EventArgs e)
    {
        PlayerMenu.IsOpen = false;
        _hostActions.RequestTaskbarHostReload();
    }

    private double GetAvailablePrimaryLengthDip(LayoutOrientation orientation)
    {
        if (_lastTaskbarHandle == IntPtr.Zero || !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var rect))
            return 0;

        var dpi = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
        if (dpi <= 0)
            return 0;

        // 设置页上限必须取当前媒体栏实际会落入的安全区间；用 0 会选到前方放不下媒体栏的窄缝，
        // 而 PositionBar 会跳过那条缝，导致滑杆与实际可用宽度不一致。
        // Match the safe range used by PositionBar. A zero requirement can select an earlier sliver
        // that cannot fit the bar, giving the settings slider a different maximum from the placed bar.
        var currentPrimary = MediaControl.CurrentLayout?.Canvas is { } canvas
            ? orientation == LayoutOrientation.Horizontal ? canvas.Width : canvas.Height
            : MediaControl.MinimumPrimaryLength;
        var requiredPixels = double.IsFinite(currentPrimary) && currentPrimary > 0
            ? (int)Math.Round(currentPrimary * dpi)
            : (int)Math.Round(MediaControl.MinimumPrimaryLength * dpi);
        var range = GetPreferredSafeRange(rect, orientation, dpi, requiredPixels, out _);
        return Math.Max(1, range.Length / dpi);
    }

    private TaskbarPrimaryRange GetPreferredSafeRange(
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int requiredPrimaryPixels,
        out bool isSafePlacement)
    {
        isSafePlacement = false;
        var primaryLength = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        var fallback = new TaskbarPrimaryRange(
            Math.Min(EdgePadding, primaryLength),
            Math.Max(Math.Min(EdgePadding, primaryLength), primaryLength - EdgePadding));
        var position = SettingsManager.Current.Position;

        if (!SettingsManager.Current.TaskbarBarAvoidIcons)
        {
            _lastStableSafeRange = null;
            _safeRangeExpansion.Reset();
            isSafePlacement = true;
            return fallback;
        }

        if (_lastTaskbarHandle == IntPtr.Zero)
            return fallback;

        // 自动隐藏后会短暂冻结占用区探测；这时不能把媒体栏退回整条任务栏的保守区间，否则 Start 定位会立即跳到左侧 20 px，
        // 然后只能等下一次 1.5 s 定位轮询才回到原位。同一任务栏、主轴长度、DPI、方向和位置偏好下，上一个已发布的安全区间比保守回退更安全。
        // Auto-hide briefly freezes occupied-area probing. Falling back to the whole taskbar here would immediately move Start placement to the left 20 px
        // and leave it there until the next 1.5 s position tick. For the same taskbar, primary length, DPI, orientation, and position preference, the last
        // published safe range is safer than that fallback.
        if (_hostActions.IsEnvironmentRecovering ||
            IsTaskbarPresentationSuspended ||
            DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
        {
            _safeRangeExpansion.Reset();
            if (TryReuseStableSafeRange(
                    primaryLength,
                    orientation,
                    dpiScale,
                    position,
                    requiredPrimaryPixels,
                    out var stableRange))
            {
                isSafePlacement = true;
                return stableRange;
            }

            return fallback;
        }

        var ranges = _occupiedAreaService.GetSafePrimaryRanges(
            _lastTaskbarHandle,
            taskbarRect,
            orientation,
            dpiScale,
            EdgePadding);
        if (ranges.Count == 0)
        {
            _safeRangeExpansion.Reset();
            if (TryReuseStableSafeRange(
                    primaryLength,
                    orientation,
                    dpiScale,
                    position,
                    requiredPrimaryPixels,
                    out var stableRange))
            {
                isSafePlacement = true;
                return stableRange;
            }

            return fallback;
        }

        // UIA 偶尔会漏报图标：先保留旧区间，扩大结果连续稳定后再采用；真正有图标侵入则立即重新选区。
        // UIA can briefly omit icons. Keep the previous range until an expanded result remains stable; shrink immediately on an intrusion.
        if (TryReuseStableSafeRange(
                primaryLength,
                orientation,
                dpiScale,
                position,
                requiredPrimaryPixels,
                out var previousRange) &&
            TaskbarFreeRangeCalculator.TryKeepSelection(
                ranges,
                previousRange,
                requiredPrimaryPixels,
                out var keptRange))
        {
            if (_safeRangeExpansion.TryAccept(ranges, previousRange, DateTime.UtcNow, out var expandedRange))
            {
                _lastStableSafeRange = new TaskbarSafeRangeSnapshot(
                    _lastTaskbarHandle, primaryLength, orientation, dpiScale, position, expandedRange);
                isSafePlacement = true;
                return expandedRange;
            }

            isSafePlacement = true;
            return keptRange;
        }

        _safeRangeExpansion.Reset();
        // 选区间 MUST 用纯策略：空闲区间里可能有比媒体栏还窄的缝隙，"最左边那条"会把媒体栏压细并钉在缝里。
        // The range MUST be chosen by the pure policy: the free ranges can hold a gap narrower than the bar itself, and "the leftmost
        // one" would squash the bar into that sliver.
        var selected = TaskbarFreeRangeCalculator.Select(ranges, position, requiredPrimaryPixels);
        _lastStableSafeRange = new TaskbarSafeRangeSnapshot(
            _lastTaskbarHandle,
            primaryLength,
            orientation,
            dpiScale,
            position,
            selected);
        isSafePlacement = selected.Length > 0;
        return selected;
    }

    private bool TryReuseStableSafeRange(
        int primaryLength,
        LayoutOrientation orientation,
        double dpiScale,
        TaskbarBarPosition position,
        int requiredPrimaryPixels,
        out TaskbarPrimaryRange range)
    {
        range = default;
        if (_lastStableSafeRange is not { } snapshot ||
            snapshot.TaskbarHandle != _lastTaskbarHandle ||
            snapshot.PrimaryLength != primaryLength ||
            snapshot.Orientation != orientation ||
            !snapshot.DpiScale.Equals(dpiScale) ||
            snapshot.Position != position ||
            snapshot.Range.Start < 0 ||
            snapshot.Range.End > primaryLength ||
            snapshot.Range.Length <= 0 ||
            requiredPrimaryPixels > 0 && snapshot.Range.Length < requiredPrimaryPixels)
        {
            return false;
        }

        range = snapshot.Range;
        return true;
    }

    private void AdvanceSizeAnimation()
    {
        if (_isClosing || _isDragging || IsTaskbarPresentationSuspended)
        {
            _sizeAnimationTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(_sizeAnimationLastTimestamp, now).TotalMilliseconds;
        _sizeAnimationLastTimestamp = now;
        var frame = MediaBarSizeAnimationCalculator.Advance(
            _sizeAnimationStart,
            _sizeAnimationTarget,
            _sizeAnimationProgress,
            elapsedMilliseconds,
            durationMilliseconds: MotionPolicy.ResolveCurrent().PositionDuration.TotalMilliseconds);
        _sizeAnimationProgress = frame.Progress;
        ApplyPrimaryLength(frame.Value);
        UpdatePositionImmediately();
        if (frame.IsCompleted)
        {
            _sizeAnimationTimer.Stop();
        }
    }

    private void MediaControl_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            EndTaskbarDrag(releaseCapture: false);
    }

    private void TaskbarWindow_PreviewMouseMove(object sender, MouseEventArgs e) => HealStaleMediaControlCapture();

    /// <summary>
    /// 全局左键抬起（低级钩子投递）后跑一遍陈旧捕获看门狗。这是窗口隧道失灵时的第二条通道：SubTree 捕获残留期间
    /// WPF 把命中测试沙盒进捕获子树，TaskbarWindow_PreviewMouseMove 一件事件都收不到，看门狗若只靠它驱动就与
    /// 病灶同归于尽；钩子在 WPF 之外观察输入，「抬起」又是每次拖动尝试的确定性终点。检查延迟到 Background 一拍：
    /// 让 WPF 先把本次抬起路由完毕（ButtonBase 等控件的合法捕获在此刻已自行释放），修复层才不会抢走按住期间
    /// 的合法捕获。
    /// Runs the stale-capture watchdog after a global left release (fed by the low-level hook). This is the second channel for
    /// when the window tunnel goes deaf: while a SubTree capture lingers, WPF sandboxes hit-testing inside the captured subtree
    /// and TaskbarWindow_PreviewMouseMove receives nothing — a watchdog driven by it alone would share the tomb with the lesion;
    /// the hook watches input outside WPF, and "release" is the deterministic endpoint of every drag attempt. The check defers
    /// one Background tick: WPF finishes routing the release first (legitimate control captures like ButtonBase's release
    /// themselves by then), so the repair layer never steals a capture that is still legally held.
    /// </summary>
    private void MouseInputMonitor_OnLeftButtonReleased(object? sender, NativeMouseButtonEventArgs e)
    {
        if (_isClosing)
            return;
        Dispatcher.BeginInvoke(HealStaleMediaControlCapture, DispatcherPriority.Background);
    }

    /// <summary>
    /// 陈旧鼠标捕获的修复层（窗口级）。「拖不动」的按下尝试可能让 Mouse.Capture(MediaControl, SubTree) 或被按下的
    /// 控件在竞态里把捕获活过抬起——释放兜底只挂在 MediaControl 的预览处理器上，而在 Popup 树内发生的抬起根本
    /// 到不了那里。捕获存续期间命中测试被锁进捕获子树：主树里的其他组件照常收事件（它们在子树内），唯独已换树进
    /// Popup 的封面永远收不到一件——悬停彻底失聪，正是"拖动尝试后封面认不出鼠标"的病灶。
    /// 判据分两支：①捕获挂在<b>已卸载的弹层树</b>上（PresentationSource 断连）——最典型是封面 tooltip 弹层关闭时
    /// 其内部"点外部关闭"用的捕获没跟着释放，挂在一棵死树上；活着的弹层（菜单、紧凑浮层、打开中的 tooltip）都
    /// 还有 source，此支碰不到它们，零误伤，因此无条件释放。②捕获挂在 MediaControl 主树（视觉子树）内——窗口级
    /// 抬起兜底覆盖不到的竞态残留。两支都要求没有任何鼠标键按下、也不在拖动中——合法捕获只存在于按下/拖动期间。
    /// 释放后按真实指针位置重同步封面悬停（见 ResyncArtworkHoverFromPointer）。
    /// Repair layer for stale mouse captures (window-level). A "can't-drag" press attempt can let the
    /// Mouse.Capture(MediaControl, SubTree) or a pressed control's capture outlive its release through races — the release
    /// backstop hangs off MediaControl's preview handlers, which a release routed inside a Popup tree never reaches. While such
    /// a capture lasts, hit-testing is locked inside the captured subtree: the other widgets in the main tree keep receiving
    /// events (they sit inside it) while the artwork — re-parented into its Popup — never receives another one, killing hover
    /// outright, the very lesion behind "the cover stops recognizing the mouse after a drag attempt". The verdict has two
    /// branches: ① the capture hangs on an <b>unloaded popup tree</b> (its PresentationSource is gone) — most typically the
    /// artwork tooltip's internal "dismiss on outside click" capture surviving the tooltip's own closure, stranded on a dead
    /// tree; live popups (menus, the compact flyout, an open tooltip) still have a source, so this branch cannot touch them and
    /// releases unconditionally. ② the capture hangs inside MediaControl's main (visual) subtree — a race leftover the
    /// window-level release backstop cannot reach. Both branches additionally require no mouse button down and no drag live —
    /// legitimate captures exist only during presses/drags. After the release, the artwork hover re-syncs from the real pointer
    /// position (see ResyncArtworkHoverFromPointer).
    /// </summary>
    private void HealStaleMediaControlCapture()
    {
        if (_isClosing || _isDragging || _isDragPending)
            return;
        if (Mouse.LeftButton == MouseButtonState.Pressed ||
            Mouse.RightButton == MouseButtonState.Pressed ||
            Mouse.MiddleButton == MouseButtonState.Pressed ||
            Mouse.XButton1 == MouseButtonState.Pressed ||
            Mouse.XButton2 == MouseButtonState.Pressed)
            return;
        if (Mouse.Captured is not Visual captured)
            return;
        // 分支①：捕获者已不在任何 PresentationSource——它的树早已卸载（tooltip 弹层关闭后的幽灵最典型），
        // 一棵死树上的捕获没有任何合法用途，直接释放。
        // Branch ①: the captor no longer belongs to any PresentationSource — its tree unloaded long ago (the tooltip-popup
        // ghost being the classic); a capture on a dead tree has no legitimate use, release it outright.
        if (PresentationSource.FromVisual(captured) is null)
        {
            var ghostTypeName = captured.GetType().Name;
            Mouse.Captured.ReleaseMouseCapture();
            AppLogService.Current?.Info("Taskbar",
                $"清除挂在已卸载弹层树上的幽灵鼠标捕获并重同步封面悬停 / released a ghost mouse capture stranded on an unloaded popup tree and re-synced the artwork hover: {ghostTypeName}");
            ResyncArtworkHoverAfterRelease();
            return;
        }
        for (var node = (DependencyObject?)captured; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (!ReferenceEquals(node, MediaControl))
                continue;
            var capturedTypeName = captured.GetType().Name;
            Mouse.Captured.ReleaseMouseCapture();
            AppLogService.Current?.Info("Taskbar",
                $"清除媒体栏主树内的陈旧鼠标捕获并重同步封面悬停 / cleared a stale mouse capture inside the media bar's main tree and re-synced the artwork hover: {capturedTypeName}");
            ResyncArtworkHoverAfterRelease();
            return;
        }
    }

    /// <summary>左键抬起后重同步原地放大的悬停：交给控制件按真实指针位置判定（见 ResyncArtworkHoverFromPointer）。
    /// Re-syncs the in-place zoom's hover after a left release: the control decides from the real pointer position (see ResyncArtworkHoverFromPointer).</summary>
    private void ResyncArtworkHoverAfterRelease() => MediaControl.ResyncArtworkHoverFromPointer();

    private void EndTaskbarDrag(bool releaseCapture = true)
    {
        _isDragging = false;
        _isDragPending = false;
        _isTaskManagerClickPending = false;
        if (releaseCapture && MediaControl.IsMouseCaptured)
            MediaControl.ReleaseMouseCapture();

        if (_pendingSizeRequest is { } request && _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            MediaControl_DesiredSizeChanged(this, new MediaBarSizeRequestEventArgs(request));
        }

        UpdatePosition();
    }

    private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 组合滚轮结束时的松键同样会合成右键菜单：判定一次取走标记，避免"按住右键滚动再松开"弹出菜单。
        // Releasing the button after a chord wheel also synthesizes the context menu, so the flag is taken once here to stop a
        // "hold the right button, scroll, release" gesture from opening the menu.
        var chordWheelClick = _mouseInputMonitor.ConsumeSuppressedClick();
        if (chordWheelClick || DateTime.UtcNow < _suppressContextMenuUntilUtc)
        {
            e.Handled = true;
            PlayerMenu.IsOpen = false;
            return;
        }

        PlayerMenu.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
    }

    private static bool IsMediaAction(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "MediaAction" })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>释放任务栏宿主事件、计时器和消息钩子。/ Releases taskbar-host events, timers, and message hooks.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _memoryPruneCoordinator.LevelChanged -= MemoryPruneCoordinator_LevelChanged;
        _timer.Stop();
        _sizeAnimationTimer.Stop();
        _spectrumTimer.Stop();
        _taskbarMotionSettleTimer.Stop();
        _taskbarHiddenTrimTimer.Stop();
        UnregisterTaskbarLocationHook();
        _lengthConstraints.Remove(this);
        DisposeMetricsSubscription();
        _outputDeviceApplyTimer.Stop();
        _quickLaunchApplyTimer.Stop();
        _volumeApplyTimer.Stop();
        SettingsManager.ExtraFeaturesSettingsChanged -= SettingsManager_ExtraFeaturesSettingsChanged;
        _occupiedAreaService.SafeRangesUpdated -= OccupiedAreaService_SafeRangesUpdated;
        _foregroundSamplingSession.Dispose();
        MediaControl.TogglePlayPauseRequested -= MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested -= MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested -= MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested -= MediaControl_ActivateSourceRequested;
        MediaControl.OpenFullPanelRequested -= MediaControl_OpenFullPanelRequested;
        MediaControl.OutputDeviceMenuRequested -= MediaControl_OutputDeviceMenuRequested;
        MediaControl.OutputDeviceWheelRequested -= MediaControl_OutputDeviceWheelRequested;
        MediaControl.VolumeMenuRequested -= MediaControl_VolumeMenuRequested;
        MediaControl.VolumeWheelRequested -= MediaControl_VolumeWheelRequested;
        MediaControl.QuickLaunchMenuRequested -= MediaControl_QuickLaunchMenuRequested;
        MediaControl.QuickLaunchWheelRequested -= MediaControl_QuickLaunchWheelRequested;
        PlayerMenu.QuickLaunchRequested -= PlayerMenu_QuickLaunchRequested;
        MediaControl.OpenTaskManagerRequested -= MediaControl_OpenTaskManagerRequested;
        MediaControl.WheelRequested -= MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged -= MediaControl_DesiredSizeChanged;
        MediaControl.RestTransitionFinished -= MediaControl_RestTransitionFinished;
        MediaControl.ArtworkZoomExtensionChanged -= MediaControl_ArtworkZoomExtensionChanged;
        PreviewMouseMove -= TaskbarWindow_PreviewMouseMove;
        _mouseInputMonitor.LeftButtonReleased -= MouseInputMonitor_OnLeftButtonReleased;
        MediaControl.OutputDeviceInfoRequested -= MediaControl_OutputDeviceInfoRequested;
        MediaControl.VolumeInfoRequested -= MediaControl_VolumeInfoRequested;
        _compactFlyout.Dispose();
        base.OnClosed(e);
    }

    private FrameworkElement GetActiveControl() => MediaControl;

    private void ApplyPrimaryLength(double primaryLength) => MediaControl.ApplyPrimaryLength(primaryLength);

    private readonly record struct TaskbarSafeRangeSnapshot(
        IntPtr TaskbarHandle,
        int PrimaryLength,
        LayoutOrientation Orientation,
        double DpiScale,
        TaskbarBarPosition Position,
        TaskbarPrimaryRange Range);
}
