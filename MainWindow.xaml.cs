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
    private const int MouseWheelDelta = 120;
    private const int VolumeWheelStepPercent = 2;
    private const int HorizontalMarginAt96Dpi = 8;
    private const int VerticalMarginAt96Dpi = 4;
    private const int AudioMonitorIntervalMilliseconds = 50;

    private readonly MediaSessionService _mediaSessionService = new();
    private readonly SystemMetricsService _systemMetricsService = new();
    private readonly TaskbarPlacementService _taskbarPlacementService = new();
    private readonly AudioDeviceService _audioDeviceService = new();
    private readonly ApplicationVolumeService _applicationVolumeService = new();
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
    private MetricSettings _metricSettings;
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
    private bool _hasPresented;
    private bool _isExpanded;
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

    public MainWindow()
    {
        TaskbarPlacementService.ValidateAlgorithm();
        _metricSettings = MetricSettingsService.Load();
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
        _placementSettings = PlacementSettingsService.Load();
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

        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        _mediaSessionService.SessionsChanged += OnSessionsChanged;
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
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        RefreshTaskbarSettings();
        PositionOverTaskbar(force: false);
    }

    private async void OnPlacementTimerTick(object? sender, EventArgs e)
    {
        await RefreshAutomaticPlacementAsync();
    }

    private void Taskbar_OnChanged(TaskbarWindowEvent taskbarEvent)
    {
        RefreshTaskbarSettings();
        if (TryGetTaskbarBounds(out var bounds))
        {
            var horizontalGeometryChanged = !_lastTaskbarRect.HasValue ||
                _lastTaskbarRect.Value.Left != bounds.ScreenBounds.Left ||
                _lastTaskbarRect.Value.Right != bounds.ScreenBounds.Right;
            if (_placementSettings.AutomaticPlacement && horizontalGeometryChanged)
            {
                _automaticLeft = null;
                _ = RefreshAutomaticPlacementAsync();
            }

            // Vertical location changes are inherited from the Explorer parent.
            // Repositioning the child during that animation would reintroduce the lag.
            if (taskbarEvent.EventId == NativeMethods.EventObjectLocationChange &&
                !horizontalGeometryChanged)
            {
                return;
            }
        }

        PositionOverTaskbar(force: true);
    }

    private void RefreshTaskbarSettings()
    {
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

        CollapseWhenPointerLeavesWindow();

        if (NativeMethods.ShouldHideForFullScreenApp(_windowHandle))
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
            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        var taskbarRect = bounds.ScreenBounds;
        var scale = bounds.Scale;
        if (_placementSettings.AutomaticPlacement &&
            _lastTaskbarRect.HasValue &&
            _lastTaskbarRect.Value.Width != taskbarRect.Width)
        {
            _automaticLeft = null;
        }

        var marginX = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
        var marginY = (int)Math.Round(VerticalMarginAt96Dpi * scale);
        var playerWidth = (int)Math.Ceiling(PlayerRoot.Width * scale);
        var minLeft = taskbarRect.Left + marginX;
        var maxLeft = Math.Max(minLeft, taskbarRect.Right - marginX - playerWidth);
        var desiredLeft = _placementSettings.AutomaticPlacement
            ? ResolveAutomaticLeft(taskbarRect, scale, minLeft)
            : taskbarRect.Left + (int)Math.Round(_placementSettings.ManualOffsetDip * scale);
        desiredLeft ??= _lastPositionLeft;
        if (!desiredLeft.HasValue)
        {
            _ = RefreshAutomaticPlacementAsync();
            return;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            force = true;
        }

        var clampedLeft = Math.Clamp(desiredLeft.Value, minLeft, maxLeft);

        var height = Math.Max(1, taskbarRect.Height - marginY * 2);
        var heightDip = Math.Max(44, height / scale);
        var width = Math.Max(1, (int)Math.Ceiling(PlayerRoot.Width * scale));
        var windowHeight = Math.Max(1, (int)Math.Ceiling(heightDip * scale));
        var rectChanged = !_lastTaskbarRect.HasValue ||
            !_lastTaskbarRect.Value.Equals(taskbarRect);
        var leftChanged = _lastPositionLeft != clampedLeft;

        Height = heightDip;
        if (!force && !rectChanged && !leftChanged)
        {
            RevealAfterPlacement();
            return;
        }

        _lastTaskbarRect = taskbarRect;
        _lastPositionLeft = clampedLeft;
        _taskbarHostService?.Position(
            clampedLeft,
            taskbarRect.Top + marginY,
            width,
            windowHeight,
            visible: true);
        RevealAfterPlacement();
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
        var playerWidthDip = (int)Math.Round(PlayerRoot.Width);
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
            var playerWidth = (int)Math.Ceiling(PlayerRoot.Width * scale);
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
        if (!_placementSettings.AutomaticPlacement ||
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
        var playerWidth = (int)Math.Ceiling(PlayerRoot.Width * scale);
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
            CachedPlayerWidthDip = (int)Math.Round(PlayerRoot.Width),
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
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        PreviousButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
        PlayPauseButton.ToolTip = snapshot.IsPlaying ? "暂停" : "播放";

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
    }

    private void ApplySessions(IReadOnlyList<MediaSessionOption> sessions)
    {
        _mediaSessions = sessions;
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
        _isExpanded = expanded;
        ControlsHost.IsHitTestVisible = expanded;
        var showVisualizer = _metricSettings.AudioMonitorEnabled && !expanded;
        AudioVisualizerHost.Visibility = showVisualizer
            ? Visibility.Visible
            : Visibility.Collapsed;
        InfoHost.Visibility = showVisualizer
            ? Visibility.Collapsed
            : Visibility.Visible;
        animate &= !_metricSettings.LowGpuMode;
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
        StopMarquee(TitleTransform);
        StopMarquee(ArtistTransform);
    }

    private static void StopMarquee(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
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
            UpdatePlayerWidth(metricsVisible: false);
            return;
        }

        MetricsHost.Visibility = Visibility.Visible;
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
        PlayerRoot.Width = PlayerWidthWithoutExtras +
            (metricsVisible ? MetricsAreaWidth : 0) +
            (_metricSettings.OutputDeviceSwitcherEnabled ? OutputDeviceAreaWidth : 0) +
            (_metricSettings.VolumeControlEnabled ? VolumeControlAreaWidth : 0);
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
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90));
        fadeOut.Completed += (_, _) =>
        {
            MetricsText.Text = text;
            MetricsText.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        };
        MetricsText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ApplyMetricSettings()
    {
        SyncMetricMenuState();
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

    private void SyncMetricMenuState()
    {
        MetricsEnabledMenuItem.IsChecked = _metricSettings.Enabled;
        SystemMemoryMenuItem.IsChecked = _metricSettings.ShowSystemMemory;
        SystemCpuMenuItem.IsChecked = _metricSettings.ShowSystemCpu;
        SystemGpuMenuItem.IsChecked = _metricSettings.ShowSystemGpu;
        ProcessMemoryMenuItem.IsChecked = _metricSettings.ShowProcessMemory;
        LowGpuModeMenuItem.IsChecked = _metricSettings.LowGpuMode;
        AudioMonitorMenuItem.IsChecked = _metricSettings.AudioMonitorEnabled;
        OutputDeviceSwitcherMenuItem.IsChecked =
            _metricSettings.OutputDeviceSwitcherEnabled;
        VolumeControlMenuItem.IsChecked = _metricSettings.VolumeControlEnabled;
        SystemMemoryMenuItem.IsEnabled = _metricSettings.Enabled;
        SystemCpuMenuItem.IsEnabled = _metricSettings.Enabled;
        SystemGpuMenuItem.IsEnabled = _metricSettings.Enabled;
        ProcessMemoryMenuItem.IsEnabled = _metricSettings.Enabled;
    }

    private void MetricSetting_OnClick(object sender, RoutedEventArgs e)
    {
        _metricSettings = new MetricSettings(
            MetricsEnabledMenuItem.IsChecked,
            SystemMemoryMenuItem.IsChecked,
            SystemCpuMenuItem.IsChecked,
            SystemGpuMenuItem.IsChecked,
            ProcessMemoryMenuItem.IsChecked,
            LowGpuModeMenuItem.IsChecked,
            AudioMonitorMenuItem.IsChecked,
            OutputDeviceSwitcherMenuItem.IsChecked,
            VolumeControlMenuItem.IsChecked);
        try
        {
            MetricSettingsService.Save(_metricSettings);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "无法保存性能指标设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ApplyMetricSettings();
    }

    private void ApplyOutputDeviceSettings()
    {
        var enabled = _metricSettings.OutputDeviceSwitcherEnabled;
        OutputDeviceHost.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        AutomaticPlacementMenuItem.IsChecked = _placementSettings.AutomaticPlacement;
        LockPositionMenuItem.IsChecked = _placementSettings.PositionLocked;
        LockPositionMenuItem.IsEnabled = !_placementSettings.AutomaticPlacement;

        var canDrag = !_placementSettings.AutomaticPlacement &&
            !_placementSettings.PositionLocked;
        var cursor = canDrag ? Cursors.SizeWE : Cursors.Hand;
        ArtworkHost.Cursor = cursor;
        InfoHost.Cursor = cursor;
    }

    private async void AutomaticPlacement_OnClick(object sender, RoutedEventArgs e)
    {
        if (AutomaticPlacementMenuItem.IsChecked)
        {
            if (NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
            {
                _automaticLeft = windowRect.Left;
            }

            _placementSettings = _placementSettings with
            {
                AutomaticPlacement = true,
                PositionLocked = true
            };
        }
        else
        {
            var manualOffsetDip = GetCurrentOffsetDip();
            _placementSettings = _placementSettings with
            {
                AutomaticPlacement = false,
                PositionLocked = false,
                ManualOffsetDip = manualOffsetDip
            };
        }

        SavePlacementSettings();
        ApplyPlacementSettings();
        if (_placementSettings.AutomaticPlacement)
        {
            _placementTimer.Start();
            await RefreshAutomaticPlacementAsync();
        }
        else
        {
            _placementTimer.Stop();
            PositionOverTaskbar(force: true);
        }
    }

    private void LockPosition_OnClick(object sender, RoutedEventArgs e)
    {
        _placementSettings = _placementSettings with
        {
            PositionLocked = LockPositionMenuItem.IsChecked
        };
        SavePlacementSettings();
        ApplyPlacementSettings();
    }

    private int GetCurrentOffsetDip()
    {
        if (!TryGetTaskbarBounds(out var bounds) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return _placementSettings.ManualOffsetDip;
        }

        var taskbarRect = bounds.ScreenBounds;
        var scale = bounds.Scale;
        return Math.Max(0, (int)Math.Round((windowRect.Left - taskbarRect.Left) / scale));
    }

    private void SavePlacementSettings(bool showError = true)
    {
        try
        {
            PlacementSettingsService.Save(_placementSettings);
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
        if (_placementSettings.AutomaticPlacement ||
            _placementSettings.PositionLocked ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PlayerRoot);
        if (position.X > 44 + InfoHost.ActualWidth ||
            !NativeMethods.GetCursorPos(out _dragStartCursor) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return;
        }

        _dragStartWindowLeft = windowRect.Left;
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

        if (e.GetPosition(PlayerRoot).X >= MediaSwitchAreaWidth)
        {
            return;
        }

        if (_mediaSessions.Count < 2)
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
        _dragMoved |= Math.Abs(deltaX) >= 3;

        if (TryGetTaskbarBounds(out var bounds))
        {
            var taskbarRect = bounds.ScreenBounds;
            var scale = bounds.Scale;
            var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
            var playerWidth = (int)Math.Ceiling(PlayerRoot.Width * scale);
            var left = Math.Clamp(
                _dragStartWindowLeft + deltaX,
                taskbarRect.Left + margin,
                Math.Max(
                    taskbarRect.Left + margin,
                    taskbarRect.Right - margin - playerWidth));
            _placementSettings = _placementSettings with
            {
                ManualOffsetDip = (int)Math.Round((left - taskbarRect.Left) / scale)
            };
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
            SavePlacementSettings();
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
        UpdateMouseHookState();
        SetExpanded(expanded: true, animate: true);
        StartupMenuItem.IsChecked = StartupService.IsEnabled;
        ApplyPlacementSettings();
        SyncMetricMenuState();
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

    private async void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.ReconnectAsync);
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
            StartupService.SetEnabled(StartupMenuItem.IsChecked);
        }
        catch (Exception exception)
        {
            StartupMenuItem.IsChecked = StartupService.IsEnabled;
            MessageBox.Show(
                exception.Message,
                "无法修改开机启动",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
