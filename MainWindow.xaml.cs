using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar;

/// <summary>
/// 协调媒体快照、任务栏定位、用户交互和所有窗口级资源的生命周期。
/// Coordinates media snapshots, taskbar placement, interaction, and window-owned resources.
/// </summary>
public partial class MainWindow : Window
{
    private const double CollapsedInfoWidth = 210;
    private const double ExpandedInfoWidth = 96;
    private const double PlayerWidthWithoutExtras = 271;
    private const double MetricsAreaWidth = 78;
    private const double OutputDeviceAreaWidth = 40;
    private const double VolumeControlAreaWidth = 40;
    private const double MediaSwitchAreaWidth = 254;
    private const double CentralHostWidth = 210;
    private const double AudioVisualizerWidth = 88;
    private const double AudioVisualizerCenterBias = 10;
    private const double HorizontalPlayerHeight = 44;
    private const double VerticalPlayerWidth = 72;
    private const double VerticalArtworkAreaHeight = 48;
    private const double VerticalBaseHeight = 165;
    private const double VerticalMetricAreaHeight = 29;
    private const double VerticalControlAreaHeight = 40;
    private const int MouseWheelDelta = 120;
    private const int VolumeWheelStepPercent = 2;
    private const int HorizontalMarginAt96Dpi = 8;
    private const int VerticalMarginAt96Dpi = 4;
    private const int AudioMonitorIntervalMilliseconds = 50;
    private const int EdgeVisiblePixels = 6;
    private const int EdgeActivationDistance = 72;
    private const int EdgeActivationSpanPadding = 80;
    private const int EdgeAnimationDurationMilliseconds = 180;

    private readonly MediaSessionService _mediaSessionService = new();
    private readonly SystemMetricsService _systemMetricsService = new();
    private readonly TaskbarPlacementService _taskbarPlacementService = new();
    private readonly AudioDeviceService _audioDeviceService = new();
    private readonly ApplicationVolumeService _applicationVolumeService = new();
    private readonly SettingsCoordinator _settingsCoordinator;
    // 这些定时器都由窗口拥有，必须在 OnClosed 中停止后再释放服务。
    // The window owns these timers; OnClosed stops them before disposing services.
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _placementTimer;
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _marqueeTimer;
    private readonly DispatcherTimer _audioMonitorTimer;
    private readonly DispatcherTimer _outputDeviceApplyTimer;
    private readonly DispatcherTimer _volumeApplyTimer;
    private readonly DispatcherTimer _volumePopupCloseTimer;
    private readonly DispatcherTimer _edgeAnimationTimer;
    private readonly DispatcherTimer _edgeHoverTimer;
    private MetricSettings _metricSettings;
    private WindowSettings _windowSettings;
    private PlacementSettings _placementSettings;
    private TaskbarSettings _taskbarSettings;
    private SystemMetricsSnapshot _lastMetricsSnapshot;
    private IReadOnlyList<MediaSessionOption> _mediaSessions = [];
    private IReadOnlyList<AudioDeviceOption> _outputDevices = [];
    // 这些服务持有 WinEvent、WASAPI、鼠标钩子或 Shell 图标等外部资源。
    // These services own WinEvent, WASAPI, mouse-hook, or Shell resources.
    private TaskbarEventWatcher? _taskbarEventWatcher;
    private AudioMonitorService? _audioMonitorService;
    private readonly MouseHookService _mouseHookService;
    private TrayIconService? _trayIconService;
    private TaskbarHostService? _taskbarHostService;
    private HwndSource? _windowSource;
    private NativeMethods.Rect? _lastTaskbarRect;
    private nint _windowHandle;
    private int? _automaticLeft;
    private int? _lastPositionLeft;
    private int? _lastPositionTop;
    private int _metricCycleIndex;
    private int _metricCycleTicks;
    // 自动定位只允许一次扫描；扫描期间的新请求会在结束后再补跑一次。
    // Automatic placement allows one scan; concurrent requests schedule one follow-up scan.
    private int _placementRefreshInProgress;
    private int _placementRefreshRequested;
    // 输出设备滚轮先预览候选项，停止输入一秒后再真正切换。
    // Output-device wheel input previews a candidate, then applies it after one idle second.
    private string? _pendingOutputDeviceId;
    private int _pendingOutputDeviceWheelSteps;
    private ApplicationVolumeSnapshot? _currentApplicationVolume;
    // 音量滚轮合并快速步进，并以短延迟批量写入 Core Audio。
    // Volume wheel steps are coalesced and written to Core Audio after a short delay.
    private int? _pendingVolumePercent;
    private int _pendingVolumeWheelSteps;
    // 来源切换时递增版本号，丢弃旧进程匹配查询的迟到结果。
    // Increment on source changes so stale process-matching results are ignored.
    private int _volumeRefreshVersion;
    private string? _lastVolumeSourceId;
    private bool _hasConnectedMedia;
    private bool _selectedMediaIsPlaying;
    private bool _hasPresented;
    private bool _isExpanded;
    private bool _isVerticalLayout;
    private bool _isMenuOpen;
    private bool _isDragging;
    private bool _dragMoved;
    private bool _isUpdatingVolumeSlider;
    private bool _isProcessingOutputDeviceWheel;
    private bool _isProcessingVolumeWheel;
    private bool _outputDeviceWheelUsesCompactStatus;
    private bool _volumeWheelUsesCompactStatus;
    private bool _showingOutputDeviceHoverStatus;
    private bool _showingVolumeHoverStatus;
    private readonly float[] _audioSpectrum = new float[AudioMonitorService.BandCount];
    private readonly float[] _smoothedAudioSpectrum = new float[AudioMonitorService.BandCount];
    private Border[] _audioBars = null!;
    private NativeMethods.Point _dragStartCursor;
    private int _dragStartWindowLeft;
    private int _dragStartWindowTop;
    private int? _floatingNormalLeft;
    private int? _floatingNormalTop;
    private int _floatingEdge;
    private int _expandedEdge;
    private int _lastFloatingWidth;
    private int _lastFloatingHeight;
    private NativeMethods.Rect _edgeAnimationFrom;
    private NativeMethods.Rect _edgeAnimationTo;
    private DateTime _edgeAnimationStarted;
    private bool _edgeAnimationExpanding;
    private bool _edgeAnimationHasTarget;

    public MainWindow()
    {
        TaskbarPlacementService.ValidateAlgorithm();
        _settingsCoordinator = (Application.Current as App)?.SettingsCoordinator ??
            new SettingsCoordinator();
        var settings = _settingsCoordinator.Current;
        _metricSettings = settings.Metrics;
        _windowSettings = settings.Window;
        _floatingNormalLeft = _windowSettings.FloatingLeft;
        _floatingNormalTop = _windowSettings.FloatingTop;
        RenderOptions.ProcessRenderMode = _metricSettings.LowGpuMode
            ? RenderMode.SoftwareOnly
            : RenderMode.Default;
        InitializeComponent();
        _audioBars =
        [
            AudioBar0,
            AudioBar1,
            AudioBar2,
            AudioBar3,
            AudioBar4,
            AudioBar5,
            AudioBar6,
            AudioBar7,
            AudioBar8
        ];

        Opacity = 0;
        _placementSettings = settings.Placement;
        _taskbarSettings = TaskbarSettingsService.Read();
        if (_taskbarSettings.Alignment == TaskbarAlignment.Unknown &&
            _placementSettings.CachedTaskbarAlignment is { } cachedAlignment)
        {
            _taskbarSettings = _taskbarSettings with { Alignment = cachedAlignment };
        }
        _mouseHookService = new MouseHookService(Dispatcher);
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnPositionTimerTick,
            Dispatcher);
        _placementTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(30),
            DispatcherPriority.Background,
            OnPlacementTimerTick,
            Dispatcher);
        _metricsTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2.5),
            DispatcherPriority.Background,
            OnMetricsTimerTick,
            Dispatcher);
        _collapseTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(160),
            DispatcherPriority.Input,
            OnCollapseTimerTick,
            Dispatcher);
        _collapseTimer.Stop();
        _marqueeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(260),
            DispatcherPriority.Render,
            OnMarqueeTimerTick,
            Dispatcher);
        _marqueeTimer.Stop();
        _audioMonitorTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(AudioMonitorIntervalMilliseconds),
            DispatcherPriority.Background,
            OnAudioMonitorTimerTick,
            Dispatcher);
        _audioMonitorTimer.Stop();
        _outputDeviceApplyTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnOutputDeviceApplyTimerTick,
            Dispatcher);
        _outputDeviceApplyTimer.Stop();
        _volumeApplyTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(90),
            DispatcherPriority.Background,
            OnVolumeApplyTimerTick,
            Dispatcher);
        _volumeApplyTimer.Stop();
        _volumePopupCloseTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnVolumePopupCloseTimerTick,
            Dispatcher);
        _volumePopupCloseTimer.Stop();
        _edgeAnimationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnEdgeAnimationTick,
            Dispatcher);
        _edgeAnimationTimer.Stop();
        _edgeHoverTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(90),
            DispatcherPriority.Input,
            OnEdgeHoverTimerTick,
            Dispatcher);
        _edgeHoverTimer.Start();

        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        _mediaSessionService.SessionsChanged += OnSessionsChanged;
        _settingsCoordinator.Changed += SettingsCoordinator_OnChanged;
        _mouseHookService.MouseButtonPressed += MouseHook_OnMouseButtonPressed;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new nint(extendedStyle));

        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        _taskbarHostService = new TaskbarHostService(_windowHandle);
        _trayIconService = new TrayIconService();
        _trayIconService.ContextMenuRequested += TrayIcon_OnContextMenuRequested;
        _trayIconService.DoubleClicked += TrayIcon_OnDoubleClicked;
        _trayIconService.ShellRestarted += TrayIcon_OnShellRestarted;

        _taskbarEventWatcher = new TaskbarEventWatcher(Dispatcher);
        _taskbarEventWatcher.TaskbarChanged += Taskbar_OnChanged;

        ApplyMetricSettings();
        ApplyWindowSettings();
        ApplyPlacementSettings();
        PositionOverTaskbar(force: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => PositionOverTaskbar(force: true));
        _positionTimer.Start();
        if (_placementSettings.AutomaticPlacement)
        {
            _placementTimer.Start();
        }
        _metricsTimer.Start();
        UpdateMetrics(advanceCycle: false);
        SetExpanded(expanded: false, animate: false);
        await RefreshAutomaticPlacementAsync();

        try
        {
            await _mediaSessionService.InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowDisconnectedState("无法访问系统媒体会话", exception.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 顺序很重要：先停止回调源，再解除宿主/钩子，最后释放 COM 与服务。
        // Order matters: stop callback sources, detach the host/hooks, then dispose services.
        _positionTimer.Stop();
        _placementTimer.Stop();
        _metricsTimer.Stop();
        _collapseTimer.Stop();
        _marqueeTimer.Stop();
        _audioMonitorTimer.Stop();
        _outputDeviceApplyTimer.Stop();
        _volumeApplyTimer.Stop();
        _volumePopupCloseTimer.Stop();
        _edgeAnimationTimer.Stop();
        _edgeHoverTimer.Stop();
        _audioMonitorService?.Dispose();
        _audioMonitorService = null;
        _taskbarEventWatcher?.Dispose();
        _mouseHookService.Dispose();
        _trayIconService?.Dispose();
        _taskbarHostService?.Dispose();
        _taskbarHostService = null;
        _windowSource?.RemoveHook(WindowMessageHook);
        _mediaSessionService.Dispose();
        _systemMetricsService.Dispose();
        _settingsCoordinator.Changed -= SettingsCoordinator_OnChanged;
    }

    private void SettingsCoordinator_OnChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SettingsCoordinator_OnChanged(sender, e));
            return;
        }

        var nextWindowSettings = e.Settings.Window;
        if (nextWindowSettings.HostMode != _windowSettings.HostMode)
        {
            if (!e.Sections.HasFlag(SettingsSection.All) &&
                NativeMethods.GetWindowRect(_windowHandle, out var currentRect))
            {
                nextWindowSettings = nextWindowSettings with
                {
                    FloatingLeft = currentRect.Left,
                    FloatingTop = currentRect.Top
                };
                _settingsCoordinator.SynchronizeWindow(nextWindowSettings);
            }

            _windowSettings = nextWindowSettings;
            (Application.Current as App)?.RecreateMainWindow();
            return;
        }

        if (e.Sections.HasFlag(SettingsSection.Components) ||
            e.Sections.HasFlag(SettingsSection.Performance))
        {
            _metricSettings = e.Settings.Metrics;
            ApplyMetricSettings();
        }

        if (e.Sections.HasFlag(SettingsSection.Appearance))
        {
            (Application.Current as App)?.ThemeService?.Refresh();
        }

        if (e.Sections.HasFlag(SettingsSection.Window) ||
            e.Sections.HasFlag(SettingsSection.General) ||
            e.Sections.HasFlag(SettingsSection.Interaction))
        {
            _windowSettings = nextWindowSettings;
            _lastTaskbarRect = null;
            _lastPositionLeft = null;
            _lastPositionTop = null;
            _automaticLeft = null;
            ApplyWindowSettings();
        }

        if (e.Sections.HasFlag(SettingsSection.Placement))
        {
            var nextPlacement = e.Settings.Placement;
            if (!nextPlacement.AutomaticPlacement && _placementSettings.AutomaticPlacement)
            {
                nextPlacement = nextPlacement with
                {
                    PositionLocked = false,
                    ManualOffsetDip = GetCurrentOffsetDip()
                };
                _settingsCoordinator.SynchronizePlacement(nextPlacement);
            }
            else if (nextPlacement.AutomaticPlacement && !_placementSettings.AutomaticPlacement &&
                NativeMethods.GetWindowRect(_windowHandle, out var currentRect))
            {
                _automaticLeft = currentRect.Left;
                nextPlacement = nextPlacement with { PositionLocked = true };
                _settingsCoordinator.SynchronizePlacement(nextPlacement);
            }

            _placementSettings = nextPlacement;
            ApplyPlacementSettings();
            if (_placementSettings.AutomaticPlacement)
            {
                _placementTimer.Start();
                _ = RefreshAutomaticPlacementAsync();
            }
            else
            {
                _placementTimer.Stop();
            }
        }

        PositionOverTaskbar(force: true);
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_windowSettings.HostMode == WindowHostMode.Taskbar)
        {
            RefreshTaskbarSettings();
        }

        UpdateFloatingEdgeCollapse();
        PositionOverTaskbar(force: false);
    }

    private async void OnPlacementTimerTick(object? sender, EventArgs e)
    {
        await RefreshAutomaticPlacementAsync();
    }

    private void Taskbar_OnChanged(TaskbarWindowEvent taskbarEvent)
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar)
        {
            return;
        }

        RefreshTaskbarSettings();
        if (TryGetTaskbarBounds(out var bounds))
        {
            var sizeChanged = !_lastTaskbarRect.HasValue ||
                _lastTaskbarRect.Value.Width != bounds.ScreenBounds.Width ||
                _lastTaskbarRect.Value.Height != bounds.ScreenBounds.Height;
            if (_placementSettings.AutomaticPlacement && sizeChanged)
            {
                _automaticLeft = null;
                _ = RefreshAutomaticPlacementAsync();
            }

            // Vertical location changes are inherited from the Explorer parent.
            // Repositioning the child during that animation would reintroduce the lag.
            if (taskbarEvent.EventId == NativeMethods.EventObjectLocationChange &&
                !sizeChanged)
            {
                return;
            }
        }

        PositionOverTaskbar(force: true);
    }

    private void RefreshTaskbarSettings()
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar)
        {
            return;
        }

        var settings = TaskbarSettingsService.Read();
        if (settings.Alignment == TaskbarAlignment.Unknown &&
            _taskbarSettings.Alignment != TaskbarAlignment.Unknown)
        {
            settings = settings with { Alignment = _taskbarSettings.Alignment };
        }

        if (settings.Alignment != _taskbarSettings.Alignment)
        {
            _automaticLeft = null;
            _taskbarSettings = settings;
            PositionOverTaskbar(force: true);
            if (_placementSettings.AutomaticPlacement)
            {
                _ = RefreshAutomaticPlacementAsync();
            }
        }
        else
        {
            _taskbarSettings = settings;
        }
    }

    private bool TryGetTaskbarBounds(out TaskbarHostBounds bounds)
    {
        bounds = default;
        return _taskbarHostService?.TryGetBounds(out bounds) == true;
    }

    private void PositionOverTaskbar(bool force)
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        if (_windowSettings.HostMode == WindowHostMode.Floating)
        {
            PositionFloatingWindow(force);
            return;
        }

        _floatingEdge = 0;

        CollapseWhenPointerLeavesWindow();

        if (_windowSettings.HideWhenNoMedia &&
            !_hasConnectedMedia &&
            !_windowSettings.AlwaysOnTop &&
            !_isMenuOpen)
        {
            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        if (!_windowSettings.AlwaysOnTop &&
            NativeMethods.ShouldHideForFullScreenApp(_windowHandle))
        {
            if (Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
            }

            StopMarquees();

            return;
        }

        if (!TryGetTaskbarBounds(out var bounds))
        {
            if (_windowSettings.AlwaysOnTop)
            {
                Visibility = Visibility.Visible;
                Topmost = true;
                return;
            }

            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        var taskbarRect = bounds.ScreenBounds;
        var scale = bounds.Scale;
        var verticalLayout = ResolveVerticalTaskbarLayout(taskbarRect);
        ApplyPlayerLayout(verticalLayout);
        ConfigurePopupPlacement(bounds, verticalLayout);
        var layoutScale = CalculateTaskbarLayoutScale(bounds, verticalLayout);
        ApplyPlayerScale(layoutScale);
        if (_placementSettings.AutomaticPlacement &&
            _lastTaskbarRect.HasValue &&
            _lastTaskbarRect.Value.Width != taskbarRect.Width)
        {
            _automaticLeft = null;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            force = true;
        }

        var width = Math.Max(1, (int)Math.Ceiling(PlayerRoot.Width * layoutScale * scale));
        var height = Math.Max(1, (int)Math.Ceiling(PlayerRoot.Height * layoutScale * scale));
        int left;
        int top;
        if (verticalLayout)
        {
            var margin = Math.Min(
                (int)Math.Round(VerticalMarginAt96Dpi * scale),
                Math.Max(0, (taskbarRect.Height - height) / 2));
            var minTop = taskbarRect.Top + margin;
            var maxTop = Math.Max(minTop, taskbarRect.Bottom - margin - height);
            top = Math.Clamp(
                taskbarRect.Top + (int)Math.Round(
                    _placementSettings.ManualVerticalOffsetDip * scale),
                minTop,
                maxTop);
            left = taskbarRect.Left + (taskbarRect.Width - width) / 2;
        }
        else
        {
            var margin = Math.Min(
                (int)Math.Round(HorizontalMarginAt96Dpi * scale),
                Math.Max(0, (taskbarRect.Width - width) / 2));
            var minLeft = taskbarRect.Left + margin;
            var maxLeft = Math.Max(minLeft, taskbarRect.Right - margin - width);
            var desiredLeft = _placementSettings.AutomaticPlacement
                ? ResolveAutomaticLeft(taskbarRect, scale, minLeft)
                : taskbarRect.Left + (int)Math.Round(
                    _placementSettings.ManualOffsetDip * scale);
            desiredLeft ??= _lastPositionLeft;
            if (!desiredLeft.HasValue)
            {
                _ = RefreshAutomaticPlacementAsync();
                return;
            }

            left = Math.Clamp(desiredLeft.Value, minLeft, maxLeft);
            top = taskbarRect.Top + (taskbarRect.Height - height) / 2;
        }

        var rectChanged = !_lastTaskbarRect.HasValue ||
            !_lastTaskbarRect.Value.Equals(taskbarRect);
        var positionChanged = _lastPositionLeft != left || _lastPositionTop != top;

        Height = PlayerRoot.Height * layoutScale;
        Topmost = _windowSettings.AlwaysOnTop;
        if (!force && !rectChanged && !positionChanged)
        {
            RevealAfterPlacement();
            return;
        }

        _lastTaskbarRect = taskbarRect;
        _lastPositionLeft = left;
        _lastPositionTop = top;
        _taskbarHostService?.Position(
            left,
            top,
            width,
            height,
            visible: true,
            topmost: _windowSettings.AlwaysOnTop);
        RevealAfterPlacement();
    }

    private bool ResolveVerticalTaskbarLayout(NativeMethods.Rect taskbarRect)
    {
        return _windowSettings.LayoutMode switch
        {
            PlayerLayoutMode.Vertical => true,
            PlayerLayoutMode.Horizontal => false,
            _ => taskbarRect.Height > taskbarRect.Width
        };
    }

    private void ConfigurePopupPlacement(TaskbarHostBounds bounds, bool verticalLayout)
    {
        if (!verticalLayout)
        {
            SetPopupPlacement(PlacementMode.Top, 0, -7);
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(bounds.Taskbar, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        var taskbarIsOnLeft = monitor == nint.Zero ||
            !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) ||
            bounds.ScreenBounds.Left + bounds.ScreenBounds.Width / 2 <=
                monitorInfo.Monitor.Left + monitorInfo.Monitor.Width / 2;
        SetPopupPlacement(
            taskbarIsOnLeft ? PlacementMode.Right : PlacementMode.Left,
            taskbarIsOnLeft ? 7 : -7,
            0);
    }

    private void SetPopupPlacement(
        PlacementMode placement,
        double horizontalOffset,
        double verticalOffset)
    {
        foreach (var popup in new[]
        {
            VolumeControlPopup,
            OutputDevicePopup,
            OutputDeviceStatusPopup,
            VolumeStatusPopup
        })
        {
            popup.Placement = placement;
            popup.HorizontalOffset = horizontalOffset;
            popup.VerticalOffset = verticalOffset;
        }
    }

    private double CalculateTaskbarLayoutScale(
        TaskbarHostBounds bounds,
        bool verticalLayout)
    {
        var requestedScale = _windowSettings.DisplayScalePercent / 100d;
        var availableThickness = verticalLayout
            ? bounds.ScreenBounds.Width
            : bounds.ScreenBounds.Height;
        var availableLength = verticalLayout
            ? bounds.ScreenBounds.Height
            : bounds.ScreenBounds.Width;
        var designThickness = verticalLayout
            ? VerticalPlayerWidth
            : HorizontalPlayerHeight;
        var designLength = verticalLayout ? PlayerRoot.Height : PlayerRoot.Width;
        var maximumThicknessScale = availableThickness / (designThickness * bounds.Scale);
        var maximumLengthScale = availableLength / (designLength * bounds.Scale);
        return Math.Clamp(
            Math.Min(requestedScale, Math.Min(maximumThicknessScale, maximumLengthScale)),
            0.1,
            1.25);
    }

    private void ApplyPlayerScale(double scale)
    {
        PlayerScaleTransform.ScaleX = scale;
        PlayerScaleTransform.ScaleY = scale;
    }

    private void ApplyPlayerLayout(bool vertical)
    {
        if (_isVerticalLayout == vertical)
        {
            return;
        }

        var contentVisible = PlayerContent.Visibility == Visibility.Visible ||
            VerticalPlayerContent.Visibility == Visibility.Visible;
        _isVerticalLayout = vertical;
        SetPlayerContentVisibility(contentVisible);
        PlayerRoot.Width = vertical ? VerticalPlayerWidth : CalculateHorizontalPlayerWidth();
        PlayerRoot.Height = vertical ? CalculateVerticalPlayerHeight() : HorizontalPlayerHeight;
        VerticalPlayerContent.Height = PlayerRoot.Height;
        VolumeControlPopup.PlacementTarget = vertical
            ? VerticalVolumeControlHost
            : VolumeControlHost;
        OutputDevicePopup.PlacementTarget = vertical
            ? VerticalOutputDeviceHost
            : OutputDeviceHost;
        OutputDeviceStatusPopup.PlacementTarget = vertical
            ? VerticalOutputDeviceHost
            : OutputDeviceHost;
        VolumeStatusPopup.PlacementTarget = vertical
            ? VerticalVolumeControlHost
            : VolumeControlHost;
        SetExpanded(_isExpanded, animate: false);
        ApplyPlacementSettings();
        if (EdgeCollapseIndicator.Visibility == Visibility.Visible)
        {
            UpdateEdgeCollapseIndicator(visible: true);
        }
        ScheduleMarqueeUpdate();
    }

    private void SetPlayerContentVisibility(bool visible)
    {
        PlayerContent.Visibility = visible && !_isVerticalLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalPlayerContent.Visibility = visible && _isVerticalLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private double CalculateHorizontalPlayerWidth()
    {
        return PlayerWidthWithoutExtras +
            (_metricSettings.SelectedCount > 0 ? MetricsAreaWidth : 0) +
            (_metricSettings.OutputDeviceSwitcherEnabled ? OutputDeviceAreaWidth : 0) +
            (_metricSettings.VolumeControlEnabled ? VolumeControlAreaWidth : 0);
    }

    private double CalculateVerticalPlayerHeight()
    {
        return VerticalBaseHeight +
            (_metricSettings.SelectedCount > 0 ? VerticalMetricAreaHeight : 0) +
            (_metricSettings.OutputDeviceSwitcherEnabled ? VerticalControlAreaHeight : 0) +
            (_metricSettings.VolumeControlEnabled ? VerticalControlAreaHeight : 0);
    }

    private int? ResolveAutomaticLeft(
        NativeMethods.Rect taskbarRect,
        double scale,
        int fallbackLeft)
    {
        if (_automaticLeft.HasValue)
        {
            return _automaticLeft.Value;
        }

        var taskbarWidthDip = (int)Math.Round(taskbarRect.Width / scale);
        var playerWidthDip = (int)Math.Round(
            PlayerRoot.Width * PlayerScaleTransform.ScaleX);
        var cachedOffset = _placementSettings.CachedAutomaticOffsetDip;
        var cachedTaskbarWidth = _placementSettings.CachedTaskbarWidthDip;
        var cachedPlayerWidth = _placementSettings.CachedPlayerWidthDip;
        var cachedAlignment = _placementSettings.CachedTaskbarAlignment;
        var cacheMatches = cachedOffset.HasValue &&
            cachedTaskbarWidth.HasValue &&
            cachedPlayerWidth.HasValue &&
            cachedAlignment.HasValue &&
            Math.Abs(cachedTaskbarWidth.Value - taskbarWidthDip) <= 2 &&
            Math.Abs(cachedPlayerWidth.Value - playerWidthDip) <= 1 &&
            cachedAlignment.Value == _taskbarSettings.Alignment;
        if (cacheMatches)
        {
            _automaticLeft = taskbarRect.Left + (int)Math.Round(
                cachedOffset.GetValueOrDefault() * scale);
            return _automaticLeft.Value;
        }

        if (_taskbarSettings.Alignment == TaskbarAlignment.Left)
        {
            // 重建任务栏后 UI Automation 暴露较慢；精确扫描前暂用可用区中点。
            // UI Automation lags after rebuilds; use the free-area midpoint until scanned.
            var playerWidth = (int)Math.Ceiling(
                PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
            var availableWidth = Math.Max(0, taskbarRect.Width - playerWidth);
            return taskbarRect.Left + availableWidth / 2;
        }

        return fallbackLeft;
    }

    private void RevealAfterPlacement()
    {
        if (_hasPresented && Opacity == 1)
        {
            return;
        }

        _hasPresented = true;
        Opacity = 1;
        ScheduleMarqueeUpdate();
    }

    private async Task RefreshAutomaticPlacementAsync()
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar ||
            _isVerticalLayout ||
            !_placementSettings.AutomaticPlacement ||
            _windowHandle == nint.Zero ||
            _isMenuOpen)
        {
            return;
        }

        if (Interlocked.Exchange(ref _placementRefreshInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _placementRefreshRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _placementRefreshRequested, 0);
                await RefreshAutomaticPlacementCoreAsync();
            }
            while (_placementSettings.AutomaticPlacement &&
                !_isMenuOpen &&
                Interlocked.Exchange(ref _placementRefreshRequested, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _placementRefreshInProgress, 0);
            if (_placementSettings.AutomaticPlacement &&
                !_isMenuOpen &&
                Interlocked.Exchange(ref _placementRefreshRequested, 0) != 0)
            {
                _ = RefreshAutomaticPlacementAsync();
            }
        }
    }

    private async Task RefreshAutomaticPlacementCoreAsync()
    {
        if (!TryGetTaskbarBounds(out var bounds))
        {
            return;
        }

        var taskbar = bounds.Taskbar;
        var taskbarRect = bounds.ScreenBounds;
        var alignment = _taskbarSettings.Alignment;
        var scale = bounds.Scale;
        var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
        var playerWidth = (int)Math.Ceiling(
            PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
        TaskbarPlacementResult? placement;
        try
        {
            placement = await _taskbarPlacementService.FindBestLeftAsync(
                taskbar,
                taskbarRect,
                playerWidth,
                margin,
                _automaticLeft).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            return;
        }

        var currentSettings = TaskbarSettingsService.Read();
        if (currentSettings.Alignment != TaskbarAlignment.Unknown)
        {
            _taskbarSettings = currentSettings;
        }

        var hasReliablePlacement = placement.HasValue &&
            (_taskbarSettings.Alignment != TaskbarAlignment.Left ||
                placement.Value.OccupiedElementCount > 0);
        if (!_placementSettings.AutomaticPlacement ||
            _isMenuOpen ||
            !hasReliablePlacement ||
            alignment != _taskbarSettings.Alignment)
        {
            if (alignment != _taskbarSettings.Alignment)
            {
                Interlocked.Exchange(ref _placementRefreshRequested, 1);
            }

            return;
        }

        _automaticLeft = placement!.Value.Left;
        var cachedSettings = _placementSettings with
        {
            CachedAutomaticOffsetDip = (int)Math.Round(
                (placement.Value.Left - taskbarRect.Left) / scale),
            CachedTaskbarWidthDip = (int)Math.Round(taskbarRect.Width / scale),
            CachedPlayerWidthDip = (int)Math.Round(
                PlayerRoot.Width * PlayerScaleTransform.ScaleX),
            CachedTaskbarAlignment = _taskbarSettings.Alignment == TaskbarAlignment.Unknown
                ? null
                : _taskbarSettings.Alignment
        };
        if (cachedSettings != _placementSettings)
        {
            _placementSettings = cachedSettings;
            SavePlacementSettings(showError: false);
        }

        PositionOverTaskbar(force: true);
    }

    private void CollapseWhenPointerLeavesWindow()
    {
        if (!_isExpanded ||
            _isMenuOpen ||
            OutputDevicePopup.IsOpen ||
            VolumeControlPopup.IsOpen ||
            _isDragging ||
            !NativeMethods.GetCursorPos(out var cursor) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return;
        }

        var isInside = cursor.X >= windowRect.Left &&
            cursor.X < windowRect.Right &&
            cursor.Y >= windowRect.Top &&
            cursor.Y < windowRect.Bottom;
        if (!isInside)
        {
            SetExpanded(expanded: false, animate: true);
        }
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private void OnSessionsChanged(IReadOnlyList<MediaSessionOption> sessions)
    {
        Dispatcher.InvokeAsync(() => ApplySessions(sessions));
    }

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _selectedMediaIsPlaying = snapshot.IsConnected && snapshot.IsPlaying;
        _hasConnectedMedia = _mediaSessions.Any(session => session.IsPlaying) ||
            (_selectedMediaIsPlaying && _mediaSessions.Count == 0);
        var volumeSourceChanged = !string.Equals(
            _lastVolumeSourceId,
            snapshot.SourceId,
            StringComparison.OrdinalIgnoreCase);
        _lastVolumeSourceId = snapshot.SourceId;
        if (volumeSourceChanged)
        {
            Interlocked.Increment(ref _volumeRefreshVersion);
            _volumeApplyTimer.Stop();
            _pendingVolumePercent = null;
            _pendingVolumeWheelSteps = 0;
            _currentApplicationVolume = null;
        }

        TitleText.Text = snapshot.Title;
        ArtistText.Text = snapshot.Artist;
        ArtworkImage.Source = snapshot.Artwork;
        VerticalTitleText.Text = FormatVerticalText(snapshot.Title);
        VerticalArtistText.Text = FormatVerticalText(snapshot.Artist);
        VerticalTitleText.ToolTip = snapshot.Title;
        VerticalArtistText.ToolTip = snapshot.Artist;
        VerticalArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalArtworkPlaceholder.Visibility = ArtworkPlaceholder.Visibility;

        PreviousButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        VerticalPreviousButton.IsEnabled = PreviousButton.IsEnabled;
        VerticalPlayPauseButton.IsEnabled = PlayPauseButton.IsEnabled;
        VerticalNextButton.IsEnabled = NextButton.IsEnabled;
        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
        VerticalPlayPauseGlyph.Text = PlayPauseGlyph.Text;
        PlayPauseButton.ToolTip = snapshot.IsPlaying ? "暂停" : "播放";
        VerticalPlayPauseButton.ToolTip = PlayPauseButton.ToolTip;

        ConnectionMenuText.Text = snapshot.IsConnected
            ? $"{snapshot.SourceName}：{snapshot.Title}"
            : "等待媒体播放";
        ShowSourceMenuItem.Header = $"显示 {snapshot.SourceName}";
        ShowSourceMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.SourceId);
        _trayIconService?.UpdateTooltip(
            snapshot.IsConnected
                ? $"AF Media Bar · {snapshot.SourceName}：{snapshot.Title} - {snapshot.Artist}"
                : "AF Media Bar - 等待媒体播放");
        if (_metricSettings.VolumeControlEnabled &&
            (volumeSourceChanged ||
                VolumeControlPopup.IsOpen ||
                VolumeStatusPopup.IsOpen))
        {
            _ = RefreshCurrentMediaVolumeAsync(
                snapshot.SourceId,
                snapshot.SourceName);
        }

        ScheduleMarqueeUpdate();
        if (_windowSettings.HideWhenNoMedia)
        {
            PositionOverTaskbar(force: true);
        }
    }

    private void ApplySessions(IReadOnlyList<MediaSessionOption> sessions)
    {
        _mediaSessions = sessions;
        var hasPlayingSession = sessions.Any(session => session.IsPlaying) ||
            (_selectedMediaIsPlaying && sessions.Count == 0);
        var hasChanged = _hasConnectedMedia != hasPlayingSession;
        _hasConnectedMedia = hasPlayingSession;
        if (hasChanged)
        {
            if (_windowSettings.HideWhenNoMedia)
            {
                PositionOverTaskbar(force: true);
            }
        }
        MediaSourcesMenuItem.Items.Clear();
        if (sessions.Count == 0)
        {
            MediaSourcesMenuItem.Items.Add(new MenuItem
            {
                Header = "暂无可用媒体会话",
                IsEnabled = false
            });
            MediaSourcesMenuItem.IsEnabled = false;
            return;
        }

        MediaSourcesMenuItem.IsEnabled = true;
        foreach (var session in sessions)
        {
            var item = new MenuItem
            {
                Header = session.IsPlaying
                    ? $"{session.DisplayName}（播放中）"
                    : session.DisplayName,
                IsCheckable = true,
                IsChecked = session.IsSelected,
                Tag = session.Key
            };
            item.Click += MediaSource_OnClick;
            MediaSourcesMenuItem.Items.Add(item);
        }
    }

    private async void MediaSource_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string key })
        {
            await RunMediaCommandAsync(() => _mediaSessionService.SelectSessionAsync(key));
        }
    }

    private void ShowDisconnectedState(string title, string detail)
    {
        ApplySnapshot(MediaSnapshot.Disconnected with
        {
            Title = title,
            Artist = detail
        });
    }

    private void PlayerRoot_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        SetExpanded(expanded: true, animate: true);
    }

    private void PlayerRoot_OnMouseLeave(object sender, MouseEventArgs e)
    {
        ScheduleCollapse();
    }

    private void ScheduleCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isMenuOpen &&
            !OutputDevicePopup.IsOpen &&
            !VolumeControlPopup.IsOpen &&
            !_isDragging &&
            !PlayerRoot.IsMouseOver)
        {
            SetExpanded(expanded: false, animate: true);
        }
    }

    private void SetExpanded(bool expanded, bool animate)
    {
        if (!_windowSettings.AutoCollapse && !expanded)
        {
            expanded = true;
        }

        _isExpanded = expanded;
        animate &= !_metricSettings.LowGpuMode;
        if (_isVerticalLayout)
        {
            ApplyVerticalExpandedState(expanded, animate);
            return;
        }

        ControlsHost.IsHitTestVisible = expanded;
        var showVisualizer = _metricSettings.AudioMonitorEnabled && !expanded;
        AudioVisualizerHost.Visibility = showVisualizer
            ? Visibility.Visible
            : Visibility.Collapsed;
        InfoHost.Visibility = showVisualizer
            ? Visibility.Collapsed
            : Visibility.Visible;
        var infoWidth = expanded
            ? ExpandedInfoWidth
            : CollapsedInfoWidth;
        InfoHost.BeginAnimation(FrameworkElement.WidthProperty, null);
        InfoHost.MaxWidth = infoWidth;
        InfoHost.Width = infoWidth;
        TitleText.Width = double.NaN;
        TitleText.MaxWidth = double.PositiveInfinity;
        TitleText.TextTrimming = TextTrimming.None;
        UpdateAudioVisualizerPlacement();
        var controlsOpacity = expanded ? 1d : 0d;
        var controlsOffset = expanded ? 0d : 8d;
        var titleOffset = expanded ? -8d : 0d;
        var artistOffset = expanded ? 0d : 3d;
        var artistOpacity = expanded ? 1d : 0d;
        if (!animate)
        {
            ControlsHost.BeginAnimation(UIElement.OpacityProperty, null);
            ControlsTransform.BeginAnimation(TranslateTransform.XProperty, null);
            TitleTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ArtistTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ArtistText.BeginAnimation(UIElement.OpacityProperty, null);
            ControlsHost.Opacity = controlsOpacity;
            ControlsTransform.X = controlsOffset;
            TitleTransform.Y = titleOffset;
            ArtistTransform.Y = artistOffset;
            ArtistText.Opacity = artistOpacity;
            ScheduleMarqueeUpdate();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        ControlsHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(controlsOpacity, duration, easing));
        ControlsTransform.BeginAnimation(
            TranslateTransform.XProperty,
            CreateAnimation(controlsOffset, duration, easing));
        TitleTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(titleOffset, duration, easing));
        ArtistTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(artistOffset, duration, easing));
        ArtistText.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(artistOpacity, duration, easing));
        ScheduleMarqueeUpdate();
    }

    private void ApplyVerticalExpandedState(bool expanded, bool animate)
    {
        AudioVisualizerHost.Visibility = Visibility.Collapsed;
        VerticalInfoHost.IsHitTestVisible = !expanded;
        VerticalControlsHost.IsHitTestVisible = expanded;
        var infoOpacity = expanded ? 0d : 1d;
        var controlsOpacity = expanded ? 1d : 0d;
        if (!animate)
        {
            VerticalInfoHost.BeginAnimation(UIElement.OpacityProperty, null);
            VerticalControlsHost.BeginAnimation(UIElement.OpacityProperty, null);
            VerticalInfoHost.Opacity = infoOpacity;
            VerticalControlsHost.Opacity = controlsOpacity;
            ScheduleMarqueeUpdate();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        VerticalInfoHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(infoOpacity, duration, easing));
        VerticalControlsHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(controlsOpacity, duration, easing));
        ScheduleMarqueeUpdate();
    }

    private void UpdateAudioVisualizerPlacement()
    {
        var centeredLeft =
            (CentralHostWidth - AudioVisualizerWidth) / 2 +
            AudioVisualizerCenterBias;
        var rightmostLeft = CentralHostWidth - AudioVisualizerWidth;
        AudioVisualizerTransform.X = Math.Clamp(
            centeredLeft,
            0,
            rightmostLeft);
    }

    private static DoubleAnimation CreateAnimation(
        double target,
        Duration duration,
        IEasingFunction easing)
    {
        return new DoubleAnimation
        {
            To = target,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
    }

    private void ScheduleMarqueeUpdate()
    {
        if (_metricSettings.LowGpuMode)
        {
            _marqueeTimer.Stop();
            StopMarquees();
            return;
        }

        _marqueeTimer.Stop();
        _marqueeTimer.Start();
    }

    private void OnMarqueeTimerTick(object? sender, EventArgs e)
    {
        _marqueeTimer.Stop();
        UpdateMarquees();
    }

    private void UpdateMarquees()
    {
        if (_metricSettings.LowGpuMode || !IsWindowContentVisible())
        {
            StopMarquees();
            return;
        }

        if (_isVerticalLayout)
        {
            StopHorizontalMarquees();
            if (_isExpanded)
            {
                StopVerticalMarquees();
                return;
            }

            UpdateVerticalMarquee(
                VerticalTitleMarquee,
                VerticalTitleViewport,
                VerticalTitleTransform);
            UpdateVerticalMarquee(
                VerticalArtistMarquee,
                VerticalArtistViewport,
                VerticalArtistTransform);
            return;
        }

        StopVerticalMarquees();
        UpdateMarquee(TitleText, TitleViewport, TitleTransform);
        UpdateMarquee(ArtistText, ArtistViewport, ArtistTransform);
    }

    private bool IsWindowContentVisible()
    {
        if (_windowHandle == nint.Zero ||
            Visibility != Visibility.Visible ||
            !_hasPresented ||
            Opacity <= 0.01 ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        return monitor != nint.Zero &&
            NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) &&
            windowRect.Right > monitorInfo.Monitor.Left &&
            windowRect.Left < monitorInfo.Monitor.Right &&
            windowRect.Bottom > monitorInfo.Monitor.Top &&
            windowRect.Top < monitorInfo.Monitor.Bottom;
    }

    private void StopMarquees()
    {
        StopHorizontalMarquees();
        StopVerticalMarquees();
    }

    private void StopHorizontalMarquees()
    {
        StopHorizontalMarquee(TitleTransform);
        StopHorizontalMarquee(ArtistTransform);
    }

    private void StopVerticalMarquees()
    {
        StopVerticalMarquee(VerticalTitleTransform);
        StopVerticalMarquee(VerticalArtistTransform);
    }

    private static void StopHorizontalMarquee(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
    }

    private static void StopVerticalMarquee(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private static void UpdateMarquee(
        System.Windows.Controls.TextBlock text,
        FrameworkElement viewport,
        TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        text.Width = double.NaN;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = Math.Ceiling(text.DesiredSize.Width + 1);
        text.Width = Math.Max(viewport.ActualWidth, textWidth);
        var overflow = textWidth - viewport.ActualWidth;
        if (overflow <= 2 || viewport.ActualWidth <= 0)
        {
            return;
        }

        var travelSeconds = Math.Max(3, overflow / 22d);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -(overflow + 8),
            BeginTime = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(travelSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static void UpdateVerticalMarquee(
        FrameworkElement content,
        FrameworkElement viewport,
        TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
        content.Height = double.NaN;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentHeight = Math.Ceiling(content.DesiredSize.Height + 1);
        content.Height = Math.Max(viewport.ActualHeight, contentHeight);
        var overflow = contentHeight - viewport.ActualHeight;
        if (overflow <= 2 || viewport.ActualHeight <= 0)
        {
            return;
        }

        var travelSeconds = Math.Max(3, overflow / 22d);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -(overflow + 8),
            BeginTime = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(travelSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private static string FormatVerticalText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length * 2);
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element is "\r" or "\n")
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }
            builder.Append(element);
        }

        return builder.ToString();
    }

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
        _metricCycleTicks++;
        var selectedCount = _metricSettings.SelectedCount;
        var advance = selectedCount > 1 && _metricCycleTicks % 3 == 0;
        UpdateMetrics(advance);
    }

    private void UpdateMetrics(bool advanceCycle)
    {
        _lastMetricsSnapshot = _systemMetricsService.Sample(_metricSettings);
        var selectedCount = _metricSettings.SelectedCount;
        if (selectedCount == 0)
        {
            MetricsText.Text = string.Empty;
            MetricsHost.Visibility = Visibility.Collapsed;
            VerticalMetricsText.Text = string.Empty;
            VerticalMetricsHost.Visibility = Visibility.Collapsed;
            UpdatePlayerWidth(metricsVisible: false);
            return;
        }

        MetricsHost.Visibility = Visibility.Visible;
        VerticalMetricsHost.Visibility = Visibility.Visible;
        UpdatePlayerWidth(metricsVisible: true);
        if (advanceCycle)
        {
            _metricCycleIndex = (_metricCycleIndex + 1) % selectedCount;
        }
        else
        {
            _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, selectedCount - 1);
        }

        SetMetricText(BuildMetricValue(_lastMetricsSnapshot, _metricCycleIndex), advanceCycle);
    }

    private void UpdatePlayerWidth(bool metricsVisible)
    {
        PlayerRoot.Width = _isVerticalLayout
            ? VerticalPlayerWidth
            : CalculateHorizontalPlayerWidth();
        PlayerRoot.Height = _isVerticalLayout
            ? CalculateVerticalPlayerHeight()
            : HorizontalPlayerHeight;
        VerticalPlayerContent.Height = PlayerRoot.Height;
        PositionOverTaskbar(force: true);
    }

    private string BuildMetricValue(SystemMetricsSnapshot sample, int selectedIndex)
    {
        if (_metricSettings.ShowSystemMemory)
        {
            if (selectedIndex-- == 0)
            {
                return $"MEM {sample.SystemMemoryPercent}%";
            }
        }

        if (_metricSettings.ShowSystemCpu)
        {
            if (selectedIndex-- == 0)
            {
                return $"CPU {(sample.SystemCpuPercent is int cpu ? $"{cpu}%" : "--%")}";
            }
        }

        if (_metricSettings.ShowSystemGpu)
        {
            if (selectedIndex-- == 0)
            {
                return $"GPU {(sample.SystemGpuPercent is int gpu ? $"{gpu}%" : "--%")}";
            }
        }

        if (_metricSettings.ShowProcessMemory)
        {
            var appMemory = sample.ProcessMemoryMegabytes < 1000
                ? $"{sample.ProcessMemoryMegabytes}M"
                : $"{sample.ProcessMemoryMegabytes / 1024d:0.0}G";
            return $"APP {appMemory}";
        }

        return string.Empty;
    }

    private void SetMetricText(string text, bool animate)
    {
        if (!animate || _metricSettings.LowGpuMode)
        {
            MetricsText.BeginAnimation(UIElement.OpacityProperty, null);
            MetricsText.Opacity = 1;
            MetricsText.Text = text;
            VerticalMetricsText.Opacity = 1;
            VerticalMetricsText.Text = text;
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90));
        fadeOut.Completed += (_, _) =>
        {
            MetricsText.Text = text;
            VerticalMetricsText.Text = text;
            MetricsText.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
            VerticalMetricsText.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        };
        MetricsText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        VerticalMetricsText.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90)));
    }

    private void ApplyMetricSettings()
    {
        ApplyOutputDeviceSettings();
        ApplyVolumeControlSettings();
        _metricCycleIndex = 0;
        _metricCycleTicks = 0;
        UpdateMetrics(advanceCycle: false);
        if (_metricSettings.LowGpuMode)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            SetExpanded(_isExpanded, animate: false);
            StopMarquees();
        }
        else
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            ScheduleMarqueeUpdate();
        }

        ApplyAudioMonitorSettings();
        _ = RefreshAutomaticPlacementAsync();
    }

    private void PositionFloatingWindow(bool force)
    {
        if (_windowHandle == nint.Zero || _taskbarHostService is null)
        {
            return;
        }

        var verticalLayout = _windowSettings.LayoutMode == PlayerLayoutMode.Vertical;
        ApplyPlayerLayout(verticalLayout);
        CollapseWhenPointerLeavesWindow();
        if (!_windowSettings.EdgeAutoCollapse &&
            (_floatingEdge != 0 || _expandedEdge != 0))
        {
            _edgeAnimationTimer.Stop();
            _edgeAnimationHasTarget = false;
            _floatingEdge = 0;
            _expandedEdge = 0;
            UpdateEdgeCollapseIndicator(visible: false);
            force = true;
        }
        if (_windowSettings.HideWhenNoMedia &&
            !_hasConnectedMedia &&
            !_windowSettings.AlwaysOnTop &&
            !_isMenuOpen)
        {
            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        if (!_windowSettings.AlwaysOnTop &&
            NativeMethods.ShouldHideForFullScreenApp(_windowHandle))
        {
            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        var left = _windowSettings.FloatingLeft ?? _floatingNormalLeft;
        var top = _windowSettings.FloatingTop ?? _floatingNormalTop;
        if ((!left.HasValue || !top.HasValue) &&
            NativeMethods.GetWindowRect(_windowHandle, out var currentRect))
        {
            left ??= currentRect.Left;
            top ??= currentRect.Top;
        }

        var monitor = left.HasValue && top.HasValue
            ? NativeMethods.MonitorFromPoint(
                new NativeMethods.Point { X = left.Value, Y = top.Value },
                2)
            : NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var desktopBounds = monitorInfo.WorkArea;
        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var layoutScale = CalculateFloatingLayoutScale(desktopBounds, scale);
        ApplyPlayerScale(layoutScale);
        var width = Math.Clamp(
            (int)Math.Ceiling(PlayerRoot.Width * layoutScale * scale),
            1,
            desktopBounds.Width);
        var height = Math.Clamp(
            (int)Math.Ceiling(PlayerRoot.Height * layoutScale * scale),
            1,
            desktopBounds.Height);
        Height = PlayerRoot.Height * layoutScale;
        left ??= desktopBounds.Left + 16;
        top ??= desktopBounds.Bottom - height - 16;
        if (_floatingEdge == 0)
        {
            var sizeChanged = _lastFloatingWidth > 0 &&
                (_lastFloatingWidth != width || _lastFloatingHeight != height);
            if (sizeChanged)
            {
                left = _expandedEdge switch
                {
                    1 => desktopBounds.Left,
                    2 => desktopBounds.Right - width,
                    _ => left
                };
                top = _expandedEdge switch
                {
                    3 => desktopBounds.Top,
                    4 => desktopBounds.Bottom - height,
                    _ => top
                };
                force = true;
            }

            left = Math.Clamp(left.Value, desktopBounds.Left, desktopBounds.Right - width);
            top = Math.Clamp(top.Value, desktopBounds.Top, desktopBounds.Bottom - height);
            _floatingNormalLeft = left;
            _floatingNormalTop = top;
            if (_expandedEdge != 0)
            {
                _windowSettings = _windowSettings with
                {
                    FloatingLeft = left,
                    FloatingTop = top
                };
                SaveWindowSettings(showError: false);
            }
        }
        else
        {
            left = _floatingEdge == 1
                ? desktopBounds.Left - width + EdgeVisiblePixels
                : _floatingEdge == 2
                    ? desktopBounds.Right - EdgeVisiblePixels
                    : left;
            top = _floatingEdge == 3
                ? desktopBounds.Top - height + EdgeVisiblePixels
                : _floatingEdge == 4
                    ? desktopBounds.Bottom - EdgeVisiblePixels
                    : top;
        }

        ConfigureFloatingPopupPlacement(
            desktopBounds,
            _floatingNormalLeft ?? left.Value,
            _floatingNormalTop ?? top.Value,
            width,
            height,
            verticalLayout);

        _lastFloatingWidth = width;
        _lastFloatingHeight = height;

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            force = true;
        }

        _taskbarHostService.SetFloating(true);
        Topmost = _windowSettings.AlwaysOnTop;
        if (!_edgeAnimationTimer.IsEnabled &&
            (force || _lastPositionLeft != left || _lastPositionTop != top))
        {
            _lastPositionLeft = left;
            _lastPositionTop = top;
            _taskbarHostService.Position(
                left.Value,
                top.Value,
                width,
                height,
                visible: true,
                topmost: _windowSettings.AlwaysOnTop);
        }

        RevealAfterPlacement();
    }

    private double CalculateFloatingLayoutScale(
        NativeMethods.Rect desktopBounds,
        double dpiScale)
    {
        var requestedScale = _windowSettings.DisplayScalePercent / 100d;
        var maximumWidthScale = desktopBounds.Width / (PlayerRoot.Width * dpiScale);
        var maximumHeightScale = desktopBounds.Height / (PlayerRoot.Height * dpiScale);
        return Math.Clamp(
            Math.Min(requestedScale, Math.Min(maximumWidthScale, maximumHeightScale)),
            0.1,
            1.25);
    }

    private void ConfigureFloatingPopupPlacement(
        NativeMethods.Rect desktopBounds,
        int left,
        int top,
        int width,
        int height,
        bool verticalLayout)
    {
        if (verticalLayout)
        {
            var openToRight = left + width / 2 <=
                desktopBounds.Left + desktopBounds.Width / 2;
            SetPopupPlacement(
                openToRight ? PlacementMode.Right : PlacementMode.Left,
                openToRight ? 7 : -7,
                0);
            return;
        }

        var openDownward = top + height / 2 <=
            desktopBounds.Top + desktopBounds.Height / 2;
        SetPopupPlacement(
            openDownward ? PlacementMode.Bottom : PlacementMode.Top,
            0,
            openDownward ? 7 : -7);
    }

    private void UpdateFloatingEdgeCollapse()
    {
        if (_windowSettings.HostMode != WindowHostMode.Floating ||
            !_windowSettings.EdgeAutoCollapse ||
            _windowHandle == nint.Zero ||
            _isDragging ||
            _isMenuOpen ||
            _edgeAnimationTimer.IsEnabled ||
            !NativeMethods.GetWindowRect(_windowHandle, out var rect) ||
            !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        const int edgeTolerance = 10;
        var desktopBounds = info.WorkArea;
        if (_floatingEdge != 0)
        {
            var normalLeft = _floatingNormalLeft ?? rect.Left;
            var normalTop = _floatingNormalTop ?? rect.Top;
            var nearHorizontalSpan = cursor.X >= normalLeft - EdgeActivationSpanPadding &&
                cursor.X < normalLeft + rect.Width + EdgeActivationSpanPadding;
            var nearVerticalSpan = cursor.Y >= normalTop - EdgeActivationSpanPadding &&
                cursor.Y < normalTop + rect.Height + EdgeActivationSpanPadding;
            var nearEdge = _floatingEdge switch
            {
                1 => cursor.X >= desktopBounds.Left - EdgeActivationDistance &&
                    cursor.X <= desktopBounds.Left + EdgeActivationDistance && nearVerticalSpan,
                2 => cursor.X >= desktopBounds.Right - EdgeActivationDistance &&
                    cursor.X <= desktopBounds.Right + EdgeActivationDistance && nearVerticalSpan,
                3 => cursor.Y >= desktopBounds.Top - EdgeActivationDistance &&
                    cursor.Y <= desktopBounds.Top + EdgeActivationDistance && nearHorizontalSpan,
                _ => cursor.Y >= desktopBounds.Bottom - EdgeActivationDistance &&
                    cursor.Y <= desktopBounds.Bottom + EdgeActivationDistance && nearHorizontalSpan
            };
            if (nearEdge)
            {
                StartEdgeAnimation(expanding: true, rect, desktopBounds);
            }

            return;
        }

        if (_expandedEdge != 0)
        {
            const int expandedProximity = 64;
            var nearExpandedWindow = cursor.X >= rect.Left - expandedProximity &&
                cursor.X < rect.Right + expandedProximity &&
                cursor.Y >= rect.Top - expandedProximity &&
                cursor.Y < rect.Bottom + expandedProximity;
            if (nearExpandedWindow)
            {
                return;
            }

            _floatingEdge = _expandedEdge;
            _expandedEdge = 0;
            StartEdgeAnimation(expanding: false, rect, desktopBounds);
            return;
        }

        var touchesLeft = rect.Left <= desktopBounds.Left + edgeTolerance;
        var touchesRight = rect.Right >= desktopBounds.Right - edgeTolerance;
        var touchesTop = rect.Top <= desktopBounds.Top + edgeTolerance;
        var touchesBottom = rect.Bottom >= desktopBounds.Bottom - edgeTolerance;
        var edge = _isVerticalLayout
            ? touchesLeft ? 1 :
                touchesRight ? 2 :
                touchesTop ? 3 :
                touchesBottom ? 4 : 0
            : touchesTop ? 3 :
                touchesBottom ? 4 :
                touchesLeft ? 1 :
                touchesRight ? 2 : 0;
        if (edge == 0 ||
            (cursor.X >= rect.Left && cursor.X < rect.Right &&
                cursor.Y >= rect.Top && cursor.Y < rect.Bottom))
        {
            return;
        }

        _floatingNormalLeft = rect.Left;
        _floatingNormalTop = rect.Top;
        _windowSettings = _windowSettings with
        {
            FloatingLeft = rect.Left,
            FloatingTop = rect.Top
        };
        _floatingEdge = edge;
        StartEdgeAnimation(expanding: false, rect, desktopBounds);
    }

    private void OnEdgeHoverTimerTick(object? sender, EventArgs e)
    {
        UpdateFloatingEdgeCollapse();
    }

    private void StartEdgeAnimation(
        bool expanding,
        NativeMethods.Rect currentRect,
        NativeMethods.Rect desktopBounds)
    {
        if (_taskbarHostService is null || _floatingEdge == 0)
        {
            return;
        }

        var normalLeft = Math.Clamp(
            _floatingNormalLeft ?? currentRect.Left,
            desktopBounds.Left,
            desktopBounds.Right - currentRect.Width);
        var normalTop = Math.Clamp(
            _floatingNormalTop ?? currentRect.Top,
            desktopBounds.Top,
            desktopBounds.Bottom - currentRect.Height);
        var collapsedLeft = _floatingEdge == 1
            ? desktopBounds.Left - currentRect.Width + EdgeVisiblePixels
            : _floatingEdge == 2
                ? desktopBounds.Right - EdgeVisiblePixels
                : normalLeft;
        var collapsedTop = _floatingEdge == 3
            ? desktopBounds.Top - currentRect.Height + EdgeVisiblePixels
            : _floatingEdge == 4
                ? desktopBounds.Bottom - EdgeVisiblePixels
                : normalTop;

        _edgeAnimationFrom = currentRect;
        _edgeAnimationTo = new NativeMethods.Rect
        {
            Left = expanding ? normalLeft : collapsedLeft,
            Top = expanding ? normalTop : collapsedTop,
            Right = (expanding ? normalLeft : collapsedLeft) + currentRect.Width,
            Bottom = (expanding ? normalTop : collapsedTop) + currentRect.Height
        };
        _edgeAnimationStarted = DateTime.UtcNow;
        _edgeAnimationExpanding = expanding;
        _edgeAnimationHasTarget = true;
        if (expanding)
        {
            SetPlayerContentVisibility(visible: true);
        }
        UpdateEdgeCollapseIndicator(visible: !expanding);
        _edgeAnimationTimer.Stop();
        _edgeAnimationTimer.Start();
    }

    private void OnEdgeAnimationTick(object? sender, EventArgs e)
    {
        if (!_edgeAnimationHasTarget || _taskbarHostService is null)
        {
            _edgeAnimationTimer.Stop();
            return;
        }

        var elapsed = (DateTime.UtcNow - _edgeAnimationStarted).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / EdgeAnimationDurationMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var left = (int)Math.Round(_edgeAnimationFrom.Left +
            (_edgeAnimationTo.Left - _edgeAnimationFrom.Left) * eased);
        var top = (int)Math.Round(_edgeAnimationFrom.Top +
            (_edgeAnimationTo.Top - _edgeAnimationFrom.Top) * eased);
        _taskbarHostService.Position(
            left,
            top,
            _edgeAnimationTo.Width,
            _edgeAnimationTo.Height,
            visible: true,
            topmost: _windowSettings.AlwaysOnTop,
            refresh: false);
        _lastPositionLeft = left;
        _lastPositionTop = top;

        if (progress < 1)
        {
            return;
        }

        _edgeAnimationTimer.Stop();
        _edgeAnimationHasTarget = false;
        _taskbarHostService.Redraw();
        if (_edgeAnimationExpanding)
        {
            _expandedEdge = _floatingEdge;
            _floatingEdge = 0;
            UpdateEdgeCollapseIndicator(visible: false);
        }
        else
        {
            _expandedEdge = 0;
            UpdateEdgeCollapseIndicator(visible: true);
            SetPlayerContentVisibility(visible: false);
        }
    }

    private void UpdateEdgeCollapseIndicator(bool visible)
    {
        EdgeCollapseIndicator.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!visible)
        {
            SetPlayerContentVisibility(visible: true);
            return;
        }

        var horizontalEdge = _floatingEdge is 3 or 4;
        EdgeCollapseIndicator.Width = horizontalEdge ? 56 : 4;
        EdgeCollapseIndicator.Height = horizontalEdge
            ? 4
            : _isVerticalLayout ? 72 : 38;
        EdgeCollapseIndicator.HorizontalAlignment = _floatingEdge == 1
            ? HorizontalAlignment.Right
            : _floatingEdge == 2
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
        EdgeCollapseIndicator.VerticalAlignment = _floatingEdge == 3
            ? VerticalAlignment.Bottom
            : _floatingEdge == 4
                ? VerticalAlignment.Top
                : VerticalAlignment.Center;
    }

    private void ApplyWindowSettings()
    {
        _taskbarHostService?.SetFloating(_windowSettings.HostMode == WindowHostMode.Floating);
        Topmost = _windowSettings.AlwaysOnTop;
        if (!_windowSettings.AutoCollapse)
        {
            SetExpanded(expanded: true, animate: true);
        }
        if (_windowSettings.AlwaysOnTop)
        {
            Visibility = Visibility.Visible;
        }
    }

    private void SaveWindowSettings(bool showError = true)
    {
        try
        {
            _settingsCoordinator.SynchronizeWindow(_windowSettings);
        }
        catch (Exception exception)
        {
            if (!showError)
            {
                return;
            }

            MessageBox.Show(
                exception.Message,
                "无法保存窗口设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyOutputDeviceSettings()
    {
        var enabled = _metricSettings.OutputDeviceSwitcherEnabled;
        OutputDeviceHost.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalOutputDeviceHost.Visibility = OutputDeviceHost.Visibility;
        UpdatePlayerWidth(_metricSettings.SelectedCount > 0);
        if (enabled)
        {
            return;
        }

        OutputDevicePopup.IsOpen = false;
        OutputDeviceStatusPopup.IsOpen = false;
        _outputDeviceApplyTimer.Stop();
        _pendingOutputDeviceId = null;
        _pendingOutputDeviceWheelSteps = 0;
        _outputDeviceWheelUsesCompactStatus = false;
        _outputDevices = [];
        OutputDeviceList.ItemsSource = null;
    }

    private void ApplyVolumeControlSettings()
    {
        var enabled = _metricSettings.VolumeControlEnabled;
        VolumeControlHost.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalVolumeControlHost.Visibility = VolumeControlHost.Visibility;
        UpdatePlayerWidth(_metricSettings.SelectedCount > 0);
        if (enabled)
        {
            _ = RefreshCurrentMediaVolumeAsync(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName);
            return;
        }

        VolumeControlPopup.IsOpen = false;
        VolumeStatusPopup.IsOpen = false;
        _volumeApplyTimer.Stop();
        _volumePopupCloseTimer.Stop();
        _pendingVolumePercent = null;
        _pendingVolumeWheelSteps = 0;
        _volumeWheelUsesCompactStatus = false;
        _currentApplicationVolume = null;
    }

    private void ApplyAudioMonitorSettings()
    {
        if (_metricSettings.AudioMonitorEnabled)
        {
            _audioMonitorService ??= new AudioMonitorService();
            if (!_audioMonitorTimer.IsEnabled)
            {
                _audioMonitorTimer.Start();
            }
        }
        else
        {
            _audioMonitorTimer.Stop();
            _audioMonitorService?.Dispose();
            _audioMonitorService = null;
            Array.Clear(_audioSpectrum);
            Array.Clear(_smoothedAudioSpectrum);
            SetAudioBarHeights();
        }

        AudioVisualizerHost.Visibility = _metricSettings.AudioMonitorEnabled && !_isExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetExpanded(_isExpanded, animate: false);
    }

    private void OnAudioMonitorTimerTick(object? sender, EventArgs e)
    {
        if (!_metricSettings.AudioMonitorEnabled || _audioMonitorService is null)
        {
            return;
        }

        _audioMonitorService.GetSpectrum(_audioSpectrum);

        if (!_isExpanded)
        {
            SetAudioBarHeights();
        }
    }

    private void SetAudioBarHeights()
    {
        for (var index = 0; index < _audioBars.Length; index++)
        {
            var target = _audioSpectrum[index];
            var current = _smoothedAudioSpectrum[index];
            var response = target > current ? 0.72f : 0.18f;
            current += (target - current) * response;
            if (current < 0.008f)
            {
                current = 0;
            }

            _smoothedAudioSpectrum[index] = current;
            _audioBars[index].Height = Math.Clamp(3 + Math.Sqrt(current) * 32, 3, 35);
        }
    }

    private async Task RefreshOutputDevicesAsync(string? preferredId = null)
    {
        try
        {
            var devices = await _audioDeviceService.GetRenderDevicesAsync()
                .WaitAsync(TimeSpan.FromSeconds(2));
            _outputDevices = devices;
            OutputDeviceList.ItemsSource = devices;

            var selected = devices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(preferredId) &&
                    string.Equals(device.Id, preferredId, StringComparison.OrdinalIgnoreCase)) ??
                devices.FirstOrDefault(device => device.IsDefault) ??
                devices.FirstOrDefault();
            OutputDeviceList.SelectedItem = selected;

            var current = devices.FirstOrDefault(device => device.IsDefault) ?? selected;
            OutputDeviceCurrentText.Text = current?.DisplayName ?? "未找到设备";
            if (_showingOutputDeviceHoverStatus &&
                _pendingOutputDeviceId is null)
            {
                OutputDeviceStatusText.Text = current is null
                    ? "未找到可用输出设备"
                    : $"输出设备：{current.DisplayName}";
            }
        }
        catch (Exception exception)
        {
            _outputDevices = [];
            OutputDeviceList.ItemsSource = null;
            OutputDeviceCurrentText.Text = "读取失败";
            if (_showingOutputDeviceHoverStatus)
            {
                OutputDeviceStatusText.Text = $"无法读取输出设备：{exception.Message}";
            }
        }
    }

    private void OutputDeviceHost_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled ||
            OutputDevicePopup.IsOpen ||
            _pendingOutputDeviceId is not null)
        {
            return;
        }

        _showingOutputDeviceHoverStatus = true;
        var current = _outputDevices.FirstOrDefault(device => device.IsDefault);
        OutputDeviceStatusText.Text = current is null
            ? "输出设备"
            : $"输出设备：{current.DisplayName}";
        OutputDeviceStatusPopup.IsOpen = true;
        if (_outputDevices.Count == 0)
        {
            _ = RefreshOutputDevicesAsync();
        }
    }

    private void OutputDeviceHost_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showingOutputDeviceHoverStatus = false;
        if (_pendingOutputDeviceId is null)
        {
            OutputDeviceStatusPopup.IsOpen = false;
        }
    }

    private async void OutputDeviceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled)
        {
            return;
        }

        OutputDeviceStatusPopup.IsOpen = false;
        if (OutputDevicePopup.IsOpen)
        {
            OutputDevicePopup.IsOpen = false;
            return;
        }

        await RefreshOutputDevicesAsync(_pendingOutputDeviceId);
        if (_outputDevices.Count > 0)
        {
            OutputDevicePopup.IsOpen = true;
        }
    }

    private async void OutputDeviceList_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(
            OutputDeviceList,
            e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is not AudioDeviceOption device)
        {
            return;
        }

        e.Handled = true;
        _outputDeviceApplyTimer.Stop();
        OutputDeviceStatusPopup.IsOpen = false;
        _pendingOutputDeviceId = null;
        _pendingOutputDeviceWheelSteps = 0;
        _outputDeviceWheelUsesCompactStatus = false;
        OutputDeviceList.SelectedItem = device;
        if (await SwitchOutputDeviceAsync(device))
        {
            OutputDevicePopup.IsOpen = false;
        }
    }

    private void OutputDevicePopup_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: false);
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void OutputDeviceHost_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: true);
    }

    private void QueueOutputDeviceFromWheel(int delta, bool useCompactStatus)
    {
        if (!_metricSettings.OutputDeviceSwitcherEnabled || delta == 0)
        {
            return;
        }

        _outputDeviceWheelUsesCompactStatus = useCompactStatus;
        var stepCount = GetWheelStepCount(delta);
        _pendingOutputDeviceWheelSteps += delta > 0 ? -stepCount : stepCount;
        _ = ProcessOutputDeviceWheelAsync();
    }

    private async Task ProcessOutputDeviceWheelAsync()
    {
        if (_isProcessingOutputDeviceWheel)
        {
            return;
        }

        _isProcessingOutputDeviceWheel = true;
        try
        {
            if (_outputDevices.Count == 0)
            {
                await RefreshOutputDevicesAsync();
            }

            if (!_metricSettings.OutputDeviceSwitcherEnabled ||
                _outputDevices.Count == 0)
            {
                _pendingOutputDeviceWheelSteps = 0;
                if (_outputDeviceWheelUsesCompactStatus)
                {
                    OutputDeviceStatusText.Text = "暂无可用输出设备";
                    OutputDeviceStatusPopup.IsOpen = true;
                    _outputDeviceApplyTimer.Stop();
                    _outputDeviceApplyTimer.Start();
                }

                return;
            }

            while (_pendingOutputDeviceWheelSteps != 0)
            {
                var wheelSteps = _pendingOutputDeviceWheelSteps;
                _pendingOutputDeviceWheelSteps = 0;
                var currentIndex = -1;
                if (!string.IsNullOrWhiteSpace(_pendingOutputDeviceId))
                {
                    currentIndex = FindOutputDeviceIndex(_pendingOutputDeviceId);
                }

                if (currentIndex < 0)
                {
                    currentIndex = _outputDevices
                        .Select((device, index) => (device, index))
                        .Where(pair => pair.device.IsDefault)
                        .Select(pair => pair.index)
                        .DefaultIfEmpty(0)
                        .First();
                }

                var nextIndex = (currentIndex + wheelSteps) % _outputDevices.Count;
                if (nextIndex < 0)
                {
                    nextIndex += _outputDevices.Count;
                }

                var nextDevice = _outputDevices[nextIndex];
                _pendingOutputDeviceId = nextDevice.Id;
                OutputDeviceList.SelectedItem = nextDevice;
                OutputDeviceCurrentText.Text = nextDevice.DisplayName;
                if (_outputDeviceWheelUsesCompactStatus)
                {
                    OutputDeviceStatusText.Text = $"输出设备：{nextDevice.DisplayName}";
                    OutputDeviceStatusPopup.IsOpen = true;
                    OutputDevicePopup.IsOpen = false;
                }
                else
                {
                    OutputDeviceStatusPopup.IsOpen = false;
                    OutputDevicePopup.IsOpen = true;
                    OutputDeviceList.ScrollIntoView(nextDevice);
                }

                _outputDeviceApplyTimer.Stop();
                _outputDeviceApplyTimer.Start();
            }
        }
        finally
        {
            _isProcessingOutputDeviceWheel = false;
            if (_pendingOutputDeviceWheelSteps != 0 &&
                _metricSettings.OutputDeviceSwitcherEnabled)
            {
                _ = ProcessOutputDeviceWheelAsync();
            }
        }
    }

    private static int GetWheelStepCount(int delta)
    {
        return Math.Max(
            1,
            (Math.Abs(delta) + MouseWheelDelta - 1) / MouseWheelDelta);
    }

    private int FindOutputDeviceIndex(string deviceId)
    {
        for (var index = 0; index < _outputDevices.Count; index++)
        {
            if (string.Equals(
                    _outputDevices[index].Id,
                    deviceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private async void OnOutputDeviceApplyTimerTick(object? sender, EventArgs e)
    {
        _outputDeviceApplyTimer.Stop();
        OutputDevicePopup.IsOpen = false;
        OutputDeviceStatusPopup.IsOpen = false;
        _outputDeviceWheelUsesCompactStatus = false;
        var deviceId = _pendingOutputDeviceId;
        _pendingOutputDeviceId = null;
        var device = _outputDevices.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
        {
            await SwitchOutputDeviceAsync(device);
        }
    }

    private async Task<bool> SwitchOutputDeviceAsync(AudioDeviceOption device)
    {
        try
        {
            await Task.Run(() => _audioDeviceService.SetDefaultRenderDevice(device.PolicyId))
                .WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(180);
            if (_metricSettings.AudioMonitorEnabled)
            {
                _audioMonitorService?.Dispose();
                _audioMonitorService = null;
                ApplyAudioMonitorSettings();
            }

            await RefreshOutputDevicesAsync(device.Id);
            return true;
        }
        catch (Exception exception)
        {
            OutputDeviceCurrentText.Text = "切换失败";
            OutputDeviceStatusText.Text = $"无法切换输出设备：{exception.Message}";
            OutputDeviceStatusPopup.IsOpen = true;
            _outputDeviceApplyTimer.Stop();
            _outputDeviceApplyTimer.Start();
            return false;
        }
    }

    private void AudioControlPopup_OnOpened(object? sender, EventArgs e)
    {
        UpdateMouseHookState();
    }

    private void OutputDevicePopup_OnClosed(object? sender, EventArgs e)
    {
        UpdateMouseHookState();
        ScheduleCollapse();
    }

    private async Task RefreshCurrentMediaVolumeAsync(
        string? sourceId,
        string? sourceName)
    {
        if (!_metricSettings.VolumeControlEnabled)
        {
            return;
        }

        var version = Interlocked.Increment(ref _volumeRefreshVersion);
        try
        {
            var snapshot = await Task.Run(() =>
                    _applicationVolumeService.GetCurrentMediaVolume(sourceId, sourceName))
                .WaitAsync(TimeSpan.FromSeconds(2));
            if (version != _volumeRefreshVersion ||
                !_metricSettings.VolumeControlEnabled)
            {
                return;
            }

            SetCurrentApplicationVolume(snapshot);
        }
        catch (Exception exception)
        {
            if (version != _volumeRefreshVersion)
            {
                return;
            }

            SetCurrentApplicationVolume(null);
            if (_showingVolumeHoverStatus || VolumeStatusPopup.IsOpen)
            {
                VolumeStatusText.Text = $"无法读取当前媒体音量：{exception.Message}";
            }
        }
    }

    private void SetCurrentApplicationVolume(ApplicationVolumeSnapshot? snapshot)
    {
        _currentApplicationVolume = snapshot;
        _isUpdatingVolumeSlider = true;
        try
        {
            CurrentMediaVolumeSlider.IsEnabled = snapshot is not null;
            CurrentMediaVolumeSlider.Value = snapshot?.VolumePercent ?? 0;
            var selectedSourceName = _mediaSessionService.SelectedSourceName;
            VolumeMediaNameText.Text = snapshot?.DisplayName ??
                (string.IsNullOrWhiteSpace(selectedSourceName)
                    ? "当前媒体"
                    : selectedSourceName);
            VolumePercentText.Text = snapshot is null
                ? "暂无"
                : $"{snapshot.VolumePercent}%";
            if (VolumeStatusPopup.IsOpen || _showingVolumeHoverStatus)
            {
                UpdateVolumeStatusText();
            }
        }
        finally
        {
            _isUpdatingVolumeSlider = false;
        }
    }

    private async void VolumeControlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_metricSettings.VolumeControlEnabled)
        {
            return;
        }

        VolumeStatusPopup.IsOpen = false;
        _volumePopupCloseTimer.Stop();
        if (VolumeControlPopup.IsOpen)
        {
            VolumeControlPopup.IsOpen = false;
            return;
        }

        await RefreshCurrentMediaVolumeAsync(
            _mediaSessionService.SelectedSourceId,
            _mediaSessionService.SelectedSourceName);
        VolumeControlPopup.IsOpen = true;
    }

    private void VolumeControlHost_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_metricSettings.VolumeControlEnabled || VolumeControlPopup.IsOpen)
        {
            return;
        }

        _showingVolumeHoverStatus = true;
        UpdateVolumeStatusText();
        VolumeStatusPopup.IsOpen = true;
        if (_currentApplicationVolume is null)
        {
            _ = RefreshCurrentMediaVolumeAsync(
                _mediaSessionService.SelectedSourceId,
                _mediaSessionService.SelectedSourceName);
        }
    }

    private void VolumeControlHost_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _showingVolumeHoverStatus = false;
        if (!_volumePopupCloseTimer.IsEnabled)
        {
            VolumeStatusPopup.IsOpen = false;
        }
    }

    private void VolumeControlHost_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueVolumeWheel(e.Delta, useCompactStatus: true);
    }

    private void VolumeControlPopup_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueVolumeWheel(e.Delta, useCompactStatus: false);
    }

    private void QueueVolumeWheel(int delta, bool useCompactStatus)
    {
        if (!_metricSettings.VolumeControlEnabled || delta == 0)
        {
            return;
        }

        _volumeWheelUsesCompactStatus = useCompactStatus;
        var stepCount = GetWheelStepCount(delta);
        _pendingVolumeWheelSteps += delta > 0 ? stepCount : -stepCount;
        _ = ProcessVolumeWheelAsync();
    }

    private async Task ProcessVolumeWheelAsync()
    {
        if (_isProcessingVolumeWheel)
        {
            return;
        }

        _isProcessingVolumeWheel = true;
        try
        {
            if (_currentApplicationVolume is null)
            {
                await RefreshCurrentMediaVolumeAsync(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName);
            }

            if (!_metricSettings.VolumeControlEnabled)
            {
                _pendingVolumeWheelSteps = 0;
                return;
            }

            var wheelSteps = _pendingVolumeWheelSteps;
            _pendingVolumeWheelSteps = 0;
            if (_currentApplicationVolume is null)
            {
                ShowVolumeWheelFeedback(_volumeWheelUsesCompactStatus);
                return;
            }

            var nextVolume = Math.Clamp(
                _currentApplicationVolume.VolumePercent +
                    wheelSteps * VolumeWheelStepPercent,
                0,
                100);
            _currentApplicationVolume = _currentApplicationVolume with
            {
                VolumePercent = nextVolume,
                IsMuted = false
            };
            SetVolumeSliderValue(nextVolume);
            QueueVolumeApply(nextVolume);
            ShowVolumeWheelFeedback(_volumeWheelUsesCompactStatus);
        }
        finally
        {
            _isProcessingVolumeWheel = false;
            if (_pendingVolumeWheelSteps != 0 &&
                _metricSettings.VolumeControlEnabled)
            {
                _ = ProcessVolumeWheelAsync();
            }
        }
    }

    private void SetVolumeSliderValue(int volumePercent)
    {
        _isUpdatingVolumeSlider = true;
        try
        {
            CurrentMediaVolumeSlider.Value = volumePercent;
            VolumePercentText.Text = $"{volumePercent}%";
            UpdateVolumeStatusText();
        }
        finally
        {
            _isUpdatingVolumeSlider = false;
        }
    }

    private void ShowVolumeWheelFeedback(bool useCompactStatus)
    {
        if (useCompactStatus)
        {
            UpdateVolumeStatusText();
            VolumeStatusPopup.IsOpen = true;
            VolumeControlPopup.IsOpen = false;
        }
        else
        {
            VolumeStatusPopup.IsOpen = false;
            VolumeControlPopup.IsOpen = true;
        }

        ScheduleVolumeInteractionClose();
    }

    private void UpdateVolumeStatusText()
    {
        var sourceName = _currentApplicationVolume?.DisplayName;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = _mediaSessionService.SelectedSourceName;
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "当前媒体";
        }

        var volume = _currentApplicationVolume is null
            ? "暂无"
            : $"{_currentApplicationVolume.VolumePercent}%";
        VolumeStatusText.Text = $"{sourceName}：{volume}";
    }

    private void ScheduleVolumeInteractionClose()
    {
        _volumePopupCloseTimer.Stop();
        _volumePopupCloseTimer.Start();
    }

    private void CurrentMediaVolumeSlider_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Slider slider ||
            !slider.IsEnabled ||
            e.LeftButton != MouseButtonState.Pressed ||
            FindVisualAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var track = FindVisualDescendant<Track>(slider);
        if (track is null || track.ActualHeight <= 0)
        {
            return;
        }

        var thumbHeight = track.Thumb?.ActualHeight ?? 0;
        var usableHeight = Math.Max(1, track.ActualHeight - thumbHeight);
        var position = Math.Clamp(
            e.GetPosition(track).Y - thumbHeight / 2,
            0,
            usableHeight);
        var fraction = 1 - position / usableHeight;
        if (track.IsDirectionReversed)
        {
            fraction = 1 - fraction;
        }

        slider.Value = slider.Minimum + fraction * (slider.Maximum - slider.Minimum);
        e.Handled = true;
    }

    private void CurrentMediaVolumeSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingVolumeSlider || _currentApplicationVolume is null)
        {
            return;
        }

        var volumePercent = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        _currentApplicationVolume = _currentApplicationVolume with
        {
            VolumePercent = volumePercent,
            IsMuted = false
        };
        VolumePercentText.Text = $"{volumePercent}%";
        QueueVolumeApply(volumePercent);
        VolumeStatusPopup.IsOpen = false;
        ScheduleVolumeInteractionClose();
    }

    private void QueueVolumeApply(int volumePercent)
    {
        _pendingVolumePercent = volumePercent;
        _volumeApplyTimer.Stop();
        _volumeApplyTimer.Start();
    }

    private async void OnVolumeApplyTimerTick(object? sender, EventArgs e)
    {
        _volumeApplyTimer.Stop();
        var volumePercent = _pendingVolumePercent;
        var application = _currentApplicationVolume;
        _pendingVolumePercent = null;
        if (!volumePercent.HasValue || application is null)
        {
            return;
        }

        try
        {
            var changed = await Task.Run(() =>
                    _applicationVolumeService.SetApplicationVolume(
                        application.ProcessName,
                        volumePercent.Value))
                .WaitAsync(TimeSpan.FromSeconds(2));
            if (!changed)
            {
                await RefreshCurrentMediaVolumeAsync(
                    _mediaSessionService.SelectedSourceId,
                    _mediaSessionService.SelectedSourceName);
            }
        }
        catch (Exception exception)
        {
            VolumeStatusText.Text = $"无法调节媒体音量：{exception.Message}";
            VolumeStatusPopup.IsOpen = true;
        }
    }

    private void OnVolumePopupCloseTimerTick(object? sender, EventArgs e)
    {
        _volumePopupCloseTimer.Stop();
        VolumeStatusPopup.IsOpen = false;
        VolumeControlPopup.IsOpen = false;
        _volumeWheelUsesCompactStatus = false;
    }

    private void VolumeControlPopup_OnClosed(object? sender, EventArgs e)
    {
        if (!VolumeStatusPopup.IsOpen)
        {
            _volumePopupCloseTimer.Stop();
        }

        UpdateMouseHookState();
        ScheduleCollapse();
    }

    private void ApplyPlacementSettings()
    {
        var taskbarMode = _windowSettings.HostMode == WindowHostMode.Taskbar;
        var automaticPlacementActive = taskbarMode &&
            !_isVerticalLayout &&
            _placementSettings.AutomaticPlacement;
        var positionLockedActive = _isVerticalLayout
            ? _placementSettings.VerticalPositionLocked
            : _placementSettings.PositionLocked;

        var canDrag = _windowSettings.HostMode == WindowHostMode.Floating ||
            (!automaticPlacementActive && !positionLockedActive);
        var cursor = canDrag ? Cursors.SizeAll : Cursors.Hand;
        ArtworkHost.Cursor = cursor;
        InfoHost.Cursor = cursor;
        VerticalArtworkHost.Cursor = cursor;
        VerticalInfoHost.Cursor = cursor;
        VerticalTitleText.Cursor = cursor;
        VerticalArtistText.Cursor = cursor;
    }

    private int GetCurrentOffsetDip()
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar)
        {
            return _placementSettings.ManualOffsetDip;
        }

        if (!TryGetTaskbarBounds(out var bounds) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return _placementSettings.ManualOffsetDip;
        }

        var taskbarRect = bounds.ScreenBounds;
        var scale = bounds.Scale;
        return Math.Max(
            0,
            (int)Math.Round(
                ((_isVerticalLayout ? windowRect.Top : windowRect.Left) -
                    (_isVerticalLayout ? taskbarRect.Top : taskbarRect.Left)) / scale));
    }

    private void SavePlacementSettings(bool showError = true)
    {
        try
        {
            _settingsCoordinator.SynchronizePlacement(_placementSettings);
        }
        catch (Exception exception)
        {
            if (!showError)
            {
                return;
            }

            MessageBox.Show(
                exception.Message,
                "无法保存位置设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void PlayerRoot_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if ((_windowSettings.HostMode == WindowHostMode.Taskbar &&
                (_isVerticalLayout
                    ? _placementSettings.VerticalPositionLocked
                    : _placementSettings.AutomaticPlacement ||
                        _placementSettings.PositionLocked)) ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PlayerRoot);
        var isDragArea = _isVerticalLayout
            ? position.Y <= VerticalArtworkAreaHeight ||
                (!_isExpanded && position.Y <= VerticalBaseHeight)
            : position.X <= 44 + InfoHost.ActualWidth;
        if (!isDragArea ||
            !NativeMethods.GetCursorPos(out _dragStartCursor) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return;
        }

        _dragStartWindowLeft = windowRect.Left;
        _dragStartWindowTop = windowRect.Top;
        if (_windowSettings.HostMode == WindowHostMode.Floating)
        {
            _edgeAnimationTimer.Stop();
            _edgeAnimationHasTarget = false;
            _floatingEdge = 0;
            _expandedEdge = 0;
            UpdateEdgeCollapseIndicator(visible: false);
        }
        _dragMoved = false;
        _isDragging = true;
        Mouse.Capture(PlayerRoot);
        e.Handled = true;
    }

    private async void PlayerRoot_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (OutputDevicePopup.IsOpen)
        {
            e.Handled = true;
            QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: false);
            return;
        }

        if (VolumeControlPopup.IsOpen)
        {
            e.Handled = true;
            QueueVolumeWheel(e.Delta, useCompactStatus: false);
            return;
        }

        var mediaPosition = e.GetPosition(PlayerRoot);
        if ((!_isVerticalLayout && mediaPosition.X >= MediaSwitchAreaWidth) ||
            (_isVerticalLayout &&
                (mediaPosition.Y > VerticalBaseHeight || _isExpanded)))
        {
            return;
        }

        var hasSelectedAvailableSession = _mediaSessions.Any(session =>
            session.IsSelected);
        if (_mediaSessions.Count == 0 ||
            (_mediaSessions.Count == 1 && hasSelectedAvailableSession))
        {
            return;
        }

        e.Handled = true;
        await RunMediaCommandAsync(
            e.Delta > 0
                ? _mediaSessionService.SelectPreviousSessionAsync
                : _mediaSessionService.SelectNextSessionAsync);
    }

    private void PlayerRoot_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed ||
            !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var deltaX = cursor.X - _dragStartCursor.X;
        var deltaY = cursor.Y - _dragStartCursor.Y;
        _dragMoved |= Math.Abs(deltaX) >= 3 || Math.Abs(deltaY) >= 3;

        if (_windowSettings.HostMode == WindowHostMode.Floating)
        {
            _floatingEdge = 0;
            _floatingNormalLeft = _dragStartWindowLeft + deltaX;
            _floatingNormalTop = _dragStartWindowTop + deltaY;
            _windowSettings = _windowSettings with
            {
                FloatingLeft = _floatingNormalLeft,
                FloatingTop = _floatingNormalTop
            };
            PositionOverTaskbar(force: true);
            e.Handled = true;
            return;
        }

        if (TryGetTaskbarBounds(out var bounds))
        {
            var taskbarRect = bounds.ScreenBounds;
            var scale = bounds.Scale;
            if (_isVerticalLayout)
            {
                var margin = (int)Math.Round(VerticalMarginAt96Dpi * scale);
                var playerHeight = (int)Math.Ceiling(
                    PlayerRoot.Height * PlayerScaleTransform.ScaleY * scale);
                var top = Math.Clamp(
                    _dragStartWindowTop + deltaY,
                    taskbarRect.Top + margin,
                    Math.Max(
                        taskbarRect.Top + margin,
                        taskbarRect.Bottom - margin - playerHeight));
                _placementSettings = _placementSettings with
                {
                    ManualVerticalOffsetDip = (int)Math.Round(
                        (top - taskbarRect.Top) / scale)
                };
            }
            else
            {
                var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
                var playerWidth = (int)Math.Ceiling(
                    PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
                var left = Math.Clamp(
                    _dragStartWindowLeft + deltaX,
                    taskbarRect.Left + margin,
                    Math.Max(
                        taskbarRect.Left + margin,
                        taskbarRect.Right - margin - playerWidth));
                _placementSettings = _placementSettings with
                {
                    ManualOffsetDip = (int)Math.Round(
                        (left - taskbarRect.Left) / scale)
                };
            }
            PositionOverTaskbar(force: true);
        }

        e.Handled = true;
    }

    private void PlayerRoot_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Mouse.Capture(null);
        if (_dragMoved)
        {
            if (_windowSettings.HostMode == WindowHostMode.Floating)
            {
                _windowSettings = _windowSettings with
                {
                    FloatingLeft = _floatingNormalLeft,
                    FloatingTop = _floatingNormalTop
                };
                SaveWindowSettings();
            }
            else
            {
                SavePlacementSettings();
            }
        }
        else
        {
            ShowSelectedMediaSource();
        }

        e.Handled = true;
    }

    private void PlayerMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = true;
        if (_windowSettings.HideWhenNoMedia && Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            PositionOverTaskbar(force: true);
        }
        UpdateMouseHookState();
        SetExpanded(expanded: true, animate: true);
        StartupMenuItem.IsChecked = _settingsCoordinator.Current.StartupEnabled;
    }

    private void PlayerMenu_OnOpening(object sender, ContextMenuEventArgs e)
    {
        PrepareContextMenuWindow();
    }

    private void PlayerMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = false;
        UpdateMouseHookState();
        ScheduleCollapse();
        if (_windowSettings.HideWhenNoMedia && !_hasConnectedMedia)
        {
            PositionOverTaskbar(force: true);
        }
    }

    private void MouseHook_OnMouseButtonPressed(NativeMethods.Point point)
    {
        if (!HasOpenInteractiveOverlay() || IsPointInsideApplicationWindow(point))
        {
            return;
        }

        PlayerMenu.IsOpen = false;
        OutputDevicePopup.IsOpen = false;
        VolumeControlPopup.IsOpen = false;
    }

    private bool HasOpenInteractiveOverlay()
    {
        return _isMenuOpen || OutputDevicePopup.IsOpen || VolumeControlPopup.IsOpen;
    }

    private void UpdateMouseHookState()
    {
        if (HasOpenInteractiveOverlay())
        {
            _mouseHookService.Start();
        }
        else
        {
            _mouseHookService.Stop();
        }
    }

    private static bool IsPointInsideApplicationWindow(NativeMethods.Point point)
    {
        var processId = (uint)Environment.ProcessId;
        var isInside = false;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (NativeMethods.GetWindowThreadProcessId(window, out var windowProcessId) == 0 ||
                windowProcessId != processId ||
                !NativeMethods.IsWindowVisible(window) ||
                !IsPointInsideWindow(window, point))
            {
                return true;
            }

            isInside = true;
            return false;
        }, nint.Zero);
        return isInside;
    }

    private static bool IsPointInsideWindow(nint window, NativeMethods.Point point)
    {
        return window != nint.Zero &&
            NativeMethods.GetWindowRect(window, out var rect) &&
            point.X >= rect.Left &&
            point.X < rect.Right &&
            point.Y >= rect.Top &&
            point.Y < rect.Bottom;
    }

    private void TrayIcon_OnContextMenuRequested(object? sender, EventArgs e)
    {
        PrepareContextMenuWindow();
        PlayerMenu.Placement = PlacementMode.MousePoint;
        PlayerMenu.PlacementTarget = this;
        PlayerMenu.IsOpen = true;
    }

    private void PrepareContextMenuWindow()
    {
        // ContextMenu 是独立的 Popup；先准备宿主窗口层级，避免菜单被任务栏覆盖。
        // ContextMenu is a separate Popup; prepare the host window layer before it opens above the taskbar.
        if (_windowHandle != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(_windowHandle);
        }
    }

    private void TrayIcon_OnDoubleClicked(object? sender, EventArgs e)
    {
        ShowSelectedMediaSource();
    }

    private void TrayIcon_OnShellRestarted(object? sender, EventArgs e)
    {
        _lastTaskbarRect = null;
        _lastPositionLeft = null;
        _lastPositionTop = null;
        _automaticLeft = null;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () =>
            {
                PositionOverTaskbar(force: true);
                if (_placementSettings.AutomaticPlacement)
                {
                    _ = RefreshAutomaticPlacementAsync();
                }
            });
    }

    private async void Previous_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipPreviousAsync);
    }

    private async void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.TogglePlayPauseAsync);
    }

    private async void Next_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipNextAsync);
    }

    private void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        RequestMediaReconnect();
    }

    internal void RequestMediaReconnect()
    {
        _ = RunMediaCommandAsync(_mediaSessionService.ReconnectAsync);
    }

    private async Task RunMediaCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            ShowDisconnectedState("媒体控制失败", exception.Message);
        }
    }

    private void Startup_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsCoordinator.UpdateStartup(StartupMenuItem.IsChecked);
        }
        catch (Exception exception)
        {
            StartupMenuItem.IsChecked = _settingsCoordinator.Current.StartupEnabled;
            MessageBox.Show(
                exception.Message,
                "无法修改开机启动",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSettings_OnClick(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.ShowSettingsWindow();
    }

    private void ShowMediaSource_OnClick(object sender, RoutedEventArgs e)
    {
        ShowSelectedMediaSource();
    }

    private void ShowMediaSource_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowSelectedMediaSource();
    }

    private void ShowSelectedMediaSource()
    {
        var sourceId = _mediaSessionService.SelectedSourceId;
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            MediaSourceLauncherService.ShowOrLaunch(
                sourceId,
                _mediaSessionService.SelectedSourceName);
        }
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).RequestShutdown();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmNcHitTest)
        {
            handled = true;
            return new IntPtr(NativeMethods.HtClient);
        }

        return IntPtr.Zero;
    }
}
