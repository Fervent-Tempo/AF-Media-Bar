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
    private MediaBarSizeRequest? _pendingSizeRequest;
    private MediaBarSizeRequest? _lastDesiredSizeRequest;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly NativeMouseInputMonitor _mouseInputMonitor;
    private readonly AdaptiveForegroundSamplingSession _foregroundSamplingSession;
    private readonly float[] _spectrumBands = new float[SpectrumComponentSettings.MaximumBandCount];
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
        MediaControl.OutputDeviceInfoRequested += MediaControl_OutputDeviceInfoRequested;
        MediaControl.VolumeInfoRequested += MediaControl_VolumeInfoRequested;

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
        _spectrumTimer.Tick += (_, _) =>
        {
            var bandCount = SettingsManager.Current.SpectrumComponent.Normalize().BandCount;
            // 采集的是"当前输出设备的全部声音"（WASAPI 回环），因此判据里没有"前台 SMTC 会话是否在播"这一项：
            // 游戏、没有 SMTC 的播放器、浏览器里没有元数据的页面都会让频谱动起来，只要频谱组件当前可见。
            // 组件不可见时连采集都不做——静置层显隐的唯一判据在 TaskbarRestLayoutPolicy 里，宿主只读控件算好的结论。
            // What is captured is everything the current output device plays (WASAPI loopback), so the condition has no "is the foreground SMTC
            // session playing" term: a game, a player without SMTC, or a browser page without metadata all move the spectrum, as long as the
            // spectrum component is visible right now. While it is not visible nothing is captured at all — the only authority on rest-layer
            // visibility is TaskbarRestLayoutPolicy, and the host merely reads the verdict the control computed.
            if (!_isClosing && !IsTaskbarPresentationSuspended && _appliedOrientation == LayoutOrientation.Horizontal &&
                MediaControl.IsSpectrumComponentVisible &&
                _audioMonitorService.GetSpectrum(_spectrumBands, bandCount))
            {
                _spectrumActive = true;
                MediaControl.ApplySpectrum(_spectrumBands.AsSpan(0, bandCount));
                return;
            }

            if (_spectrumActive)
            {
                Array.Clear(_spectrumBands, 0, _spectrumBands.Length);
                MediaControl.ApplySpectrum(_spectrumBands.AsSpan(0, bandCount));
                _spectrumActive = false;
            }
        };
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

    private void UpdatePosition()
    {
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
            _taskBarService.ApplyInputRegion(taskbarWindowHandle, [barRect]);
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
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            _compactFlyout.Dismiss();
        }

        if (!_timer.IsEnabled && !IsTaskbarPresentationSuspended)
            _timer.Start();

        // Delegate UI update to the original media control and its taskbar-only overlay.
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
        Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Background);
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
            MediaControl.ApplyLayout(windowMode, orientation, lengthScalePercent, thicknessScalePercent);
            MediaControl.ApplyAppearanceSettings();
            MediaControl.ApplyTaskbarExperienceSettings();
            _appliedWindowMode = windowMode;
            _appliedOrientation = orientation;
            _appliedLengthScalePercent = lengthScalePercent;
            _appliedThicknessScalePercent = thicknessScalePercent;
            _appliedMediaFontSizePercent = mediaFontSizePercent;
            MediaControl.RefreshDesiredSize();
            // 横/竖布局会改变静置层性能组件是否存在；没有新媒体快照时也必须同步租约。
            // Horizontal/vertical layout changes can add or remove the rest-layer performance component, even when no new media snapshot follows.
            SynchronizeMetricsSubscription(SettingsManager.Current.PerformanceComponent.Normalize());
        }

        if (orientationChanged && IsLoaded)
        {
            Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Loaded);
        }

        // 窗口模式切换由 MainWindow 负责重新创建任务栏或灵动岛宿主。
        // MainWindow recreates the taskbar or dynamic-island host when the window mode changes.
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
        // 静置层设置里包含媒体文字字号，必须重新应用布局才能把新字号写进文本块；
        // 布局状态未变化时 ApplyLayoutSettings 不会做任何工作。
        // Rest-layer settings include the media font size, so the layout has to be re-applied before the text update;
        // ApplyLayoutSettings does nothing when the layout state is unchanged.
        ApplyLayoutSettings(SettingsManager.Current.WindowMode, SettingsManager.Current.LayoutOrientationMode);
        ApplyExtraFeaturesSettings();
        MediaControl.UpdateSongInfo(_lastSnapshot);
        // 改设置就可能改变静置层还剩几个组件，因此"完全隐藏"的结论必须跟着重算一次：
        // 只在快照变化时同步会让"把无媒体保留组件全部取消"这一步要等下一首歌才生效。
        // A settings change can change how many components the rest layer keeps, so the "hide completely" verdict has to be recomputed
        // here: synchronizing it on snapshot changes alone would delay "keep nothing while idle" until the next track.
        ApplyMediaBarVisibility();
        Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Background);
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

    private void ShowQuickLaunchResult(QuickLaunchResult result)
    {
        if (_isClosing || result == QuickLaunchResult.Success) return;
        _compactFlyout.ShowQuickLaunch(
            SettingsManager.Current.QuickLaunch.Entries ?? [],
            MediaControl.GetQuickLaunchAnchor());
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

            return;
        }

        if (!_isDragging && !_isDragPending)
            return;

        EndTaskbarDrag();
        e.Handled = true;
    }

    private void MediaControl_DesiredSizeChanged(object? sender, MediaBarSizeRequestEventArgs eventArgs)
    {
        var request = eventArgs.Request;
        if (_isClosing || _appliedOrientation is not { } orientation)
            return;

        _lastDesiredSizeRequest = request;
        if (_isDragging || IsTaskbarPresentationSuspended || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc)
        {
            _pendingSizeRequest = request;
            return;
        }

        ApplyDesiredSizeRequest(request, orientation);
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
        if (request.SkipTransition || !motion.UseContinuousMotion || Math.Abs(target - current) < LayoutSizeCalculator.MinimumChangeDip)
        {
            ApplyPrimaryLength(target);
            UpdatePosition();
            return;
        }

        _sizeAnimationStart = current;
        _sizeAnimationTarget = target;
        _sizeAnimationProgress = 0;
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

        // 这里问的是"这个方向最多能有多长"，因此不做"放得下"判断（0 表示按位置偏好取区间）。
        // This asks how long the bar may become in this orientation, so no fitting test is applied (0 keeps the plain preference).
        var range = GetPreferredSafeRange(rect, orientation, dpi, requiredPrimaryPixels: 0, out _);
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

        // UIA 偶尔会漏掉一组图标，表现为安全区间突然扩大。旧区间仍被新结果完整包含时保持原位；
        // 真正有新图标侵入旧区间时包含关系会失效，下面会立即选择新位置。
        // UIA can transiently omit an icon group, making a safe range suddenly expand. Keep the old placement while the new result still
        // fully contains it; when icons genuinely invade that range containment fails and a new position is selected immediately below.
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
            isSafePlacement = true;
            return keptRange;
        }

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

        var frame = MediaBarSizeAnimationCalculator.Advance(
            _sizeAnimationStart,
            _sizeAnimationTarget,
            _sizeAnimationProgress,
            elapsedMilliseconds: 16,
            durationMilliseconds: MotionPolicy.ResolveCurrent().PositionDuration.TotalMilliseconds);
        _sizeAnimationProgress = frame.Progress;
        ApplyPrimaryLength(frame.Value);
        UpdatePosition();
        if (frame.IsCompleted)
        {
            ApplyPrimaryLength(_sizeAnimationTarget);
            UpdatePosition();
            _sizeAnimationTimer.Stop();
        }
    }

    private void MediaControl_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            EndTaskbarDrag(releaseCapture: false);
    }

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
        }
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
        MediaControl.OpenTaskManagerRequested -= MediaControl_OpenTaskManagerRequested;
        MediaControl.WheelRequested -= MediaControl_WheelRequested;
        MediaControl.DesiredSizeChanged -= MediaControl_DesiredSizeChanged;
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