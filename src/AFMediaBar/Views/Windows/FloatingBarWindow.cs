// 独立桌面工具窗口；不附着或重新托管任务栏 HWND。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Windows;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>拥有单个显示器上的浮窗、输入、弹层和呈现租约，不拥有共享业务服务。</summary>
public sealed partial class FloatingBarWindow : Window, IBarRuntimeHost
{
    private readonly SpectrumPresentationSession _spectrumSession;
    private readonly TaskBarMediaControl _mediaControl = new();
    private readonly TaskbarWindowViewModel _commands;
    private readonly GlobalInteractionRouter _interactionRouter;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly AudioMonitorService _audioMonitor;
    private readonly MediaSourceActivationService _sourceActivationService;
    private readonly SystemMetricsMonitorService _metricsMonitor;
    private readonly NativeMouseInputMonitor _mouse;
    private readonly MemoryPruneCoordinator _prune;
    private readonly DesktopWidgetZOrderService _zOrder;
    private readonly AppLogService _log;
    private readonly FloatingBarLengthConstraints _lengthConstraints;
    private readonly AdaptiveForegroundSamplingSession _foregroundSamplingSession;
    private readonly TaskbarCompactFlyoutWindow _compactFlyout;
    private readonly DispatcherTimer _quickLaunchApplyTimer = new();
    private readonly DispatcherTimer _volumeApplyTimer = new();
    private readonly DispatcherTimer _dragTimer = new(DispatcherPriority.Input);
    private readonly DispatcherTimer _sizeTimer = new();
    private readonly DispatcherTimer _backgroundTimer = new();
    private IDisposable? _zRegistration;
    private IDisposable? _metricsSubscription;
    private int _metricsSubscriptionGeneration;
    private IReadOnlyList<MetricKind> _subscribedMetricKinds = [];
    private TimeSpan? _subscribedMetricInterval;
    private int _metricCycleIndex;
    private int _metricSampleCount;
    private CancellationTokenSource _requests = new();
    private int _menuEnvironmentVersion;
    private bool _isClosing;
    private bool _windowClosed;
    private bool _suspended = true;
    private bool _applyingSettings;
    private bool _savingPosition;
    private bool _taskManagerClickPending;
    private bool _dragPending;
    private bool _dragging;
    private Point _dragStart;
    private Point _dragCenter;
    private DateTime _suppressClickUntil;
    private Point _centerPx;
    private FloatingBarPosition? _savedPosition;
    private LayoutOrientation _orientation;
    private FloatingBarLayer _layer;
    private double _primary = 240;
    private double _targetPrimary = 240;
    private double _animationStart;
    private long _animationTick;
    private double _animationProgress;
    private double _cross = 44;
    private Size? _layoutSizeDip;
    private double _layoutDpi;
    private Int32Rect? _nativeBoundsPx;
    private nint _handle;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private AudioDeviceTarget? _deviceMenuTarget;
    private QuickLaunchEntry? _pendingQuickLaunch;
    private ApplicationVolumeSnapshot? _currentVolume;
    private int? _pendingVolume;
    private AppSettings PresentationSettings { get; set; }
    private bool IsBackgroundPruned => _prune.CurrentLevel >= MemoryPruneLevel.DisplayOff;
    private bool IsOperational => !_isClosing && !_suspended && !IsBackgroundPruned && SettingsManager.Current.TaskbarBarEnabled && !_mediaControl.ShouldHideTaskbarWindow;
    private double Dpi => Math.Max(1, Monitor.DpiX) / 96d;
    private GlobalInteractionSettings EffectiveInteraction => ExperimentalAudioPolicy.Interaction(
        PresentationSettings.Interaction, SettingsManager.Current.Experiments.NewAudioControl);

    public string TargetMonitorDeviceId { get; }
    public DisplayMonitorInfo Monitor { get; }
    public bool IsVerticalPresentation => _orientation == LayoutOrientation.Vertical;
    public AppSettings EffectiveSettings => PresentationSettings;
    public event EventHandler? OpenFullPanelRequested;
    public event EventHandler? PlacementTransitionStarted;
    public event Action<FloatingBarWindow, Point?>? PlayerMenuOpenRequested;
    internal bool IsPerformanceMenuRequested { get; private set; }

    public FloatingBarWindow(string targetMonitorDeviceId, DisplayMonitorInfo monitor,
        TaskbarWindowViewModel commands, GlobalInteractionRouter interactionRouter,
        AudioInteractionService audioInteractionService, AudioMonitorService audioMonitor,
        MediaSourceActivationService sourceActivationService, SystemMetricsMonitorService metricsMonitor,
        NativeMouseInputMonitor mouse, MemoryPruneCoordinator prune, DesktopWidgetZOrderService zOrder,
        WindowAppearanceService appearance, ScreenBackgroundSampler sampler, AppLogService log, FloatingBarLengthConstraints lengthConstraints)
    {
        TargetMonitorDeviceId = targetMonitorDeviceId;
        Monitor = monitor;
        _commands = commands;
        _interactionRouter = interactionRouter;
        _audioInteractionService = audioInteractionService;
        _audioMonitor = audioMonitor;
        _sourceActivationService = sourceActivationService;
        _metricsMonitor = metricsMonitor;
        _mouse = mouse;
        _prune = prune;
        _zOrder = zOrder;
        _log = log;
        _lengthConstraints = lengthConstraints;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowHelper.SetNoActivate(this);
        Content = _mediaControl;
        PresentationSettings = ProjectSettings();
        _centerPx = new Point(monitor.WorkArea.Left + monitor.WorkArea.Width / 2,
            monitor.WorkArea.Top + monitor.WorkArea.Height / 2);
        _mediaControl.AllowRestConnectionTransition = false;
        _mediaControl.SuppressedClickSource = () => DateTime.UtcNow < _suppressClickUntil || _mouse.ConsumeSuppressedClick();
        _compactFlyout = new TaskbarCompactFlyoutWindow(appearance);
        ConnectInput();
        _foregroundSamplingSession = new AdaptiveForegroundSamplingSession(sampler, Dispatcher,
            () => IsOperational && !_dragging && PresentationSettings.Appearance.PlayerForegroundMode == PlayerForegroundMode.Automatic &&
                _mediaControl.TryGetForegroundSampleBounds(out var bounds) ? bounds : null,
            _mediaControl.ApplyAdaptiveForegroundDecision);
        _spectrumSession = new SpectrumPresentationSession(audioMonitor, Dispatcher,
            () => PresentationSettings.SpectrumComponent,
            () => IsOperational && _mediaControl.IsSpectrumComponentVisible,
            (bands, count) => _mediaControl.ApplySpectrum(bands.AsSpan(0, count)),
            exception => _log.Warn("FloatingBar", $"频谱采样失败: {exception.Message}"));
        _backgroundTimer.Interval = TimeSpan.FromSeconds(2);
        _backgroundTimer.Tick += (_, _) => _foregroundSamplingSession.RequestRefresh();
        _sizeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _sizeTimer.Tick += (_, _) => AdvanceSize();
        _dragTimer.Interval = TimeSpan.FromMilliseconds(16);
        _dragTimer.Tick += (_, _) => ObserveDrag();
        _quickLaunchApplyTimer.Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds);
        _quickLaunchApplyTimer.Tick += async (_, _) =>
        {
            _quickLaunchApplyTimer.Stop();
            if (_pendingQuickLaunch is not { } pending || !IsOperational || _lastSnapshot.IsConnected) return;
            _pendingQuickLaunch = null;
            await LaunchAsync(pending);
        };
        _volumeApplyTimer.Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds);
        _volumeApplyTimer.Tick += async (_, _) =>
        {
            _volumeApplyTimer.Stop();
            if (_pendingVolume is not { } value || _currentVolume is not { } volume || !IsOperational) return;
            _pendingVolume = null;
            var token = _requests.Token;
            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return _audioInteractionService.SetApplicationVolume(volume.ProcessName, value);
                }, token);
                if (!token.IsCancellationRequested && IsOperational) _currentVolume = volume with { VolumePercent = value, IsMuted = false };
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.Error("FloatingBar", "应用音量失败", ex); }
        };
        SettingsManager.SettingsChanged += SettingsChanged;
        _prune.LevelChanged += PruneChanged;
        _mouse.LeftButtonReleased += LeftReleased;
        _audioInteractionService.DeviceSwitches.Updated += OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated += OnAudioDeviceInvalidated;
        Closed += (_, _) => { _windowClosed = true; Dispose(); };
        ApplySettings();
    }

    private static AppSettings ProjectSettings() =>
        (SettingsManager.Current.FloatingBar.Profile ?? BarDisplaySettings.Capture(SettingsManager.Current)).ApplyTo(SettingsManager.Current);

    private void ConnectInput()
    {
        _mediaControl.TogglePlayPauseRequested += (_, _) => Execute(_commands.TogglePlayPauseCommand);
        _mediaControl.SkipPreviousRequested += (_, _) => Execute(_commands.SkipPreviousCommand);
        _mediaControl.SkipNextRequested += (_, _) => Execute(_commands.SkipNextCommand);
        _mediaControl.ActivateSourceRequested += (_, _) => Execute(_commands.ActivateMediaSourceCommand);
        _mediaControl.OpenFullPanelRequested += (_, _) => { if (IsOperational) OpenFullPanelRequested?.Invoke(this, EventArgs.Empty); };
        _mediaControl.OutputDeviceMenuRequested += MediaControl_OutputDeviceMenuRequested;
        _mediaControl.InputDeviceMenuRequested += MediaControl_InputDeviceMenuRequested;
        _mediaControl.InputDeviceWheelRequested += MediaControl_InputDeviceWheelRequested;
        _mediaControl.InputDeviceInfoRequested += MediaControl_InputDeviceInfoRequested;
        _mediaControl.OutputDeviceWheelRequested += MediaControl_OutputDeviceWheelRequested;
        _mediaControl.OutputDeviceInfoRequested += MediaControl_OutputDeviceInfoRequested;
        _mediaControl.VolumeMenuRequested += MediaControl_VolumeMenuRequested;
        _mediaControl.VolumeWheelRequested += MediaControl_VolumeWheelRequested;
        _mediaControl.VolumeInfoRequested += MediaControl_VolumeInfoRequested;
        _mediaControl.QuickLaunchMenuRequested += MediaControl_QuickLaunchMenuRequested;
        _mediaControl.QuickLaunchWheelRequested += MediaControl_QuickLaunchWheelRequested;
        _mediaControl.OpenTaskManagerRequested += MediaControl_OpenTaskManagerRequested;
        _mediaControl.WheelRequested += WheelRequested;
        _mediaControl.DesiredSizeChanged += (_, args) => ResizeContent(args.Request);
        _compactFlyout.QuickLaunchSelected += MediaControl_QuickLaunchRequested;
        _compactFlyout.OutputDeviceSelected += MediaControl_OutputDeviceSelected;
        _compactFlyout.OutputDeviceWheelRequested += CompactFlyout_OutputDeviceWheelRequested;
        _compactFlyout.VolumeWheelRequested += CompactFlyout_VolumeWheelRequested;
        _compactFlyout.VolumeValueRequested += MediaControl_VolumeValueRequested;
        PreviewMouseLeftButtonDown += StartDrag;
        PreviewMouseMove += (_, _) => ObserveDrag();
        LostMouseCapture += (_, _) => { if (_dragging && Mouse.Captured != this) EndDrag(); };
        _mediaControl.ArtworkPointerPressed += StartDrag;
        _mediaControl.ArtworkPointerMoved += ArtworkPointerMoved;
        _mediaControl.ArtworkPointerReleased += PointerReleased;
        PreviewMouseLeftButtonUp += PointerReleased;
        PreviewMouseRightButtonUp += (_, args) =>
        {
            if (!IsOperational || DateTime.UtcNow < _suppressClickUntil || _mouse.ConsumeSuppressedClick()) return;
            IsPerformanceMenuRequested = _mediaControl.IsPerformanceComponentClick(args.OriginalSource as DependencyObject);
            PlayerMenuOpenRequested?.Invoke(this, null);
            args.Handled = true;
        };
    }

    private void ArtworkPointerMoved(object sender, MouseEventArgs args) => ObserveDrag();

    private void PointerReleased(object sender, MouseButtonEventArgs args)
    {
        var pending = _taskManagerClickPending;
        _taskManagerClickPending = false;
        EndDrag();
        if (pending && IsOperational && DateTime.UtcNow >= _suppressClickUntil &&
            _mediaControl.IsPerformanceComponentClick(args.OriginalSource as DependencyObject))
            _mediaControl.RequestOpenTaskManager();
    }

    private static void Execute(ICommand command) { if (command.CanExecute(null)) command.Execute(null); }

    private async void WheelRequested(object? sender, PlayerSurfaceWheelEventArgs args)
    {
        if (!IsOperational) return;
        if (args.IsLeftButtonDown) EndDrag();
        if (args.IsRightButtonDown) _suppressClickUntil = DateTime.UtcNow.AddMilliseconds(450);
        var version = _menuEnvironmentVersion;
        try
        {
            var result = await _interactionRouter.ExecuteWheelAsync(args.Delta, args.IsShiftDown,
                args.IsLeftButtonDown, args.IsRightButtonDown, EffectiveInteraction, this, _requests.Token);
            if (CanPublishMenu(version) && result is { } value) _mediaControl.SetWheelResult(value);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error("FloatingBar", "滚轮操作失败", ex); }
    }

    private async Task LaunchAsync(QuickLaunchEntry entry)
    {
        var version = _menuEnvironmentVersion;
        try { var result = await _sourceActivationService.LaunchAsync(entry); if (CanPublishMenu(version)) ShowQuickLaunchResult(result); }
        catch (Exception ex) { _log.Error("FloatingBar", "快速启动失败", ex); }
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_isClosing || _savingPosition) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SettingsChanged(sender, args)); return; }
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_isClosing || _applyingSettings) return;
        _applyingSettings = true;
        try
        {
            var settings = SettingsManager.Current.FloatingBar;
            var saved = settings.Positions.FirstOrDefault(p => string.Equals(p.MonitorDeviceId, TargetMonitorDeviceId, StringComparison.OrdinalIgnoreCase));
            if (saved != _savedPosition && saved is not null && string.Equals(TargetMonitorDeviceId, Monitor.DeviceId, StringComparison.OrdinalIgnoreCase))
                _centerPx = new Point(Monitor.WorkArea.Left + saved.CenterXDip * Dpi, Monitor.WorkArea.Top + saved.CenterYDip * Dpi);
            if (_savedPosition is not null && saved is null)
                _centerPx = new Point(Monitor.WorkArea.Left + Monitor.WorkArea.Width / 2, Monitor.WorkArea.Top + Monitor.WorkArea.Height / 2);
            _savedPosition = saved;
            var environmentChanged = settings.Orientation != _orientation || settings.Layer != _layer ||
                PresentationSettings.Experiments.NewAudioControl != SettingsManager.Current.Experiments.NewAudioControl;
            if (environmentChanged) ClearInteraction();
            _orientation = settings.Orientation;
            _layer = settings.Layer;
            PresentationSettings = ProjectSettings();
            _mediaControl.ApplyPresentationSettings(PresentationSettings);
            _cross = (_orientation == LayoutOrientation.Vertical ? 48 : 44) * PresentationSettings.LayoutThicknessScalePercent / 100;
            _mediaControl.ApplyLayout(WindowMode.FloatingBar, _orientation,
                PresentationSettings.LayoutLengthScalePercent, PresentationSettings.LayoutThicknessScalePercent, _cross);
            _mediaControl.ApplyAppearanceSettings();
            _mediaControl.ApplyTaskbarExperienceSettings();
            _mediaControl.ApplyQuickLaunchEntries(PresentationSettings.QuickLaunch.Entries ?? []);
            _mediaControl.UpdateSongInfo(_lastSnapshot);
            _mediaControl.RefreshDesiredSize();
            if (environmentChanged) RegisterZOrder();
            if (PresentationSettings.TaskbarBarPositionLocked) EndDrag();
            SynchronizeResources();
            ApplyBounds(refreshLayout: true);
        }
        finally { _applyingSettings = false; }
    }

    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (_isClosing) return;
        var wasConnected = _lastSnapshot.IsConnected;
        _lastSnapshot = snapshot;
        if (_suspended) return;
        if (snapshot.IsConnected && !wasConnected)
        {
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch)) _compactFlyout.Dismiss();
        }
        _mediaControl.UpdateSongInfo(snapshot);
        SynchronizeResources();
        ApplyVisibility();
    }

    private void ResizeContent(MediaBarSizeRequest request)
    {
        if (_isClosing || request.Orientation != _orientation) return;
        var max = (_orientation == LayoutOrientation.Vertical ? Monitor.WorkArea.Height : Monitor.WorkArea.Width) / Dpi;
        var next = Math.Clamp(request.PrimaryLength, 1, Math.Max(1, max));
        if (Math.Abs(next - _targetPrimary) < .1) return;
        _targetPrimary = next;
        var motion = MotionPolicy.ResolveCurrent();
        if (_suspended || _applyingSettings || request.SkipTransition || !motion.UseTransitions)
        {
            _sizeTimer.Stop();
            _primary = next;
            ApplyBounds();
            return;
        }
        _sizeTimer.Interval = TimeSpan.FromMilliseconds(motion.Mode == MotionMode.Full ? 16 : 33);
        _animationStart = _primary;
        _animationProgress = 0;
        _animationTick = Stopwatch.GetTimestamp();
        _sizeTimer.Start();
    }

    private void AdvanceSize()
    {
        var tick = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_animationTick, tick).TotalMilliseconds;
        _animationTick = tick;
        var frame = MediaBarSizeAnimationCalculator.Advance(_animationStart, _targetPrimary, _animationProgress, elapsed, MotionPolicy.ResolveCurrent().PositionDuration.TotalMilliseconds);
        _animationProgress = frame.Progress;
        _primary = frame.Value;
        ApplyBounds();
        if (frame.IsCompleted) _sizeTimer.Stop();
    }

    private void ApplyBounds(bool refreshLayout = false)
    {
        if (_isClosing) return;
        var requested = _orientation == LayoutOrientation.Vertical ? new Size(_cross, _primary) : new Size(_primary, _cross);
        var bounds = FloatingBarGeometryPolicy.Resolve(new Size(Monitor.WorkArea.Width / Dpi, Monitor.WorkArea.Height / Dpi),
            requested, new Point((_centerPx.X - Monitor.WorkArea.Left) / Dpi, (_centerPx.Y - Monitor.WorkArea.Top) / Dpi));
        // 纯平移不重排内容或发布长度预算；实际尺寸、DPI 或配置变化才更新布局。
        var size = bounds.Size;
        if (refreshLayout || _layoutSizeDip != size || _layoutDpi != Dpi)
        {
            _layoutSizeDip = size;
            _layoutDpi = Dpi;
            Width = bounds.Width;
            Height = bounds.Height;
            _mediaControl.ApplyPrimaryLength(_orientation == LayoutOrientation.Vertical ? bounds.Height : bounds.Width);
            if (!_suspended) _lengthConstraints.Range.Update(this, _mediaControl.GetMinimumPrimaryLength(Dpi),
                (_orientation == LayoutOrientation.Vertical ? Monitor.WorkArea.Height : Monitor.WorkArea.Width) / Dpi);
        }
        if (_orientation == LayoutOrientation.Vertical)
            _mediaControl.ApplyVerticalTaskbarSide(_centerPx.X < Monitor.WorkArea.Left + Monitor.WorkArea.Width / 2);
        var physical = new Int32Rect((int)Math.Round(Monitor.WorkArea.Left + bounds.Left * Dpi),
            (int)Math.Round(Monitor.WorkArea.Top + bounds.Top * Dpi), (int)Math.Ceiling(bounds.Width * Dpi), (int)Math.Ceiling(bounds.Height * Dpi));
        if (_handle != nint.Zero && _nativeBoundsPx != physical)
        {
            var flags = SWP_NOACTIVATE | SWP_NOZORDER;
            if (_nativeBoundsPx is { } previous && previous.Width == physical.Width && previous.Height == physical.Height)
                flags |= SWP_NOSIZE;
            if (SetWindowPos(_handle, 0, physical.X, physical.Y, physical.Width, physical.Height, flags))
                _nativeBoundsPx = physical;
        }
        if (!_dragging) _foregroundSamplingSession?.RequestRefresh();
    }

    private Rect GetPhysicalBounds()
    {
        var origin = _mediaControl.PointToScreen(new Point());
        return new Rect(origin, new Size(ActualWidth * Dpi, ActualHeight * Dpi));
    }

    public Rect GetMediaBarScreenBounds()
    {
        var origin = _mediaControl.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(_mediaControl);
        return new Rect(origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY, _mediaControl.ActualWidth, _mediaControl.ActualHeight);
    }

    internal Point CenterPx => _centerPx;
    internal void RestoreCenter(Point center) { _centerPx = center; }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _nativeBoundsPx = null;
        if (!_isClosing) Dispatcher.BeginInvoke(() => { if (!_isClosing) ApplyBounds(refreshLayout: true); }, DispatcherPriority.Loaded);
    }

    private void StartDrag(object sender, MouseButtonEventArgs args)
    {
        if (!IsOperational) return;
        if (PresentationSettings.PerformanceComponent.OpenTaskManagerOnClick &&
            _mediaControl.IsPerformanceComponentClick(args.OriginalSource as DependencyObject))
        { _taskManagerClickPending = true; return; }
        if (PresentationSettings.TaskbarBarPositionLocked) return;
        for (var item = args.OriginalSource as DependencyObject; item is not null; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if (item is ButtonBase or Slider or ComboBox || item is FrameworkElement { Tag: "MediaAction" }) return;
        if (!GetCursorPos(out var cursor)) return;
        _dragStart = new Point(cursor.X, cursor.Y);
        var bounds = GetPhysicalBounds();
        _dragCenter = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        _dragPending = true;
        _dragTimer.Start();
    }

    private void ObserveDrag()
    {
        if (!_dragPending) return;
        if (!IsOperational || Mouse.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        if (!GetCursorPos(out var cursor)) return;
        var delta = new Vector(cursor.X - _dragStart.X, cursor.Y - _dragStart.Y);
        if (!_dragging && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * Dpi &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * Dpi) return;
        if (!_dragging)
        {
            _dragging = true;
            _mediaControl.ApplyPlacementMotionSuspension(true);
            ClearInteraction();
            PlacementTransitionStarted?.Invoke(this, EventArgs.Empty);
            Mouse.Capture(this, CaptureMode.SubTree);
        }
        ApplyDragPosition(cursor.X, cursor.Y);
    }

    private void ApplyDragPosition(int x, int y)
    {
        _centerPx = _dragCenter + new Vector(x - _dragStart.X, y - _dragStart.Y);
        ApplyBounds();
    }

    private void LeftReleased(object? sender, NativeMouseButtonEventArgs args)
    {
        if (!_isClosing) Dispatcher.BeginInvoke(EndDrag);
    }

    private void EndDrag()
    {
        _dragTimer.Stop();
        _dragPending = false;
        if (!_dragging) return;
        if (!_isClosing && !_suspended && GetCursorPos(out var cursor)) ApplyDragPosition(cursor.X, cursor.Y);
        _dragging = false;
        _mediaControl.ApplyPlacementMotionSuspension(false);
        if (Mouse.Captured == this) Mouse.Capture(null);
        _suppressClickUntil = DateTime.UtcNow.AddMilliseconds(450);
        if (!_isClosing && !_suspended && string.Equals(TargetMonitorDeviceId, Monitor.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            var bounds = _nativeBoundsPx is { } physical
                ? new Rect(physical.X, physical.Y, physical.Width, physical.Height) : GetPhysicalBounds();
            _centerPx = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            _savedPosition = new FloatingBarPosition(TargetMonitorDeviceId,
                (_centerPx.X - Monitor.WorkArea.Left) / Dpi, (_centerPx.Y - Monitor.WorkArea.Top) / Dpi);
            _savingPosition = true;
            try
            {
                var settings = SettingsManager.Current.FloatingBar;
                SettingsManager.Current.FloatingBar = settings with
                {
                    Positions = settings.Positions.Where(p => !string.Equals(p.MonitorDeviceId, TargetMonitorDeviceId, StringComparison.OrdinalIgnoreCase))
                        .Append(_savedPosition).ToArray()
                };
            }
            finally { _savingPosition = false; }
        }
        if (IsOperational) _foregroundSamplingSession.RequestRefresh();
    }

    private void ClearInteraction()
    {
        _taskManagerClickPending = false;
        _menuEnvironmentVersion++;
        _spectrumSession.Invalidate();
        _requests.Cancel();
        _requests.Dispose();
        _requests = new CancellationTokenSource();
        _audioInteractionService.CancelDeviceOwner(this);
        _compactFlyout.Dismiss();
        _quickLaunchApplyTimer.Stop();
        _volumeApplyTimer.Stop();
        _pendingQuickLaunch = null;
        _pendingVolume = null;
        _currentVolume = null;
        _deviceMenuTarget = null;
        _mediaControl.ApplyHostVisibilitySuspension(true);
        if (IsOperational) _mediaControl.ApplyHostVisibilitySuspension(false);
        _foregroundSamplingSession?.Invalidate(clearDecision: false);
    }

    private void SynchronizeResources()
    {
        SynchronizeMetricsSubscription(PresentationSettings.PerformanceComponent.Normalize());
        _spectrumSession.Interval = TimeSpan.FromMilliseconds(1000d / PresentationSettings.SpectrumComponent.Normalize().RefreshRateHz);
        if (IsOperational && _mediaControl.IsSpectrumComponentVisible) _spectrumSession.Start(); else _spectrumSession.Stop();
    }

    private void PruneChanged(object? sender, MemoryPruneLevelChangedEventArgs args)
    {
        if (_isClosing) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => PruneChanged(sender, args)); return; }
        _mediaControl.ApplyBackgroundPruneLevel(args.Level);
        if (IsBackgroundPruned) { ClearInteraction(); _backgroundTimer.Stop(); _sizeTimer.Stop(); }
        else if (!_suspended) { _mediaControl.ApplyHostVisibilitySuspension(false); _backgroundTimer.Start(); ApplySnapshot(_lastSnapshot); }
        SynchronizeResources();
    }

    private void RegisterZOrder()
    {
        _zRegistration?.Dispose();
        _zRegistration = null;
        if (_handle != nint.Zero && !_suspended && !_isClosing)
            _zRegistration = _zOrder.Attach(_handle, _layer, () => !_isClosing && !_suspended &&
                !_mediaControl.ShouldHideTaskbarWindow && SettingsManager.Current.TaskbarBarEnabled);
    }

    private void ApplyVisibility()
    {
        if (_suspended || _isClosing) return;
        var visible = SettingsManager.Current.TaskbarBarEnabled && !_mediaControl.ShouldHideTaskbarWindow;
        _mediaControl.ApplyHostVisibilitySuspension(!visible || IsBackgroundPruned);
        if (visible) { if (!IsVisible) Show(); } else { ClearInteraction(); Hide(); }
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _handle = new WindowInteropHelper(this).EnsureHandle();
        if (_handle == nint.Zero) throw new InvalidOperationException("Floating window creation failed.");
        _suspended = false;
        Show();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isClosing) throw new ObjectDisposedException(nameof(FloatingBarWindow));
        ApplySettings();
        ApplySnapshot(_lastSnapshot);
        RegisterZOrder();
        _backgroundTimer.Start();
    }

    public void Suspend()
    {
        if (_isClosing || _suspended) return;
        _suspended = true;
        _lengthConstraints.Range.Remove(this);
        _zRegistration?.Dispose();
        _zRegistration = null;
        EndDrag();
        ClearInteraction();
        _sizeTimer.Stop();
        _backgroundTimer.Stop();
        SynchronizeResources();
        if (!_windowClosed) Hide();
    }

    public void Resume()
    {
        if (_isClosing) return;
        _suspended = false;
        ApplySettings();
        ApplySnapshot(_lastSnapshot);
        RegisterZOrder();
        _backgroundTimer.Start();
    }

    public void Dispose()
    {
        if (_isClosing) return;
        Suspend();
        _isClosing = true;
        _spectrumSession.Dispose();
        _requests.Cancel();
        _requests.Dispose();
        _mediaControl.ArtworkPointerPressed -= StartDrag;
        _mediaControl.ArtworkPointerMoved -= ArtworkPointerMoved;
        _mediaControl.ArtworkPointerReleased -= PointerReleased;
        SettingsManager.SettingsChanged -= SettingsChanged;
        _prune.LevelChanged -= PruneChanged;
        _mouse.LeftButtonReleased -= LeftReleased;
        _audioInteractionService.DeviceSwitches.Updated -= OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated -= OnAudioDeviceInvalidated;
        DisposeMetricsSubscription();
        _foregroundSamplingSession.Dispose();
        _compactFlyout.Dispose();
        _mediaControl.ApplyHostVisibilitySuspension(true);
        _mediaControl.DisposePresentation();
        if (!_windowClosed) Close();
    }
}
