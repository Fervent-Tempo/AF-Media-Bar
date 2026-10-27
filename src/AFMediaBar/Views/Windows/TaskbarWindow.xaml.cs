// 任务栏媒体栏宿主，负责停靠、定位与窗口资源释放。

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
using MenuItem = Wpf.Ui.Controls.MenuItem;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 透明子窗口覆盖整个任务栏，媒体栏通过 Canvas 放置并用 SetWindowRgn 裁剪。
/// A transparent child window spans the whole taskbar; the media bar is placed on a canvas
/// and clipped with SetWindowRgn so the rest of the taskbar remains visible and click-through.
/// </summary>
public partial class TaskbarWindow : Window, IBarRuntimeHost
{
    private readonly SpectrumPresentationSession _spectrumSession;
    // physical px offsets from the taskbar edges
    private const int EdgePadding = 20;
    private static readonly TimeSpan EnvironmentChangeProbeCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TaskbarMotionSampleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan TaskbarHiddenTrimDelay = TimeSpan.FromSeconds(30);

    private readonly ITaskbarDockService _taskBarService;
    private readonly string _targetMonitorDeviceId;
    private readonly TaskbarOccupiedAreaService _occupiedAreaService;
    private GlobalInteractionSettings EffectiveInteraction => SettingsManager.Current.Interaction;

    private readonly TaskbarLengthConstraintsService _lengthConstraints;
    public TaskbarWindowViewModel ViewModel { get; }
    private readonly ITaskbarWindowHostActions _hostActions;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _sizeAnimationTimer;
    private readonly DispatcherTimer _quickLaunchApplyTimer;
    private readonly DispatcherTimer _volumeApplyTimer;
    private readonly DispatcherTimer _taskbarMotionSettleTimer;
    private readonly DispatcherTimer _taskbarHiddenTrimTimer;
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
    private bool _hasTaskbarPresentationPlacement;
    private bool _isRestoringTaskbarPresentation;
    private IntPtr _placedTaskbarHandle;
    private RECT _placedTaskbarRect;
    private double _placedTaskbarDpiScale;
    private IntPtr _taskbarLocationHook;
    private IntPtr _hookedTaskbarHandle;
    private WinEventProc? _taskbarLocationCallback;
    private uint _taskbarThreadId;
    private readonly TaskbarOccupancyRefreshPolicy _occupancyRefresh = new();
    private readonly DispatcherTimer _occupancyRefreshTimer;
    private bool _environmentLayoutQueued;
    private bool _isDragging;
    private bool _isDragPending;
    private bool _isTaskManagerClickPending;
    private DateTime _suppressContextMenuUntilUtc;
    private DateTime _skipOccupiedAreaProbeUntilUtc;
    private bool _hasSafePlacement;
    private WindowMode? _appliedWindowMode;
    private LayoutOrientation? _appliedOrientation;
    private double _appliedLengthScalePercent = double.NaN;
    private double _appliedThicknessScalePercent = double.NaN;
    private int _appliedMediaFontSizePercent = -1;
    private bool? _appliedSmallArtwork;
    private int _dragStartCursorPrimary;
    private int _dragStartBarPrimary;
    private int _dragPrimaryLimit;
    private bool _applySettingsSizeImmediately;
    private MediaBarSizeRequest? _pendingSizeRequest;
    private MediaBarSizeRequest? _pendingRestExitSizeRequest;
    private MediaBarSizeRequest? _lastDesiredSizeRequest;
    private IntPtr _inputRegionWindowHandle;
    private RECT _lastInputRegion;
    private bool _hasInputRegion;
    /// <summary>连续"可见却不可命中"的样本数；达到策略要求后自愈重建宿主。/ Consecutive "visible but not hit-testable" samples; the host heals itself once the policy's requirement is met.</summary>
    private int _staleSurfaceSamples;
    private readonly TaskbarHostSurfaceSession _surfaceSession;
    private bool _surfaceProbeInFlight;
    private int _surfaceProbeGeneration;
    /// <summary>最近一次定位读到的任务栏 DPI 缩放；区域扩展的 DIP→物理像素换算依赖它。/ DPI scale from the latest positioning pass; the region extension's DIP-to-physical conversion relies on it.</summary>
    private double _lastDpiScale = 1.0;
    /// <summary>原地放大激活期间沿媒体栏主轴末端扩展的物理像素数；0 表示未扩展。基础矩形存于 _lastInputRegion。/ Physical pixels temporarily extending the bar's primary-axis end while the in-place artwork zoom is active; 0 when not extended. The base rect lives in _lastInputRegion.</summary>
    private int _artworkZoomExtensionPx;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly AdaptiveForegroundSamplingSession _foregroundSamplingSession;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private AudioDeviceTarget? _deviceMenuTarget;
    private int _menuEnvironmentVersion;
    private TrayIconBounds? _playerMenuOpeningAnchor;
    private QuickLaunchEntry? _pendingQuickLaunch;
    private ApplicationVolumeSnapshot? _currentVolume;
    private int? _pendingVolume;
    private int _metricCycleIndex;
    private int _metricSampleCount;
    private IReadOnlyList<MetricKind> _subscribedMetricKinds = [];
    private TimeSpan? _subscribedMetricInterval;

    public event EventHandler<FullPanelOpenRequestEventArgs>? OpenFullPanelRequested;
    public event EventHandler? InformationAutomaticallyHidden;
    public bool HasAutomaticallyHiddenInformation => !_isClosing && !_isEnvironmentSuspended && IsVisible &&
        MediaControl.HasAutomaticallyHiddenInformation;
    /// <summary>请求稳定宿主打开右键菜单；坐标为空表示鼠标入口，否则为键盘入口的物理屏幕锚点。</summary>
    public event Action<TaskbarWindow, Point?>? PlayerMenuOpenRequested;
    internal ContextMenu PlayerContextMenu => PlayerMenu;

    /// <summary>
    /// 创建任务栏媒体宿主并连接其基础设施动作。
    /// Creates the taskbar media host and connects its infrastructure actions.
    /// </summary>
    private readonly QuickLaunchService? _quickLaunch;
    public TaskbarWindow(
        AFMediaBar.Classes.Services.Audio.BluetoothBatteryService batterySource,
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
        MemoryPruneCoordinator memoryPruneCoordinator,
        TaskbarHostSurfaceSessions surfaceSessions,
        TaskbarWindowViewModel viewModel,
        AFMediaBar.ViewModels.Components.AFContextMenuViewModel menuViewModel,
        bool startImmediately = true,
        Func<ExternalFullPanelHandleWindow>? externalFullPanelHandleFactory = null,
        QuickLaunchService? quickLaunch = null,
        QuickLaunchIconService? quickLaunchIcons = null)
    {
        _quickLaunch = quickLaunch;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        ViewModel = viewModel;
        PlayerMenu.ViewModel = menuViewModel;
        _targetMonitorDeviceId = targetMonitorDeviceId;
        _surfaceSession = surfaceSessions.Get(targetMonitorDeviceId);
        _mouseInputMonitor = mouseInputMonitor;
        InitializeExternalFullPanelHandle(externalFullPanelHandleFactory);
        DataContext = ViewModel;
        MediaControl.BindBatterySource(batterySource);
        ContextMenuHelper.AttachOutsideClickDismissal(PlayerMenu);
        appearanceService.Attach(PlayerMenu, this);
        PlayerMenu.OpenSettingsRequested += PlayerMenu_OpenSettingsRequested;
        PlayerMenu.ExitRequested += PlayerMenu_ExitRequested;
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
        MediaControl.InputDeviceMenuRequested += MediaControl_InputDeviceMenuRequested;
        MediaControl.InputDeviceWheelRequested += MediaControl_InputDeviceWheelRequested;
        MediaControl.InputDeviceInfoRequested += MediaControl_InputDeviceInfoRequested;
        MediaControl.OutputDeviceWheelRequested += MediaControl_OutputDeviceWheelRequested;
        MediaControl.VolumeMenuRequested += MediaControl_VolumeMenuRequested;
        MediaControl.VolumeWheelRequested += MediaControl_VolumeWheelRequested;
        MediaControl.QuickLaunchMenuRequested += MediaControl_QuickLaunchMenuRequested;
        MediaControl.QuickLaunchWheelRequested += MediaControl_QuickLaunchWheelRequested;
        MediaControl.OpenTaskManagerRequested += MediaControl_OpenTaskManagerRequested;
        MediaControl.InformationAutomaticallyHidden += MediaControl_InformationAutomaticallyHidden;
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
        MediaControl.ArtworkPointerMoved += MediaControl_PreviewMouseMove;
        MediaControl.ArtworkPointerReleased += MediaControl_PreviewMouseLeftButtonUp;
        PreviewMouseMove += TaskbarWindow_PreviewMouseMove;
        _mouseInputMonitor.LeftButtonReleased += MouseInputMonitor_OnLeftButtonReleased;

        _taskBarService = taskBarService;
        _occupiedAreaService = occupiedAreaService;
        _occupiedAreaService.SafeRangesUpdated += OccupiedAreaService_SafeRangesUpdated;
        _lengthConstraints = lengthConstraints;
        _hostActions = hostActions;
        _interactionRouter = interactionRouter;
        _audioInteractionService = audioInteractionService;
        _audioInteractionService.DeviceSwitches.Updated += OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated += OnAudioDeviceInvalidated;
        _audioMonitorService = audioMonitorService;
        _sourceActivationService = sourceActivationService;
        _metricsMonitor = metricsMonitor;
        _memoryPruneCoordinator = memoryPruneCoordinator;
        _foregroundSamplingSession = new AdaptiveForegroundSamplingSession(
            screenBackgroundSampler,
            Dispatcher,
            GetAdaptiveForegroundSampleRegions,
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

        _occupancyRefreshTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        _occupancyRefreshTimer.Tick += OccupancyRefreshTimer_Tick;
        _sizeAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sizeAnimationTimer.Tick += (_, _) => AdvanceSizeAnimation();
        _spectrumSession = new SpectrumPresentationSession(audioMonitorService, Dispatcher,
            () => SettingsManager.Current.SpectrumComponent,
            () => !_isClosing && !IsTaskbarPresentationSuspended && _appliedOrientation.HasValue && MediaControl.IsSpectrumComponentVisible,
            (bands, count) => MediaControl.ApplySpectrum(bands.AsSpan(0, count)),
            exception => AppLogService.Current?.Warn("Audio", $"频谱采样失败: {exception.Message}"));

        // 窗口也可能在档位已经生效时被创建（屏幕关闭期间 Explorer 重建了任务栏）：那时不能等下一次档位变化，
        // 否则刚启动的计时器会一直转到一个没人看得见的窗口上。这里只走"停"的一侧，不触发恢复路径。
        // The window can also be created while a level already holds, when Explorer rebuilds the taskbar during a dark screen. It must not wait for the
        // next level change there, or the timers that just started would keep running for a window nobody can see. Only the stopping side runs here.
        if (IsBackgroundPruned)
        {
            ApplyBackgroundPruneLevel(_backgroundPruneLevel);
        }

        _quickLaunchApplyTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds) };
        _quickLaunchApplyTimer.Tick += async (_, _) =>
        {
            MediaControl.CancelQuickLaunchInteraction();
            _quickLaunchApplyTimer.Stop();
            var pending = _pendingQuickLaunch;
            _pendingQuickLaunch = null;
            if (pending is not null && CanPublishMenu(_menuEnvironmentVersion) && !_lastSnapshot.IsConnected)
            {
                if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch))
                    _compactFlyout.Dismiss();
                var environmentVersion = _menuEnvironmentVersion;
                var anchor = MediaControl.GetQuickLaunchAnchor();
                var result = await LaunchQuickLaunchEntryAsync(pending);
                if (CanPublishMenu(environmentVersion)) ShowQuickLaunchResult(result, anchor);
            }
        };
        MediaControl.QuickLaunchActionsAllowed = () => CanPublishMenu(_menuEnvironmentVersion) && !_isDragging && !IsTaskbarPresentationSuspended;
        MediaControl.QuickLaunchDirectRequested += MediaControl_QuickLaunchRequested;
        if (_quickLaunch is not null) MediaControl.BindQuickLaunch(_quickLaunch, () =>
        { _quickLaunchApplyTimer.Stop(); _pendingQuickLaunch = null; }, quickLaunchIcons);

        _volumeApplyTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds) };
        _volumeApplyTimer.Tick += async (_, _) =>
        {
            _volumeApplyTimer.Stop();
            var pending = _pendingVolume;
            var volume = _currentVolume;
            _pendingVolume = null;
            if (pending is not null && volume is not null && CanPublishMenu(_menuEnvironmentVersion))
            {
                var environmentVersion = _menuEnvironmentVersion;
                await Task.Run(() => _audioInteractionService.SetApplicationVolume(volume.ProcessName, pending.Value));
                if (CanPublishMenu(environmentVersion)) _currentVolume = volume with { VolumePercent = pending.Value, IsMuted = false };
            }
        };
        SettingsManager.ExtraFeaturesSettingsChanged += SettingsManager_ExtraFeaturesSettingsChanged;
        ApplyExtraFeaturesSettings();

        Loaded += Window_Loaded;

        if (startImmediately) Show(); else SuspendForEnvironmentRecovery();
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResumeAfterEnvironmentRecovery();
        Show();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isClosing) throw new ObjectDisposedException(nameof(TaskbarWindow));
        UpdatePosition();
        if (_lastTaskbarHandle == IntPtr.Zero || _windowHandle == IntPtr.Zero || GetParent(_windowHandle) != _lastTaskbarHandle)
            throw new InvalidOperationException("Taskbar host is unavailable.");
    }

    public void Suspend() { SuspendForEnvironmentRecovery(); Hide(); }
    public void Resume() { ResumeAfterEnvironmentRecovery(); Show(); ApplyMediaBarVisibility(); UpdatePosition(); }
    public void Dispose()
    {
        if (_isClosing) return;
        SuspendForEnvironmentRecovery();
        DetachFromTaskbar();
        try { Close(); } catch (InvalidOperationException) { }
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

    private void SuspendHostInput()
    {
        InvalidateSurfaceProbe();
        MediaControl.IsHitTestVisible = false;
        WindowHelper.SetInputTransparent(this, true);
    }

    private void RegisterTaskbarLocationHook(IntPtr taskbarHandle)
    {
        if (taskbarHandle == IntPtr.Zero ||
            taskbarHandle == _hookedTaskbarHandle && _taskbarLocationHook != IntPtr.Zero)
            return;

        UnregisterTaskbarLocationHook();
        _taskbarLocationCallback ??= (_, eventType, hwnd, objectId, _, eventThread, _) =>
        {
            if (_isClosing) return;
            if (eventType == EVENT_OBJECT_LOCATIONCHANGE && hwnd == _lastTaskbarHandle && objectId == OBJID_WINDOW)
            {
                Dispatcher.BeginInvoke(() => ObserveTaskbarMotion(allowStableSample: false), DispatcherPriority.Send);
                return;
            }
            if (eventType is not (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x8004 or EVENT_OBJECT_LOCATIONCHANGE)) return;
            // 销毁后父子关系已不可查询，绑定线程内的销毁只请求受控补探测。
            if (hwnd != _lastTaskbarHandle && !IsChild(_lastTaskbarHandle, hwnd) &&
                !(eventType == 0x8001 && eventThread == _taskbarThreadId)) return;
            QueueOccupancyRefresh();
        };

        _taskbarThreadId = GetWindowThreadProcessId(taskbarHandle, out var processId);
        _taskbarLocationHook = SetWinEventHook(
            0x8000,
            EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _taskbarLocationCallback,
            unchecked((uint)processId),
            0,
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
    private void ObserveTaskbarMotion(bool allowStableSample = true)
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
        // 重复 WinEvent 不能压缩两个 50 ms 稳定样本，否则会在 Shell 动画中途恢复布局与输入。
        _taskbarMotionState = TaskbarMotionPolicy.Observe(previous, taskbarRect, monitorBounds, orientation, allowStableSample);
        if (!allowStableSample && _taskbarMotionState == previous)
            return;

        if (_taskbarMotionState.IsMoving)
        {
            _taskbarMotionSettleTimer.Stop();
            _taskbarMotionSettleTimer.Start();
        }
        else
        {
            _taskbarMotionSettleTimer.Stop();
        }

        if (previous.IsMoving == _taskbarMotionState.IsMoving && previous.IsHidden == _taskbarMotionState.IsHidden)
        {
            if (_taskbarMotionState.IsMoving)
                ApplyMediaBarVisibility();
            return;
        }

        ApplyTaskbarPresentationState(previous);
    }

    private void ApplyTaskbarPresentationState(TaskbarMotionState previous)
    {
        var suspended = IsTaskbarPresentationSuspended;
        SuspendHostInput();

        if (suspended)
        {
            _externalFullPanelHandle?.SuspendTaskbar();
            StopOccupancyRefresh();
            _placementState = TaskbarPlacementPolicy.ClearConfirmation(_placementState);
            MediaControl.ApplyHostVisibilitySuspension(true);
            // 冻结任务栏相对几何供父窗口带动；稳定后仍需重新取得安全区间，才能恢复布局与输入。
            _hasSafePlacement = false;
            StopLayoutTransition();
            if (_lastDesiredSizeRequest is { } desiredSize)
                _pendingSizeRequest = desiredSize;
            _spectrumSession.Stop();
            DisposeMetricsSubscription();
            _foregroundSamplingSession.Invalidate(clearDecision: false);
            // 点击浮窗使用独立 HWND；这里只挂起媒体栏，任务栏收起不结束菜单交互。
            _isTaskManagerClickPending = false;
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
                        $"任务栏已自动隐藏，宿主暂停: {_targetMonitorDeviceId}");
            }
            else
            {
                _taskbarHiddenTrimTimer.Stop();
            }

            // 使用冻结的任务栏相对位置随父 HWND 运动，不写自己的位置或尺寸。
            ApplyMediaBarVisibility();
            return;
        }

        _taskbarHiddenTrimTimer.Stop();
        if (previous.IsHidden || previous.IsMoving)
            AppLogService.Current?.Info("Taskbar",
                $"任务栏动画稳定，开始恢复宿主: {_targetMonitorDeviceId}");

        // 稳定样本已确认动画结束，立即请求安全区，并以短时刷新完成后续确认。
        // 首次隐藏启动没有可复用定位，不能再等慢速定位轮询触发首次探测。
        QueueOccupancyRefresh();

        // 运动期间只复用原生相对几何；稳定后先校正最终尺寸、定位和裁剪，最后恢复输入。
        _hasTaskbarPresentationPlacement = false;
        _isRestoringTaskbarPresentation = true;
        try
        {
            // 恢复歌词画面时仍拒绝输入；尺寸请求在本次定位中一次落到最终值。
            MediaControl.ApplyHostVisibilitySuspension(false);
            if (_pendingSizeRequest is { } request && _appliedOrientation is { } orientation)
            {
                _pendingSizeRequest = null;
                ApplyDesiredSizeRequest(request with { SkipTransition = true }, orientation);
            }
            UpdatePositionImmediately();
        }
        finally
        {
            _isRestoringTaskbarPresentation = false;
        }

        ApplyMediaBarVisibility();
        ResumeOperationalTimers();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCDESTROY)
        {
            // Explorer 会在 TaskbarCreated 到达 MainWindow 前销毁任务栏子 HWND；立即阻止媒体回调。
            // Explorer destroys taskbar child HWNDs before TaskbarCreated reaches MainWindow;
            // block media callbacks immediately so they cannot show an already closed Window.
            _isClosing = true;
            _menuEnvironmentVersion++;
            MediaControl.SuspendQuickLaunchPresentation();
            MediaControl.ApplyHostVisibilitySuspension(true);
            PlayerMenu.IsOpen = false;
            StopOccupancyRefresh();
            _windowHandle = IntPtr.Zero;
            _timer.Stop();
            StopLayoutTransition();
            _spectrumSession.Stop();
            _taskbarMotionSettleTimer.Stop();
            _taskbarHiddenTrimTimer.Stop();
            UnregisterTaskbarLocationHook();
            DisposeMetricsSubscription();
            MediaControl.CancelQuickLaunchInteraction();
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
            StopLayoutTransition();
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
        // 每个轮询周期只采一次，且让本轮定位和 WPF 渲染先执行，避免把刚显示的空白帧累计成失绘。
        _ = VerifyHostSurfaceAsync();
        _foregroundSamplingSession.RequestRefresh();
    }

    private void OccupiedAreaService_SafeRangesUpdated(object? sender, TaskbarSafeRangesUpdatedEventArgs e)
    {
        if (_isClosing || e.TaskbarHandle != _lastTaskbarHandle)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing && !_isEnvironmentSuspended && e.TaskbarHandle == _lastTaskbarHandle)
                UpdatePositionImmediately();
        }, DispatcherPriority.Input);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _setupComplete = true;
        ResumeOperationalTimers();
        SynchronizeSpectrumTimer();
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

            // 布局刷新可能请求安全区；先识别隐藏状态，避免首次布局绕过探测门禁。
            ObserveTaskbarMotion();
            ApplyLayoutSettings(WindowMode.Taskbar, SettingsManager.Current.LayoutOrientationMode,
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

        // 候选宿主先恢复状态、再 Show；首次 HWND 尚未创建不代表旧宿主已被 Explorer 销毁。
        // 初次定位由 Loaded 完成，避免激活期间排队重建并取消自己的运行模式切换。
        if (!_setupComplete && _windowHandle == IntPtr.Zero)
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

            ApplyLayoutSettings(WindowMode.Taskbar, SettingsManager.Current.LayoutOrientationMode,
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
        if (_isClosing || _isEnvironmentSuspended || _positionUpdateInProgress || IsTaskbarPresentationSuspended ||
            taskbarHandle != _lastTaskbarHandle || taskbarWindowHandle != _windowHandle)
            return;

        // 排队定位可能先于 WinEvent 投递执行；写原生几何前重新检查 Shell 是否已经开始移动。
        ObserveTaskbarMotion(allowStableSample: false);
        if (IsTaskbarPresentationSuspended)
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
            _taskBarService.SetWindowPosition(taskbarWindowHandle, taskbarHandle,
                taskbarWidth, taskbarHeight);

            // Place the bar on the canvas and clip the window to it
            RECT barRect = PositionBar(taskbarRect, dpiScale);
            if (HasArea(barRect))
            {
                if (!_hasInputRegion || _inputRegionWindowHandle != taskbarWindowHandle ||
                    !SameRect(_lastInputRegion, barRect))
                {
                    // 首次取得有效区域时请求重画。
                    ApplyBarInputRegion(taskbarWindowHandle, barRect, redraw: !_hasInputRegion);
                    _inputRegionWindowHandle = taskbarWindowHandle;
                    _lastInputRegion = barRect;
                    _hasInputRegion = true;
                }
            }
            else
            {
                // 安全几何未就绪时保留原生区域，由显隐门禁隐藏宿主。
                _hasInputRegion = false;
            }
            if (!_returnTransition.IsActive)
                ResumePlacementInteraction();
            // 首次占用区探测完成前宿主保持隐藏；发布事件会立即重跑定位并在安全几何落地后显示。
            // Keep the host hidden until its first occupancy probe completes; publication immediately repositions and reveals it after safe geometry lands.
            _hasTaskbarPresentationPlacement = _hasSafePlacement;
            _placedTaskbarHandle = taskbarHandle;
            _placedTaskbarRect = taskbarRect;
            _placedTaskbarDpiScale = dpiScale;
            ApplyMediaBarVisibility();
            CommitPlacement();
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private RECT PositionBar(RECT taskbarRect, double dpiScale) => PositionPrimaryBar(taskbarRect, dpiScale);

    private static bool SameRect(RECT left, RECT right) =>
        left.Left == right.Left && left.Top == right.Top &&
        left.Right == right.Right && left.Bottom == right.Bottom;

    private static bool HasArea(RECT rect) => rect.Right > rect.Left && rect.Bottom > rect.Top;

    /// <summary>
    /// 沿主轴把放大期间的可见与输入区扩到内容溢出的一侧（随展开方向翻到首端或尾端），窗口仍铺满任务栏；
    /// WPF 裁剪由控件配合解除。扩展带在重定位时保留。
    /// Widens the zoom's visible and input region along the primary axis to the side the content overflows (flipping to the
    /// leading or trailing end with the expansion direction) while the window still spans the taskbar; the WPF clip is
    /// released by the control in step. The extension band survives repositioning.
    /// </summary>
    private void ApplyBarInputRegion(IntPtr taskbarWindowHandle, RECT baseRect, bool redraw)
    {
        var effective = TaskbarArrangementPolicy.ExtendZoomOverflow(
            baseRect, _artworkZoomExtensionPx, IsVerticalPresentation, MediaControl.ResolvedTaskbarArrangement);
        _taskBarService.ApplyInputRegion(taskbarWindowHandle, [effective], redraw);
    }

    private bool CanVerifyHostSurface =>
        !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned && !IsTaskbarPresentationSuspended &&
        !_isDragging && !_isRestoringTaskbarPresentation && !_isPlacementInteractionSuspended &&
        !_returnTransition.IsActive && !MediaControl.IsRestConnectionTransitionActive && _hasInputRegion &&
        Visibility == Visibility.Visible && _hasTaskbarPresentationPlacement &&
        SettingsManager.Current.TaskbarBarEnabled && !MediaControl.ShouldHideTaskbarWindow &&
        !_hostActions.IsEnvironmentRecovering && _windowHandle != IntPtr.Zero && _lastTaskbarHandle != IntPtr.Zero;

    private void InvalidateSurfaceProbe()
    {
        _staleSurfaceSamples = 0;
        _surfaceProbeGeneration++;
        _surfaceSession.RecordUnhealthy();
    }

    /// <summary>后台检查可交互区域；连续异常只作为重建依据，不推断 WPF 是否停止提交像素。</summary>
    private async Task VerifyHostSurfaceAsync()
    {
        if (_surfaceProbeInFlight) return;
        _surfaceProbeInFlight = true;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (!CanVerifyHostSurface) { InvalidateSurfaceProbe(); return; }

            var generation = _surfaceProbeGeneration;
            var windowHandle = _windowHandle;
            var taskbarHandle = _lastTaskbarHandle;
            var region = _lastInputRegion;
            var motion = _taskbarMotionState;
            var hit = await Task.Run(() => _taskBarService.ProbeHostSurface(windowHandle, taskbarHandle, region));
            if (_isClosing) return;
            // 隐藏、关闭、重建或布局改变后的结果不能触发重载。
            if (generation != _surfaceProbeGeneration || !CanVerifyHostSurface || windowHandle != _windowHandle ||
                taskbarHandle != _lastTaskbarHandle || !SameRect(region, _lastInputRegion) || motion != _taskbarMotionState)
            {
                InvalidateSurfaceProbe();
                return;
            }

            if (!TaskbarHostSurfacePolicy.IsStaleSample(hit))
            {
                _staleSurfaceSamples = 0;
                if (hit == TaskbarHostSurfaceHit.Host) _surfaceSession.RecordHealthy();
                else _surfaceSession.RecordUnhealthy();
                return;
            }

            _surfaceSession.RecordUnhealthy();
            _staleSurfaceSamples = Math.Min(_staleSurfaceSamples + 1, TaskbarHostSurfacePolicy.RequiredStaleSamples);
            if (!_surfaceSession.ShouldReload(_staleSurfaceSamples, DateTime.UtcNow)) return;

            _staleSurfaceSamples = 0;
            _surfaceSession.RecordReload(DateTime.UtcNow);
            AppLogService.Current?.Warn("Taskbar", $"媒体栏连续采样未命中宿主，请求重建: {_targetMonitorDeviceId}, " +
                $"region=({region.Left},{region.Top})-({region.Right},{region.Bottom}), " +
                $"reloads={_surfaceSession.Reloads}, " +
                $"cooldown={TaskbarHostSurfacePolicy.GetCooldown(_surfaceSession.ConsecutiveReloads).TotalSeconds:F0}s");
            _hostActions.RequestTaskbarHostReload();
        }
        catch (Exception exception)
        {
            if (_isClosing) return;
            InvalidateSurfaceProbe();
            AppLogService.Current?.Warn("Taskbar", $"媒体栏表面检查失败: {exception.Message}");
        }
        finally { _surfaceProbeInFlight = false; }
    }

    /// <summary>
    /// 原地放大时同步推挤组件的输入区；收回后还原。扩展带暂时沿主轴扩占任务栏区域（落在内容溢出侧，
    /// 随展开方向翻到首端或尾端），重定位时保留该扩展量。
    /// </summary>
    private void MediaControl_ArtworkZoomExtensionChanged(object? sender, double extraDip)
    {
        if (_isClosing)
            return;

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
            ApplyBarInputRegion(_inputRegionWindowHandle, _lastInputRegion, redraw: false);
    }

    #endregion

    #region SMTC

    /// <summary>
    /// 在任务栏宿主上应用媒体快照并安排位置刷新。
    /// Applies a media snapshot to the taskbar host and schedules repositioning.
    /// </summary>
    /// <param name="snapshot">不可变媒体快照 / Immutable media snapshot.</param>
    /// <param name="lyricsSettingsRefresh">
    /// 本次快照重放是否由歌词呈现类设置变更触发。传 true 时歌词帧遵循"可见内容一字未变就不重发"，
    /// 与演示器守卫合成同一条规则；媒体快照递送不传，保持原有行为。
    /// Whether this snapshot replay is driven by a lyrics-presentation settings change. With true the lyrics
    /// frame follows "no visible change, no re-send", forming one rule with the presenter's own guard; media
    /// snapshot delivery does not pass it and keeps its behaviour.
    /// </param>
    public void ApplySnapshot(MediaSnapshot snapshot, bool lyricsSettingsRefresh = false)
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
        if (!snapshot.IsConnected)
            _externalFullPanelHandle?.Dismiss();
        if (snapshot.IsConnected && !wasConnected)
        {
            _pendingRestExitSizeRequest = null;
            MediaControl.CancelQuickLaunchInteraction();
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch)) _compactFlyout.Dismiss();
        }

        if (!_timer.IsEnabled && !IsTaskbarPresentationSuspended)
            _timer.Start();

        // Delegate UI update to the original media control and its taskbar-only overlay.
        MediaControl.AllowRestConnectionTransition = !_isDragging && !IsTaskbarPresentationSuspended &&
            !_hostActions.IsEnvironmentRecovering;
        MediaControl.UpdateSongInfo(snapshot, lyricsSettingsRefresh);
        MediaControl.ApplyAppearanceSettings();
        SynchronizeSpectrumTimer();
        // 新宿主构造时仍是断开快照；若性能组件没有配置为“无媒体时保留”，构造阶段不会取得指标租约。
        // 重放当前媒体快照会改变控件算出的组件显隐，因此必须在显隐落地后同步一次租约。这里不强制续租，
        // 否则约 240 ms 一次的媒体快照会不断重置性能指标轮换与刷新间隔。
        // A new host is still disconnected while constructed; unless performance is kept without media, it acquires no metric lease then.
        // Replaying the current media snapshot changes the control-owned visibility decision, so synchronize the lease after that decision lands.
        // This path must not force renewal, or media snapshots arriving about every 240 ms would continually reset metric rotation and cadence.
        SynchronizeMetricsSubscription(SettingsManager.Current.PerformanceComponent.Normalize());

        // A media progress snapshot does not change taskbar geometry. Size changes already
        // request placement through DesiredSizeChanged; the periodic timer covers Shell changes.
        if (snapshot.IsConnected != wasConnected)
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

        var orientation = _taskBarService.IsTaskbarVertical(taskbarHandle)
            ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
        double? crossAxisDip = null;
        if (_taskBarService.TryGetTaskbarRect(taskbarHandle, out var actualRect))
        {
            var actualDpi = _taskBarService.GetTaskbarDpiScale(taskbarHandle);
            crossAxisDip = Math.Max(1, (orientation == LayoutOrientation.Vertical ? actualRect.Right - actualRect.Left : actualRect.Bottom - actualRect.Top) / Math.Max(0.1, actualDpi));
            var monitor = MonitorFromWindow(taskbarHandle, 2);
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
            if (orientation == LayoutOrientation.Vertical && GetMonitorInfo(monitor, ref info))
                MediaControl.ApplyVerticalTaskbarSide(actualRect.Left + actualRect.Right < info.rcMonitor.Left + info.rcMonitor.Right);
        }

        // 应用布局到媒体控件
        // Apply layout to media control
        var orientationChanged = _appliedOrientation != orientation;
        var lengthScalePercent = SettingsManager.Current.LayoutLengthScalePercent;
        var thicknessScalePercent = ResolveTaskbarThicknessScalePercent(
            taskbarHandle,
            orientation,
            SettingsManager.Current.LayoutThicknessScalePercent);
        if (orientation == LayoutOrientation.Horizontal && crossAxisDip.HasValue)
            crossAxisDip = Math.Min(crossAxisDip.Value, LayoutPresets.TaskbarHorizontal.Canvas.Height * thicknessScalePercent / 100);
        var mediaFontSizePercent = SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        // 小封面会改变封面组件的尺寸与位置，必须参与重建判定，否则开关只在下次启动时生效。
        var smallArtwork = SettingsManager.Current.TaskbarExperience.SmallArtwork;
        var layoutChanged = _appliedWindowMode != windowMode ||
                            orientationChanged ||
                            !lengthScalePercent.Equals(_appliedLengthScalePercent) ||
                            !thicknessScalePercent.Equals(_appliedThicknessScalePercent) ||
                            (crossAxisDip.HasValue && Math.Abs((orientation == LayoutOrientation.Vertical ? MediaControl.CurrentLayout?.Canvas.Width ?? 0 : MediaControl.CurrentLayout?.Canvas.Height ?? 0) - crossAxisDip.Value) > 0.1) ||
                            mediaFontSizePercent != _appliedMediaFontSizePercent ||
                            smallArtwork != _appliedSmallArtwork;
        if (layoutChanged)
        {
            var previousImmediate = _applySettingsSizeImmediately;
            _applySettingsSizeImmediately = true;
            try
            {
                MediaControl.ApplyLayout(windowMode, orientation, lengthScalePercent, thicknessScalePercent, crossAxisDip);
                MediaControl.ApplyAppearanceSettings();
                _appliedWindowMode = windowMode;
                _appliedOrientation = orientation;
                _appliedLengthScalePercent = lengthScalePercent;
                _appliedThicknessScalePercent = thicknessScalePercent;
                _appliedMediaFontSizePercent = mediaFontSizePercent;
                _appliedSmallArtwork = smallArtwork;
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

    private IReadOnlyList<Int32Rect> GetAdaptiveForegroundSampleRegions()
    {
        if (_isClosing || _isEnvironmentSuspended || IsTaskbarPresentationSuspended || _isDragging || IsBackgroundPruned || Visibility != Visibility.Visible ||
            SettingsManager.Current.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic)
        {
            return [];
        }

        return MediaControl.GetForegroundSampleRegions();
    }

    /// <summary>在全局左键点击位于菜单外时关闭右键菜单。 / Closes the context menu after a global left click outside it.</summary>
    public void CloseContextMenuIfOutside(int screenX, int screenY) =>
        ContextMenuHelper.CloseIfOutside(PlayerMenu, screenX, screenY);

    /// <summary>关闭任务栏媒体菜单。/ Closes the taskbar media menu.</summary>
    internal void ClosePlayerMenu()
    {
        PlayerMenu.IsOpen = false;
        PlayerMenu.PlacementTarget = null;
    }

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
        _menuEnvironmentVersion++;
        MediaControl.SuspendQuickLaunchPresentation();
        _compactFlyout.Dismiss();
        _volumeApplyTimer.Stop();
        _pendingVolume = null;
        _audioInteractionService.CancelDeviceOwner(this);
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
            ApplyLayoutSettings(WindowMode.Taskbar, SettingsManager.Current.LayoutOrientationMode,
                taskbarHandle);
            ApplyExtraFeaturesSettings();
            MediaControl.UpdateSongInfo(_lastSnapshot);
            SynchronizeSpectrumTimer();
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
    /// 运动期间复用稳定的任务栏相对位置并跟随父 HWND，稳定收起时隐藏窗口，恢复后重新断言几何。
    /// </summary>
    private void ApplyMediaBarVisibility()
    {
        if (_isClosing || _isEnvironmentSuspended)
        {
            return;
        }

        var hasSafePlacement = _appliedOrientation == LayoutOrientation.Horizontal
            ? _hasSafePlacement : !SettingsManager.Current.TaskbarBarAvoidIcons || _hasSafePlacement;
        var reusablePlacement = _hasTaskbarPresentationPlacement && _placedTaskbarHandle == _lastTaskbarHandle &&
            GetParent(_windowHandle) == _lastTaskbarHandle &&
            _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle) == _placedTaskbarDpiScale &&
            TaskbarMotionPolicy.CanReusePlacement(_placedTaskbarRect, _taskbarMotionState.LastRect,
                _appliedOrientation ?? LayoutOrientation.Horizontal);
        var resolved = TaskbarHostVisibilityPolicy.Resolve(_taskbarMotionState,
            MediaControl.ShouldHideTaskbarWindow, reusablePlacement);
        if (resolved == TaskbarHostVisibility.Visible && !IsTaskbarPresentationSuspended &&
            (!hasSafePlacement || !_hasTaskbarPresentationPlacement))
            resolved = TaskbarHostVisibility.Collapsed;
        var target = resolved == TaskbarHostVisibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility != target)
        {
            InvalidateSurfaceProbe();
            Visibility = target;
            if (target == Visibility.Visible)
                WidgetCanvas.InvalidateVisual();
        }
        // 首次安全区间可能稍后才发布；这一条也负责恢复延迟定位后的输入，不能只在运动结束时恢复。
        if (target == Visibility.Visible && !IsTaskbarPresentationSuspended &&
            !_isRestoringTaskbarPresentation && !MediaControl.IsHitTestVisible)
            ResumeOperationalTimers();
    }

    /// <summary>安全停止任务栏宿主并解除 Explorer 停靠。/ Safely stops the taskbar host and detaches it from Explorer.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;

        _isClosing = true;
        StopOccupancyRefresh();
        SuspendForEnvironmentRecovery();
        DetachFromTaskbar();
    }

    /// <summary>
    /// 停止旧任务栏宿主的布局、动画和菜单更新，等待环境恢复时重建窗口。
    /// Stops layout, animation, and menu updates on an old taskbar host until it is recreated.
    /// </summary>
    internal void SuspendForEnvironmentRecovery()
    {
        _externalFullPanelHandle?.Dismiss(immediate: true);
        _audioInteractionService.CancelDeviceOwner(this);
        if (_isEnvironmentSuspended)
            return;

        _placementState = TaskbarPlacementPolicy.ClearConfirmation(_placementState);
        InvalidateSurfaceProbe();
        _isEnvironmentSuspended = true;
        _menuEnvironmentVersion++;
        MediaControl.SuspendQuickLaunchPresentation();
        StopOccupancyRefresh();
        _hasTaskbarPresentationPlacement = false;
        Visibility = Visibility.Collapsed;
        UnregisterTaskbarLocationHook();
        _timer.Stop();
        StopLayoutTransition();
        _spectrumSession.Stop();
        _taskbarMotionSettleTimer.Stop();
        _taskbarHiddenTrimTimer.Stop();
        DisposeMetricsSubscription();
        MediaControl.CancelQuickLaunchInteraction();
        _quickLaunchApplyTimer.Stop();
        _volumeApplyTimer.Stop();
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
        // 隐藏启动及等待首次安全区时也必须保留定位轮询，不能依赖成功显示来启动恢复链。
        if (!IsBackgroundPruned)
            _timer.Start();
        RegisterTaskbarLocationHook(_lastTaskbarHandle);
        _taskbarMotionState = default;
        ObserveTaskbarMotion();
        if (!IsTaskbarPresentationSuspended)
        {
            UpdatePositionImmediately();
            ResumeOperationalTimers();
        }
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
        _externalFullPanelHandle?.Dismiss(immediate: true);
        _backgroundPruneLevel = level;
        InvalidateSurfaceProbe();

        // 控件自己那几只计时器由宿主转达；控件不查询电源状态（所有权与依赖方向见 IMPLEMENTATION_CONSTRAINTS）。
        // The control's own timers are passed down by the host; the control never queries the power state, which keeps ownership and dependency
        // direction as IMPLEMENTATION_CONSTRAINTS requires.
        MediaControl.ApplyBackgroundPruneLevel(level);

        if (!IsBackgroundPruned)
        {
            ResumeOperationalTimers();
            return;
        }

        _menuEnvironmentVersion++;
        MediaControl.SuspendQuickLaunchPresentation();
        _compactFlyout.Dismiss();
        PlayerMenu.IsOpen = false;
        _audioInteractionService.CancelDeviceOwner(this);
        _quickLaunchApplyTimer?.Stop();
        _volumeApplyTimer?.Stop();
        _pendingQuickLaunch = null;
        _pendingVolume = null;
        _timer.Stop();
        StopOccupancyRefresh();
        StopLayoutTransition();
        _spectrumSession.Stop();
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
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned)
            return;

        // 观察任务栏的轮询不依赖媒体栏可见；输入、媒体采样仍等待安全定位完成。
        if (!_timer.IsEnabled)
            _timer.Start();
        if (IsTaskbarPresentationSuspended ||
            _isRestoringTaskbarPresentation || Visibility != Visibility.Visible || !_hasTaskbarPresentationPlacement)
            return;

        MediaControl.ApplyHostVisibilitySuspension(false);
        MediaControl.IsHitTestVisible = true;
        WindowHelper.SetInputTransparent(this, false);
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

    private void MediaControl_InformationAutomaticallyHidden(object? sender, EventArgs args)
    {
        if (HasAutomaticallyHiddenInformation) InformationAutomaticallyHidden?.Invoke(this, EventArgs.Empty);
    }

    private void MediaControl_OpenFullPanelRequested(object? sender, EventArgs e) =>
        OpenFullPanelRequested?.Invoke(this, new FullPanelOpenRequestEventArgs(
            GetMediaBarScreenBounds(), IsVerticalPresentation, ensureOpen: false));

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


    private async void MediaControl_OutputDeviceMenuRequested(object? sender, EventArgs e) =>
        await ShowDeviceMenuAsync(AudioDeviceDirection.Output, EffectiveInteraction.OutputDeviceClickTarget, MediaControl.GetOutputDeviceAnchor());
    private async void MediaControl_InputDeviceMenuRequested(object? sender, EventArgs e) =>
        await ShowDeviceMenuAsync(AudioDeviceDirection.Input, EffectiveInteraction.InputDeviceClickTarget, MediaControl.GetInputDeviceAnchor());
    private bool CanPublishMenu(int environmentVersion) =>
        !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned && environmentVersion == _menuEnvironmentVersion;

    private async Task ShowDeviceMenuAsync(AudioDeviceDirection direction, AudioDeviceSwitchTarget scope, TrayIconBounds anchor)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion) || scope == AudioDeviceSwitchTarget.Disabled) return;
        var environmentVersion = _menuEnvironmentVersion;
        var mode = direction == AudioDeviceDirection.Output ? TaskbarCompactFlyoutMode.OutputDevice : TaskbarCompactFlyoutMode.InputDevice;
        if (_compactFlyout.IsShowing(mode)) { _compactFlyout.Dismiss(); return; }
        try
        {
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, false);
            if (!CanPublishMenu(environmentVersion)) return;
            if (target is null) { MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            var devices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, devices);
            var route = await _audioInteractionService.DeviceSwitches.ReadRouteAsync(target);
            if (!CanPublishMenu(environmentVersion)) return;
            _deviceMenuTarget = target;
            _compactFlyout.ShowDevices(target, devices, selected, anchor, route.IsSupported && route.Error is null);
            if (!route.IsSupported || route.Error is not null)
            {
                _compactFlyout.ShowDeviceStatus(Translations.Get(route.Error ?? "Audio.Route.Unsupported"));
                return;
            }
            if (selected is null) _compactFlyout.ShowDeviceStatus(target.Application is null ?
                await _audioInteractionService.GetDeviceDescriptionAsync(direction) : Translations.Get("Audio.Route.HiddenOrDisconnected"));
        }
        catch (Exception exception) { Debug.WriteLine(exception); if (!_isClosing) MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Route.Failed")); }
    }
    private async void MediaControl_OutputDeviceWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e) =>
        await PreviewDeviceAsync(AudioDeviceDirection.Output, e.Delta);
    private async void MediaControl_InputDeviceWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e) =>
        await PreviewDeviceAsync(AudioDeviceDirection.Input, e.Delta);
    private async Task PreviewDeviceAsync(AudioDeviceDirection direction, int delta)
    {
        try
        {
            var settings = EffectiveInteraction;
            var scope = direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
            if (!CanPublishMenu(_menuEnvironmentVersion) || scope == AudioDeviceSwitchTarget.Disabled) return;
            var environmentVersion = _menuEnvironmentVersion;
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, true);
            if (!CanPublishMenu(environmentVersion)) return;
            if (target is null) { MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            await _audioInteractionService.DeviceSwitches.PreviewAsync(target, TrayWheelPolicy.GetDeviceSteps(delta), this);
        }
        catch (Exception exception) { Debug.WriteLine(exception); if (!_isClosing) MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Route.Failed")); }
    }
    private async void CompactFlyout_OutputDeviceWheelRequested(int delta)
    {
        if (_deviceMenuTarget is not { } target || _isClosing) return;
        try { await _audioInteractionService.DeviceSwitches.PreviewAsync(target, TrayWheelPolicy.GetDeviceSteps(delta), this); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private async void MediaControl_OutputDeviceSelected(AudioDeviceOption device)
    {
        if (_deviceMenuTarget is not { } target || _isClosing) return;
        try
        {
            var result = await _audioInteractionService.DeviceSwitches.SelectAsync(target, device, this);
            if (!_isClosing) MediaControl.SetDeviceTooltip(target.Direction, AudioDevicePresentation.GetResultText(result, target.Application is not null));
        }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private async void MediaControl_OutputDeviceInfoRequested(object? sender, EventArgs e) => await RefreshDeviceTooltipAsync(AudioDeviceDirection.Output);
    private async void MediaControl_InputDeviceInfoRequested(object? sender, EventArgs e) => await RefreshDeviceTooltipAsync(AudioDeviceDirection.Input);
    private async Task RefreshDeviceTooltipAsync(AudioDeviceDirection direction)
    {
        try
        {
            var settings = EffectiveInteraction;
            var scope = direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
            if (scope == AudioDeviceSwitchTarget.Disabled) { MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Device.Disabled")); return; }
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, true);
            if (_isClosing) return;
            if (target is null) { MediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            var choices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, choices);
            var route = await _audioInteractionService.DeviceSwitches.ReadRouteAsync(target);
            var detail = !route.IsSupported || route.Error is not null ? Translations.Get(route.Error ?? "Audio.Route.Unsupported") :
                selected?.DisplayName ?? (target.Application is null ? await _audioInteractionService.GetDeviceDescriptionAsync(direction) :
                    Translations.Get("Audio.Route.HiddenOrDisconnected"));
            if (!_isClosing) MediaControl.SetDeviceTooltip(direction, (target.Application is null ? string.Empty : target.Application.DisplayName + " · ") + detail);
        }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private void OnAudioDeviceSwitchUpdated(AudioDeviceSwitchUpdate update) => Dispatcher.BeginInvoke(() =>
    {
        if (_isClosing) return;
        var settings = EffectiveInteraction;
        var scope = update.Target.Direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
        if ((scope == AudioDeviceSwitchTarget.SystemDefault && update.Target.Application is null) ||
            (scope == AudioDeviceSwitchTarget.ForegroundApplication && update.Target.Application is not null))
        {
            var detail = update.Result is null ? update.Candidate?.DisplayName ?? Translations.Get("Audio.Device.Unavailable") :
                AudioDevicePresentation.GetResultText(update.Result, update.Target.Application is not null);
            MediaControl.SetDeviceTooltip(update.Target.Direction, string.IsNullOrEmpty(detail) ? string.Empty :
                (update.Target.Application is null ? string.Empty : update.Target.Application.DisplayName + " · ") + detail);
        }
        if (_deviceMenuTarget?.Key == update.Target.Key && _compactFlyout.IsVisible)
        {
            if (update.Result is null && update.Candidate is { } candidate) _compactFlyout.SetOutputDevicePreview(candidate);
            else if (update.Result is { } result)
            {
                _compactFlyout.ShowDeviceStatus(AudioDevicePresentation.GetResultText(result, update.Target.Application is not null));
                _ = RefreshOpenDeviceMenuAsync();
            }
        }
    });
    private void OnAudioDeviceInvalidated(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() =>
    {
        if (!_isClosing) _ = RefreshOpenDeviceMenuAsync();
    });
    private async Task RefreshOpenDeviceMenuAsync()
    {
        var environmentVersion = _menuEnvironmentVersion;
        if (_deviceMenuTarget is not { } target || !_compactFlyout.IsVisible ||
            !_compactFlyout.IsShowing(target.Direction == AudioDeviceDirection.Output ? TaskbarCompactFlyoutMode.OutputDevice : TaskbarCompactFlyoutMode.InputDevice)) return;
        try
        {
            var devices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, devices);
            if (!CanPublishMenu(environmentVersion) || !_compactFlyout.IsVisible || _deviceMenuTarget?.Key != target.Key) return;
            _compactFlyout.UpdateDevices(devices, selected, _audioInteractionService.DeviceSwitches.HasPending(target));
        }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }

    private async void MediaControl_VolumeMenuRequested(object? sender, EventArgs e)
    {
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.Volume))
        {
            _compactFlyout.Dismiss();
            return;
        }

        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        var environmentVersion = _menuEnvironmentVersion;
        var openingAnchor = MediaControl.GetVolumeAnchor();
        var volume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume = volume;
        _compactFlyout.ShowVolume(_lastSnapshot.SourceName, volume?.VolumePercent, openingAnchor);
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
        if (!CanPublishMenu(_menuEnvironmentVersion) || mode is not (TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.InputDevice or TaskbarCompactFlyoutMode.Volume))
            return;

        if (_compactFlyout.IsShowing(mode))
        {
            _compactFlyout.Dismiss();
            return;
        }


        if (mode is TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.InputDevice)
        {
            var direction = mode == TaskbarCompactFlyoutMode.OutputDevice ? AudioDeviceDirection.Output : AudioDeviceDirection.Input;
            await ShowDeviceMenuAsync(direction, AudioDeviceSwitchTarget.SystemDefault,
                anchor ?? (direction == AudioDeviceDirection.Output ? MediaControl.GetOutputDeviceAnchor() : MediaControl.GetInputDeviceAnchor()));
            return;
        }

        var environmentVersion = _menuEnvironmentVersion;
        var openingAnchor = anchor ?? MediaControl.GetVolumeAnchor();
        var volume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume = volume;
        _compactFlyout.ShowVolume(_lastSnapshot.SourceName, volume?.VolumePercent, openingAnchor);
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
        var environmentVersion = _menuEnvironmentVersion;
        if (!CanPublishMenu(environmentVersion)) return;
        if (_pendingVolume is { } pending)
        {
            MediaControl.SetVolumePreview(pending);
            return;
        }

        var volume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (!CanPublishMenu(environmentVersion)) return;
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
        var environmentVersion = _menuEnvironmentVersion;
        if (!CanPublishMenu(environmentVersion)) return;
        var volume = _currentVolume ?? await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume ??= volume;
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
        if (_currentVolume is null || !CanPublishMenu(_menuEnvironmentVersion)) return;
        _pendingVolume = Math.Clamp(value, 0, 100);
        _volumeApplyTimer.Stop();
        _volumeApplyTimer.Start();
    }

    private async void MediaControl_QuickLaunchRequested(QuickLaunchEntry entry)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        MediaControl.CancelQuickLaunchInteraction();
        _quickLaunch?.Select(entry.Id);
        _quickLaunchApplyTimer.Stop();
        _pendingQuickLaunch = null;
        _compactFlyout.Dismiss();
        var environmentVersion = _menuEnvironmentVersion;
        var anchor = MediaControl.GetQuickLaunchAnchor();
        var result = await LaunchQuickLaunchEntryAsync(entry);
        if (CanPublishMenu(environmentVersion)) ShowQuickLaunchResult(result, anchor);
    }

    private async void PlayerMenu_QuickLaunchRequested(QuickLaunchEntry entry)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        _quickLaunch?.Select(entry.Id);
        var environmentVersion = _menuEnvironmentVersion;
        var anchor = _playerMenuOpeningAnchor ?? GetMediaBarScreenPhysicalBounds();
        var result = await LaunchQuickLaunchEntryAsync(entry);
        if (CanPublishMenu(environmentVersion)) ShowQuickLaunchResult(result, anchor);
    }

    private async Task<QuickLaunchResult?> LaunchQuickLaunchEntryAsync(QuickLaunchEntry entry)
    {
        // 已打开的列表可能仍持有删除前的条目；在后台命令入队前再次校验身份。
        if (!(SettingsManager.Current.QuickLaunch.Entries ?? []).Any(current => current.Id == entry.Id &&
            current.Kind == entry.Kind && current.Target == entry.Target)) return null;
        var token = MediaControl.QuickLaunchRequestToken;
        try
        {
            var result = await _sourceActivationService.LaunchAsync(entry, token);
            return token.IsCancellationRequested ? null : result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            return QuickLaunchResult.Failed;
        }
    }

    private void ShowQuickLaunchResult(QuickLaunchResult? result, TrayIconBounds? anchor = null)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion) || result is null or QuickLaunchResult.Success) return;
        _compactFlyout.ShowQuickLaunch(
            SettingsManager.Current.QuickLaunch.Entries ?? [],
            anchor ?? MediaControl.GetQuickLaunchAnchor());
        _compactFlyout.ShowQuickLaunchStatus(DescribeQuickLaunchResult(result.Value));
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
        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
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
        if (_quickLaunch?.Settings.InteractionMode == QuickLaunchInteractionMode.SelectThenLaunch)
        {
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            _quickLaunch.MoveSelection(e.Delta);
            return;
        }
        var current = _pendingQuickLaunch is null
            ? 0
            : entries.ToList().FindIndex(entry => entry.Id == _pendingQuickLaunch.Id);
        var index = DeferredCircularSelection.Move(current, e.Delta, entries.Count);
        if (index < 0) return;
        _pendingQuickLaunch = entries[index];
        _quickLaunch?.Select(_pendingQuickLaunch.Id);
        MediaControl.SetQuickLaunchPreview(_pendingQuickLaunch);
        MediaControl.CancelQuickLaunchInteraction();
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
        _spectrumSession.Interval = TimeSpan.FromMilliseconds(1000d / spectrum.RefreshRateHz);
        var performance = SettingsManager.Current.PerformanceComponent.Normalize();
        MediaControl.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
        MediaControl.ApplyTaskbarExperienceSettings();
        // 是否订阅指标采样按控件算出的性能组件显隐决定：静置层显隐的唯一判据在 TaskbarRestLayoutPolicy 里，
        // 宿主自行读一遍 PerformanceVisible 会在"无媒体时不保留性能组件"的状态下继续空转采样。
        // Whether to subscribe for metric sampling follows the performance component's visibility as the control computed it: the only authority on
        // rest-layer visibility is TaskbarRestLayoutPolicy, and reading PerformanceVisible here instead would keep sampling for nothing while the
        // performance component is not kept without media.
        SynchronizeMetricsSubscription(performance);
        SynchronizeSpectrumTimer();
    }

    private void SynchronizeSpectrumTimer()
    {
        var shouldRun = !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned &&
                        !IsTaskbarPresentationSuspended && _appliedOrientation.HasValue &&
                        MediaControl.IsSpectrumComponentVisible;
        if (shouldRun)
        {
            if (!_spectrumSession.IsEnabled)
                _spectrumSession.Start();
        }
        else
        {
            _spectrumSession.Stop();
            _spectrumSession.Clear();
        }
    }

    private void SynchronizeMetricsSubscription(PerformanceComponentSettings performance)
    {
        var metrics = performance.GetOrderedMetrics().ToArray();
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
        var metrics = settings.GetOrderedMetrics();
        if (metrics.Count == 0)
            return;
        _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, metrics.Count - 1);
        _metricSampleCount++;
        _metricCycleIndex = MetricPresentationPolicy.Advance(_metricCycleIndex, _metricSampleCount, metrics.Count);
        MediaControl.ApplyPerformanceSnapshot(snapshot, _metricCycleIndex,
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
            e.OriginalSource is DependencyObject source && IsMediaAction(source))
        {
            return;
        }

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

        if (SettingsManager.Current.TaskbarBarPositionLocked)
            return;

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
        // 普通点击保持放大；独立 Popup 的捕获和输入在拖动阈值前仍留在 Popup 子树。
        if (!MediaControl.CaptureArtworkPointer(e.OriginalSource as DependencyObject))
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
            MediaControl.SuspendQuickLaunchPresentation();
            _externalFullPanelHandle?.Dismiss(immediate: true);
            MediaControl.CollapseArtworkZoomForDrag();
            Mouse.Capture(GetActiveControl(), CaptureMode.SubTree);
        }

        var targetPrimary = Math.Clamp(
            _dragStartBarPrimary + cursorPrimary - _dragStartCursorPrimary,
            0,
            _dragPrimaryLimit);

        // Keep the chosen anchor while dragging: forcing Start would also flip automatic right-side presentation.
        // The placement and drag offset use the same physical-pixel anchor, including the full taskbar center.
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
            dragPrimarySize,
            dragIsVertical ? dragTaskbarRect.Bottom - dragTaskbarRect.Top : dragTaskbarRect.Right - dragTaskbarRect.Left,
            SettingsManager.Current.Position);
        PreparePrimaryDrag(targetPrimary, dragPrimarySize, dragRange, dragTaskbarRect, dragDpiScale);

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
        // 放大 Popup 显式转发抬起；全局抬起钩子继续兜底处理未送达的释放。
        MediaControl.ReleaseArtworkPointerCapture();
        if (MediaControl.IsMouseCaptured)
            MediaControl.ReleaseMouseCapture();

        if (_isTaskManagerClickPending)
        {
            _isTaskManagerClickPending = false;
            var suppressed = _mouseInputMonitor.ConsumeSuppressedClick();
            // 指针可能已经移到别的元素上，因此抬起时重新判定命中，而不是相信按下时的结论。
            // The pointer may have moved onto another element, so the hit is re-evaluated on release instead of trusting the
            // verdict from the press.
            if (!suppressed && SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick &&
                e.OriginalSource is DependencyObject source && MediaControl.IsPerformanceComponentClick(source))
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
        if (_isDragging || IsTaskbarPresentationSuspended || _isRestoringTaskbarPresentation || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
        {
            _pendingSizeRequest = request;
            return;
        }

        if (MediaControl.IsRestConnectionTransitionActive && orientation == LayoutOrientation.Horizontal)
        {
            StopLayoutTransition();
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
        var minimum = MediaControl.MinimumPrimaryLength;
        _lengthConstraints.Update(this, minimum, maximum);
        if (maximum <= 0)
        {
            StopLayoutTransition();
            UpdatePositionImmediately();
            return;
        }
        var motion = MotionPolicy.ResolveCurrent();

        _skipPlacementTransition = request.SkipTransition || !motion.UseContinuousMotion;
        try { UpdatePositionImmediately(); }
        finally { _skipPlacementTransition = false; }
    }

    private void PlayerMenu_OpenSettingsRequested(object? sender, EventArgs e) =>
        ViewModel.RaiseOpenSettingsRequested();

    private void PlayerMenu_ExitRequested(object? sender, EventArgs e) => Execute(ViewModel.ExitApplicationCommand);

    private void PlayerMenu_OpenUpdateSettingsRequested(object? sender, EventArgs e) =>
        ViewModel.RaiseOpenUpdateSettingsRequested();

    private void PlayerMenu_ReloadTaskbarHostRequested(object? sender, EventArgs e)
    {
        PlayerMenu.IsOpen = false;
        _hostActions.RequestTaskbarHostReload();
    }

    private double GetAvailablePrimaryLengthDip(LayoutOrientation orientation)
    {
        if (_lastTaskbarHandle == IntPtr.Zero || !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var rect)) return 0;
        var dpi = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
        if (dpi <= 0) return 0;
        var desired = _lastDesiredSizeRequest?.PrimaryLength ?? MediaControl.MinimumPrimaryLength;
        var preview = PreviewPlacement(rect, dpi, desired);
        return preview.State.IsVisible ? preview.State.Budget / dpi : 0;
    }

    private TaskbarPrimaryRange GetPreferredSafeRange(
        RECT taskbarRect, LayoutOrientation orientation, double dpiScale,
        int requiredPrimaryPixels, out bool isSafePlacement)
    {
        var preview = PreviewPlacement(taskbarRect, dpiScale,
            _lastDesiredSizeRequest?.PrimaryLength ?? MediaControl.MinimumPrimaryLength);
        isSafePlacement = preview.State.IsVisible;
        return preview.State.Range;
    }

    private void AdvanceSizeAnimation()
    {
        if (_returnTransition.IsActive) AdvanceReturnTransition();
        else StopLayoutTransition();
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
        Dispatcher.BeginInvoke(() =>
        {
            if (_isDragPending && !_isDragging) EndTaskbarDrag();
            HealStaleMediaControlCapture();
        }, DispatcherPriority.Background);
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
        if (releaseCapture)
        {
            MediaControl.ReleaseArtworkPointerCapture();
            if (MediaControl.IsMouseCaptured)
                MediaControl.ReleaseMouseCapture();
        }

        if (_pendingSizeRequest is { } request && _appliedOrientation is { } orientation)
        {
            _pendingSizeRequest = null;
            MediaControl_DesiredSizeChanged(this, new MediaBarSizeRequestEventArgs(request));
        }

        UpdatePosition();
    }

    internal bool IsPerformanceMenuRequested { get; private set; }
    internal bool IsBatteryMenuRequested { get; private set; }

    private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 阻止 WPF 把 Popup 绑定到会随 Explorer 收起的任务栏子窗口。
        e.Handled = true;
        if (!CanPublishMenu(_menuEnvironmentVersion) || IsTaskbarPresentationSuspended || _isRestoringTaskbarPresentation)
        {
            e.Handled = true;
            PlayerMenu.IsOpen = false;
            return;
        }
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

        var menuSource = e.CursorLeft < 0 && e.CursorTop < 0 ? Keyboard.FocusedElement as DependencyObject
            : MediaControl.InputHitTest(Mouse.GetPosition(MediaControl)) as DependencyObject ?? e.OriginalSource as DependencyObject;
        IsPerformanceMenuRequested = SettingsManager.Current.Experiments.MemoryTools && MediaControl.IsPerformanceComponentClick(menuSource);
        IsBatteryMenuRequested = MediaControl.IsBatteryComponentClick(menuSource);
        PlayerMenu.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
        _playerMenuOpeningAnchor = GetMediaBarScreenPhysicalBounds();
        Point? keyboardAnchor = e.CursorLeft < 0 && e.CursorTop < 0
            ? new Point(_playerMenuOpeningAnchor.Value.Left, _playerMenuOpeningAnchor.Value.Top) : null;
        PlayerMenuOpenRequested?.Invoke(this, keyboardAnchor);
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
        DisposeExternalFullPanelHandle();
        PlayerMenu.ExitRequested -= PlayerMenu_ExitRequested;
        StopOccupancyRefresh();
        _memoryPruneCoordinator.LevelChanged -= MemoryPruneCoordinator_LevelChanged;
        _timer.Stop();
        StopLayoutTransition();
        _spectrumSession.Dispose();
        _taskbarMotionSettleTimer.Stop();
        _taskbarHiddenTrimTimer.Stop();
        UnregisterTaskbarLocationHook();
        _lengthConstraints.Remove(this);
        DisposeMetricsSubscription();
        MediaControl.CancelQuickLaunchInteraction();
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
        MediaControl.InputDeviceMenuRequested -= MediaControl_InputDeviceMenuRequested;
        MediaControl.InputDeviceWheelRequested -= MediaControl_InputDeviceWheelRequested;
        MediaControl.InputDeviceInfoRequested -= MediaControl_InputDeviceInfoRequested;
        _audioInteractionService.DeviceSwitches.Updated -= OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated -= OnAudioDeviceInvalidated;
        _audioInteractionService.CancelDeviceOwner(this);
        MediaControl.OutputDeviceWheelRequested -= MediaControl_OutputDeviceWheelRequested;
        MediaControl.VolumeMenuRequested -= MediaControl_VolumeMenuRequested;
        MediaControl.VolumeWheelRequested -= MediaControl_VolumeWheelRequested;
        MediaControl.QuickLaunchMenuRequested -= MediaControl_QuickLaunchMenuRequested;
        MediaControl.QuickLaunchWheelRequested -= MediaControl_QuickLaunchWheelRequested;
        PlayerMenu.QuickLaunchRequested -= PlayerMenu_QuickLaunchRequested;
        MediaControl.OpenTaskManagerRequested -= MediaControl_OpenTaskManagerRequested;
        MediaControl.InformationAutomaticallyHidden -= MediaControl_InformationAutomaticallyHidden;
        InformationAutomaticallyHidden = null;
        MediaControl.WheelRequested -= MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged -= MediaControl_DesiredSizeChanged;
        MediaControl.RestTransitionFinished -= MediaControl_RestTransitionFinished;
        MediaControl.ArtworkZoomExtensionChanged -= MediaControl_ArtworkZoomExtensionChanged;
        MediaControl.ArtworkPointerMoved -= MediaControl_PreviewMouseMove;
        MediaControl.ArtworkPointerReleased -= MediaControl_PreviewMouseLeftButtonUp;
        PreviewMouseMove -= TaskbarWindow_PreviewMouseMove;
        _mouseInputMonitor.LeftButtonReleased -= MouseInputMonitor_OnLeftButtonReleased;
        MediaControl.OutputDeviceInfoRequested -= MediaControl_OutputDeviceInfoRequested;
        MediaControl.VolumeInfoRequested -= MediaControl_VolumeInfoRequested;
        _compactFlyout.Dispose();
        ClosePlayerMenu();
        PlayerMenuOpenRequested = null;
        MediaControl.DisposePresentation();
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
