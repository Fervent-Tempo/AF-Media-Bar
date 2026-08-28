using System.Diagnostics;
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
using AFMediaBar.Adapters;
using AFMediaBar.Controls;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.ViewModels;
// System.Windows.Localization（枚举）与本地化帮助类同名，用别名消歧。
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar;

/// <summary>
/// 负责窗口生命周期、设置分发和跨模块用户命令，具体呈现与定位由领域 partial 模块协调。
/// Owns lifecycle, settings dispatch, and cross-module commands while focused partial modules coordinate presentation and placement.
/// </summary>
public partial class MainWindow : Window
{
    private const double ArtworkAreaWidth = 44;
    private const double CompactCentralHostWidth = 114;
    private const double MediaSwitchAreaWidth = 254;
    private const double CentralHostWidth = 210;
    private const double HorizontalPlayerHeight = 44;
    private const double VerticalPlayerWidth = 72;
    private const double VerticalArtworkAreaHeight = 48;
    private const double VerticalBaseHeight = 165;
    private const int EnvironmentRecoveryDelayMilliseconds = 900;
    private const int EnvironmentRecoveryRetryMilliseconds = 600;
    private const int EnvironmentRecoveryMaxAttempts = 8;

    private readonly MediaSessionService _mediaSessionService = new(
        new WpfArtworkDecoder(),
        WpfStringLocalizer.Instance);
    internal MediaSessionService MediaSessionService => _mediaSessionService;
    private readonly SettingsCoordinator _settingsCoordinator;
    private readonly MainWindowViewModel _viewModel;
    // 这些定时器都由窗口拥有，必须在 OnClosed 中停止后再释放服务。
    // The window owns these timers; OnClosed stops them before disposing services.
    private readonly DispatcherTimer _environmentRecoveryTimer;
    private MetricSettings _metricSettings;
    private WindowSettings _windowSettings;
    private PlacementSettings _placementSettings;
    // 这些服务持有 WinEvent、WASAPI、鼠标钩子或 Shell 图标等外部资源。
    // These services own WinEvent, WASAPI, mouse-hook, or Shell resources.
    private TaskbarEventWatcher? _taskbarEventWatcher;
    private readonly MouseHookService _mouseHookService;
    private TrayIconService? _trayIconService;
    private TaskbarHostService? _taskbarHostService;
    private HwndSource? _windowSource;
    private nint _windowHandle;
    private int? _lastPositionLeft;
    private int? _lastPositionTop;
    private bool _hasPresented;
    private bool _isExpanded;
    private bool _isVerticalLayout;
    private bool _isMenuOpen;
    private bool _isDragging;
    private bool _dragMoved;
    private PlacementSettings? _dragPreviousPlacementSettings;
    private bool _powerSuspended;
    private bool _sessionLocked;
    private bool _environmentRecoveryRunning;
    private bool _sessionNotificationRegistered;
    private bool _isClosed;
    private int _environmentRecoveryAttempts;
    private string _environmentRecoveryReason = string.Empty;
    private NativeMethods.Point _dragStartCursor;
    private int _dragStartWindowLeft;
    private int _dragStartWindowTop;

    private bool IsEnvironmentSuspended => _powerSuspended || _sessionLocked;

    public MainWindow()
    {
        TaskbarPlacementService.ValidateAlgorithm();
        _settingsCoordinator = (Application.Current as App)?.SettingsCoordinator ??
            new SettingsCoordinator();
        var settings = _settingsCoordinator.Current;
        _viewModel = new MainWindowViewModel(settings.Window);
        DataContext = _viewModel;
        _metricSettings = settings.Metrics;
        _windowSettings = settings.Window;
        _floatingNormalLeft = _windowSettings.FloatingLeft;
        _floatingNormalTop = _windowSettings.FloatingTop;
        RenderOptions.ProcessRenderMode = _metricSettings.LowGpuMode
            ? RenderMode.SoftwareOnly
            : RenderMode.Default;
        InitializeComponent();
        InitializeComponentLayout(settings.Layout);
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

        // A floating window is already a top-level HWND and must not enter the
        // taskbar-host startup concealment path. WPF can otherwise reapply the
        // cached hidden state after the native window has been positioned.
        Opacity = _windowSettings.HostMode == WindowHostMode.Floating ? 1 : 0;
        _placementSettings = settings.Placement;
        _taskbarSettings = TaskbarSettingsService.Read();
        if (_taskbarSettings.Alignment == TaskbarAlignment.Unknown &&
            _placementSettings.CachedTaskbarAlignment is { } cachedAlignment)
        {
            _taskbarSettings = _taskbarSettings with { Alignment = cachedAlignment };
        }
        _mouseHookService = new MouseHookService(new WpfUiDispatcher(Dispatcher));
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
        _environmentRecoveryTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(EnvironmentRecoveryDelayMilliseconds),
            DispatcherPriority.Background,
            OnEnvironmentRecoveryTimerTick,
            Dispatcher);
        _environmentRecoveryTimer.Stop();
        ApplyComponentMetricRefreshInterval();

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
        if (NativeMethods.GetWindowLongPtr(
                _windowHandle,
                NativeMethods.GwlExStyle).ToInt64() != extendedStyle)
        {
            DiagnosticsLogService.Write("window-style-update-failed");
        }

        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        _sessionNotificationRegistered = NativeMethods.WtsRegisterSessionNotification(
            _windowHandle,
            NativeMethods.NotifyForThisSession);
        if (!_sessionNotificationRegistered)
        {
            DiagnosticsLogService.Write(
                "session-notification-registration-failed",
                details: $"Win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        _taskbarHostService = new TaskbarHostService(_windowHandle);
        _trayIconService = new TrayIconService();
        _trayIconService.ContextMenuRequested += TrayIcon_OnContextMenuRequested;
        _trayIconService.DoubleClicked += TrayIcon_OnDoubleClicked;
        _trayIconService.ShellRestarted += TrayIcon_OnShellRestarted;

        _taskbarEventWatcher = new TaskbarEventWatcher(new WpfUiDispatcher(Dispatcher));
        _taskbarEventWatcher.TaskbarChanged += Taskbar_OnChanged;

        ApplyMetricSettings();
        ApplyWindowSettings();
        ApplyPlacementSettings();
        PositionOverTaskbar(force: true);
        SyncWindowStateProjection();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () => PositionOverTaskbar(force: true));
            ResumeEnvironmentSensitiveTimers();
            UpdateMetrics(advanceCycle: false);
            SetExpanded(expanded: false, animate: false);
            await RefreshAutomaticPlacementAsync();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("main-window-load", exception);
            ScheduleEnvironmentRecovery("window-load-failure");
        }

        try
        {
            await _mediaSessionService.InitializeAsync();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("media-session-initialize", exception);
            ShowDisconnectedState("Msg.SessionAccessFailed", exception.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 顺序很重要：先停止回调源，再解除宿主/钩子，最后释放 COM 与服务。
        // Order matters: stop callback sources, detach the host/hooks, then dispose services.
        _isClosed = true;
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
        _environmentRecoveryTimer.Stop();
        DisposeEdgeSurfaces();
        _componentSurface?.Dispose();
        _audioMonitorService?.Dispose();
        _audioMonitorService = null;
        _taskbarEventWatcher?.Dispose();
        _mouseHookService.Dispose();
        _trayIconService?.Dispose();
        _taskbarHostService?.Dispose();
        _taskbarHostService = null;
        if (_sessionNotificationRegistered && _windowHandle != nint.Zero)
        {
            NativeMethods.WtsUnRegisterSessionNotification(_windowHandle);
            _sessionNotificationRegistered = false;
        }
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
            PrepareHostModeTransition();
            if (_windowSettings.HostMode == WindowHostMode.Floating &&
                !e.Sections.HasFlag(SettingsSection.All) &&
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
            _viewModel.ApplyWindowSettings(_windowSettings);
            (Application.Current as App)?.RecreateMainWindow();
            return;
        }

        if (e.Sections.HasFlag(SettingsSection.Performance))
        {
            _metricSettings = LayoutRuntimeService.ResolveComponentSettings(
                _activeLayoutProfile,
                e.Settings.Metrics);
            ApplyMetricSettings();
        }

        if (e.Sections.HasFlag(SettingsSection.Layout))
        {
            ComponentSurface_OnLayoutSettingsChanged(e.Settings.Layout);
        }

        if ((e.Sections.HasFlag(SettingsSection.Appearance) ||
             e.Sections.HasFlag(SettingsSection.Font)) &&
            !e.Sections.HasFlag(SettingsSection.Layout))
        {
            ApplyComponentLayout();
            ApplyResponsivePlayerDimensions();
            PositionOverTaskbar(force: true);
        }

        if (e.Sections.HasFlag(SettingsSection.Window) ||
            e.Sections.HasFlag(SettingsSection.General) ||
            e.Sections.HasFlag(SettingsSection.Interaction))
        {
            _windowSettings = nextWindowSettings;
            _viewModel.ApplyWindowSettings(_windowSettings);
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
                _ = RefreshAutomaticPlacementSafelyAsync();
            }
            else
            {
                _placementTimer.Stop();
            }
        }

        PositionOverTaskbar(force: true);
    }

    private void ApplyMediaDisplaySettings()
    {
        var artworkVisible = _windowSettings.ShowArtwork
            ? Visibility.Visible
            : Visibility.Collapsed;
        ArtworkColumn.Width = new GridLength(
            _windowSettings.ShowArtwork ? ArtworkAreaWidth : 0);
        ArtworkHost.Visibility = artworkVisible;
        VerticalArtworkHost.Visibility = artworkVisible;

        var artworkRadius = Math.Clamp(_windowSettings.ArtworkCornerRadius, 0, 20);
        var horizontalRadius = (double)artworkRadius;
        var verticalRadius = (double)artworkRadius;
        ArtworkHost.CornerRadius = new CornerRadius(horizontalRadius);
        VerticalArtworkHost.CornerRadius = new CornerRadius(verticalRadius);
        ArtworkImageClip.RadiusX = horizontalRadius;
        ArtworkImageClip.RadiusY = horizontalRadius;
        VerticalArtworkImageClip.RadiusX = verticalRadius;
        VerticalArtworkImageClip.RadiusY = verticalRadius;

        var centralWidth = _windowSettings.ShowMediaInfo
            ? CentralHostWidth
            : CompactCentralHostWidth;
        CentralColumn.Width = new GridLength(centralWidth);
        CentralHost.Width = centralWidth;
        ApplyResponsivePlayerDimensions();
        SetExpanded(_isExpanded, animate: false);
        ScheduleMarqueeUpdate();
    }

    private void ApplyWindowSettings()
    {
        _viewModel.ApplyWindowSettings(_windowSettings);
        ApplyMediaDisplaySettings();
        var floating = _windowSettings.HostMode == WindowHostMode.Floating;
        if (_taskbarHostService is not null &&
            !_taskbarHostService.SetFloating(floating))
        {
            DiagnosticsLogService.Write(
                "window-host-transition-failed",
                details: _windowSettings.HostMode.ToString());
            if (!floating)
            {
                ScheduleEnvironmentRecovery("window-host-transition-failure");
            }
        }

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

    private void SyncWindowStateProjection()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        int? left = null;
        int? top = null;
        var width = 0;
        var height = 0;
        if (NativeMethods.GetWindowRect(_windowHandle, out var rect))
        {
            left = rect.Left;
            top = rect.Top;
            width = rect.Width;
            height = rect.Height;
        }

        _viewModel.Placement.ApplyBounds(
            left,
            top,
            width,
            height,
            NativeMethods.GetDpiForWindow(_windowHandle));
        _viewModel.Placement.SetPresentation(IsVisible, _isExpanded);
        _viewModel.TaskbarHost.ApplySnapshot(
            _taskbarHostService?.TaskbarHandle ?? nint.Zero,
            _taskbarHostService?.IsEmbedded == true,
            _taskbarHostService?.IsFloating == true);
    }

    private void SaveWindowSettings(bool showError = true)
    {
        try
        {
            _settingsCoordinator.SynchronizeWindow(_windowSettings);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("save-window-settings", exception);
            if (!showError)
            {
                return;
            }

            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.SaveWindowFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
            DiagnosticsLogService.Write("save-placement-settings", exception);
            if (!showError)
            {
                return;
            }

            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.SavePositionFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void PlayerRoot_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInteractiveLayoutElement(source))
        {
            return;
        }

        // 长条本身始终是拖动区域；只有明确可交互的组件拦截鼠标，避免自定义布局后旧节点尺寸决定拖动范围。
        // The strip itself is always draggable; only explicitly interactive widgets intercept the pointer so legacy node sizes cannot shrink the drag area.
        if (!NativeMethods.GetCursorPos(out _dragStartCursor) ||
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
        if (_windowSettings.HostMode == WindowHostMode.Taskbar)
        {
            // 长条本身固定可拖动；开始拖动时自动退出自动定位/锁定，避免用户必须先寻找隐藏的解锁开关。
            // The strip is always draggable; starting a drag exits auto-placement/lock so users do not need to find a hidden unlock switch first.
            _dragPreviousPlacementSettings = _placementSettings;
            _placementSettings = _placementSettings with
            {
                AutomaticPlacement = false,
                PositionLocked = false,
                VerticalPositionLocked = false
            };
            _placementTimer.Stop();
        }
        _isDragging = true;
        Mouse.Capture(PlayerRoot);
        e.Handled = true;
    }

    private async void PlayerRoot_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsLayoutWheelSource(source))
        {
            // 组件自身将在事件隧道结束时处理设备/音量滚轮；父级不能把它误判为媒体来源切换。
            // The widget handles device/volume wheel input after tunneling; the parent must not mistake it for media-source switching.
            return;
        }

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
            _floatingNormalLeft = _dragStartWindowLeft + deltaX - _layoutBodyCorrectionX;
            _floatingNormalTop = _dragStartWindowTop + deltaY - _layoutBodyCorrectionY;
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
                var edgeInsets = ResolveCollapsedActiveEdgeInsets();
                var collapsedTop = (int)Math.Round(edgeInsets.Top * PlayerScaleTransform.ScaleY * scale);
                var collapsedBottom = (int)Math.Round(edgeInsets.Bottom * PlayerScaleTransform.ScaleY * scale);
                var top = Math.Clamp(
                    _dragStartWindowTop + deltaY,
                    taskbarRect.Top + margin - collapsedTop,
                    Math.Max(
                        taskbarRect.Top + margin - collapsedTop,
                        taskbarRect.Bottom - margin - playerHeight + collapsedBottom));
                _placementSettings = _placementSettings with
                {
                    ManualVerticalOffsetDip = (int)Math.Round(
                        (top - _layoutBodyCorrectionY - taskbarRect.Top) / scale)
                };
            }
            else
            {
                var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
                var playerWidth = (int)Math.Ceiling(
                    PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
                var edgeInsets = ResolveCollapsedActiveEdgeInsets();
                var collapsedLeft = (int)Math.Round(edgeInsets.Left * PlayerScaleTransform.ScaleX * scale);
                var collapsedRight = (int)Math.Round(edgeInsets.Right * PlayerScaleTransform.ScaleX * scale);
                var left = Math.Clamp(
                    _dragStartWindowLeft + deltaX,
                    taskbarRect.Left + margin - collapsedLeft,
                    Math.Max(
                        taskbarRect.Left + margin - collapsedLeft,
                        taskbarRect.Right - margin - playerWidth + collapsedRight));
                var playerHeight = (int)Math.Ceiling(
                    PlayerRoot.Height * PlayerScaleTransform.ScaleY * scale);
                var collapsedTop = (int)Math.Round(edgeInsets.Top * PlayerScaleTransform.ScaleY * scale);
                var collapsedBottom = (int)Math.Round(edgeInsets.Bottom * PlayerScaleTransform.ScaleY * scale);
                var centeredTop =
                    taskbarRect.Top + (taskbarRect.Height - playerHeight) / 2;
                var top = Math.Clamp(
                    _dragStartWindowTop + deltaY,
                    taskbarRect.Top - collapsedTop,
                    Math.Max(taskbarRect.Top - collapsedTop, taskbarRect.Bottom - playerHeight + collapsedBottom));
                _placementSettings = _placementSettings with
                {
                    ManualOffsetDip = (int)Math.Round(
                        (left - _layoutBodyCorrectionX - taskbarRect.Left) / scale),
                    TaskbarTopOffsetDip = Math.Clamp(
                        (int)Math.Round(
                            (top - _layoutBodyCorrectionY - centeredTop) / scale),
                        -20,
                        20)
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

        FinishPlayerDrag(commit: _dragMoved);
        e.Handled = true;
    }

    private void PlayerRoot_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            // 窗口失去捕获时也要提交已移动位置或恢复临时解锁状态，避免拖动中切换桌面后留下“半拖动”状态。
            // If capture is lost, commit the moved position or restore the temporary unlock so desktop switches cannot leave a half-drag state.
            FinishPlayerDrag(commit: _dragMoved);
        }
    }

    private void FinishPlayerDrag(bool commit)
    {
        _isDragging = false;
        Mouse.Capture(null);
        if (commit)
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
        else if (_dragPreviousPlacementSettings is { } previousPlacement)
        {
            _placementSettings = previousPlacement;
            if (_placementSettings.AutomaticPlacement)
            {
                _placementTimer.Start();
            }
        }

        _dragPreviousPlacementSettings = null;
        _dragMoved = false;
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
        TaskbarModeMenuItem.IsChecked = _windowSettings.HostMode == WindowHostMode.Taskbar;
        FloatingModeMenuItem.IsChecked = _windowSettings.HostMode == WindowHostMode.Floating;
        TaskbarModeMenuItem.IsEnabled = _windowSettings.HostMode != WindowHostMode.Taskbar;
        FloatingModeMenuItem.IsEnabled = _windowSettings.HostMode != WindowHostMode.Floating;
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
        if (_windowSettings.HostMode == WindowHostMode.Taskbar &&
            _windowHandle != nint.Zero)
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
        ScheduleEnvironmentRecovery("explorer-restarted");
    }

    private void PauseEnvironmentSensitiveTimers()
    {
        _positionTimer.Stop();
        _placementTimer.Stop();
        _metricsTimer.Stop();
        _audioMonitorTimer.Stop();
        _collapseTimer.Stop();
        _marqueeTimer.Stop();
        _edgeAnimationTimer.Stop();
        _edgeHoverTimer.Stop();
        _edgeAnimationHasTarget = false;
        StopMarquees();
        _audioMonitorService?.ResetAfterEnvironmentChange();
    }

    private void ResumeEnvironmentSensitiveTimers()
    {
        if (_isClosed ||
            !IsLoaded ||
            IsEnvironmentSuspended ||
            _environmentRecoveryTimer.IsEnabled)
        {
            return;
        }

        _positionTimer.Start();
        if (_placementSettings.AutomaticPlacement)
        {
            _placementTimer.Start();
        }

        _metricsTimer.Start();
        if (_metricSettings.AudioMonitorEnabled)
        {
            _audioMonitorService ??= new AudioMonitorService();
            _audioMonitorTimer.Start();
        }

        ScheduleMarqueeUpdate();
    }

    private void ScheduleEnvironmentRecovery(string reason)
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!_environmentRecoveryRunning && !_environmentRecoveryTimer.IsEnabled)
        {
            _environmentRecoveryAttempts = 0;
        }

        _environmentRecoveryReason = reason;
        _viewModel.ApplyRecovery(reason);
        PauseEnvironmentSensitiveTimers();
        _environmentRecoveryTimer.Stop();
        _environmentRecoveryTimer.Interval =
            TimeSpan.FromMilliseconds(EnvironmentRecoveryDelayMilliseconds);
        _environmentRecoveryTimer.Start();
    }

    private void OnEnvironmentRecoveryTimerTick(object? sender, EventArgs e)
    {
        _environmentRecoveryTimer.Stop();
        if (_isClosed || IsEnvironmentSuspended)
        {
            return;
        }

        _environmentRecoveryRunning = true;
        try
        {
            _environmentRecoveryAttempts++;
            _lastTaskbarRect = null;
            _lastPositionLeft = null;
            _lastPositionTop = null;
            _automaticLeft = null;
            _audioMonitorService?.ResetAfterEnvironmentChange();

            if (_windowSettings.HostMode == WindowHostMode.Taskbar)
            {
                RefreshTaskbarSettings();
                if (_taskbarHostService?.SetFloating(floating: false) != true ||
                    !TryGetTaskbarBounds(out _))
                {
                    RetryEnvironmentRecovery();
                    return;
                }
            }

            PositionOverTaskbar(force: true);
            if (_environmentRecoveryTimer.IsEnabled)
            {
                return;
            }

            ResumeEnvironmentSensitiveTimers();
            if (_placementSettings.AutomaticPlacement)
            {
                _ = RefreshAutomaticPlacementSafelyAsync();
            }

            DiagnosticsLogService.Write(
                "environment-recovery-completed",
                details: $"Reason={_environmentRecoveryReason};Attempts={_environmentRecoveryAttempts}");
            _viewModel.ApplyRecovery(null);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "environment-recovery-failed",
                exception,
                $"Reason={_environmentRecoveryReason};Attempts={_environmentRecoveryAttempts}");
            RetryEnvironmentRecovery();
        }
        finally
        {
            _environmentRecoveryRunning = false;
        }
    }

    private void RetryEnvironmentRecovery()
    {
        if (_environmentRecoveryAttempts >= EnvironmentRecoveryMaxAttempts)
        {
            DiagnosticsLogService.Write(
                "environment-recovery-exhausted",
                details: $"Reason={_environmentRecoveryReason};Attempts={_environmentRecoveryAttempts}");
            _viewModel.ApplyRecovery(null);
            ResumeEnvironmentSensitiveTimers();
            return;
        }

        _environmentRecoveryTimer.Interval =
            TimeSpan.FromMilliseconds(EnvironmentRecoveryRetryMilliseconds);
        _environmentRecoveryTimer.Start();
    }

    private async Task RefreshAutomaticPlacementSafelyAsync()
    {
        try
        {
            await RefreshAutomaticPlacementAsync();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("automatic-placement-refresh", exception);
        }
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

    internal void RequestEnvironmentRecovery(string reason)
    {
        if (Dispatcher.CheckAccess())
        {
            ScheduleEnvironmentRecovery(reason);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => ScheduleEnvironmentRecovery(reason));
    }

    private async Task RunMediaCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("media-command", exception, command.Method.Name);
            ShowDisconnectedState("Msg.MediaControlFailed", exception.Message);
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
            DiagnosticsLogService.Write("startup-setting-update", exception);
            StartupMenuItem.IsChecked = _settingsCoordinator.Current.StartupEnabled;
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.AutoStartFailed"),
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

    private static bool IsInteractiveLayoutElement(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ComponentLayoutSurface.GetIsInteractiveElement(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLayoutWheelSource(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: MediaCommandKind command } &&
                command is MediaCommandKind.SelectOutputDevice or MediaCommandKind.AdjustVolume &&
                ComponentLayoutSurface.GetIsInteractiveElement(current))
            {
                return true;
            }
        }

        return false;
    }

    private void TaskbarMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (_windowSettings.HostMode == WindowHostMode.Taskbar)
        {
            return;
        }

        SwitchHostMode(WindowHostMode.Taskbar);
    }

    private void FloatingMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (_windowSettings.HostMode == WindowHostMode.Floating)
        {
            return;
        }

        SwitchHostMode(WindowHostMode.Floating);
    }

    private void SwitchHostMode(WindowHostMode hostMode)
    {
        try
        {
            _settingsCoordinator.UpdateWindow(_windowSettings with { HostMode = hostMode });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(
                "quick-host-mode-switch",
                exception,
                hostMode.ToString());
            TaskbarModeMenuItem.IsChecked = _windowSettings.HostMode == WindowHostMode.Taskbar;
            FloatingModeMenuItem.IsChecked = _windowSettings.HostMode == WindowHostMode.Floating;
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.SaveWindowFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowSelectedMediaSource()
    {
        var sourceId = _mediaSessionService.SelectedSourceId;
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            var sourceName = _mediaSessionService.SelectedSourceName;
            if (!MediaSourceLauncherService.ShowOrLaunch(sourceId, sourceName))
            {
                DiagnosticsLogService.Write(
                    "media-source-open-failed",
                    details: $"SourceId={sourceId};SourceName={sourceName}");
            }
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
        if (message == NativeMethods.WmPowerBroadcast)
        {
            var powerEvent = wParam.ToInt32();
            if (powerEvent == NativeMethods.PbtApmSuspend)
            {
                _powerSuspended = true;
                PauseEnvironmentSensitiveTimers();
                DiagnosticsLogService.Write("system-suspend");
            }
            else if (powerEvent is NativeMethods.PbtApmResumeAutomatic or
                NativeMethods.PbtApmResumeSuspend)
            {
                _powerSuspended = false;
                ScheduleEnvironmentRecovery("system-resume");
            }
        }
        else if (message == NativeMethods.WmWtsSessionChange)
        {
            var sessionEvent = wParam.ToInt32();
            if (sessionEvent == NativeMethods.WtsSessionLock)
            {
                _sessionLocked = true;
                PauseEnvironmentSensitiveTimers();
                DiagnosticsLogService.Write("session-locked");
            }
            else if (sessionEvent == NativeMethods.WtsSessionUnlock)
            {
                _sessionLocked = false;
                ScheduleEnvironmentRecovery("session-unlocked");
            }
        }
        else if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged)
        {
            ScheduleEnvironmentRecovery(
                message == NativeMethods.WmDpiChanged ? "dpi-changed" : "display-changed");
        }
        else if (message == NativeMethods.WmDeviceChange)
        {
            ScheduleEnvironmentRecovery("device-changed");
        }

        if (message == NativeMethods.WmNcHitTest)
        {
            handled = true;
            return new IntPtr(NativeMethods.HtClient);
        }

        return IntPtr.Zero;
    }
}
