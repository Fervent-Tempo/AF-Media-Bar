using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarPlayer.Interop;
using TaskbarPlayer.Models;
using TaskbarPlayer.Services;

namespace TaskbarPlayer;

public partial class MainWindow : Window
{
    private const double CollapsedInfoWidth = 210;
    private const double ExpandedInfoWidth = 96;
    private const int HorizontalMarginAt96Dpi = 8;
    private const int VerticalMarginAt96Dpi = 4;

    private readonly MediaSessionService _mediaSessionService = new();
    private readonly SystemMetricsService _systemMetricsService = new();
    private readonly TaskbarPlacementService _taskbarPlacementService = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _taskbarAnimationTimer;
    private readonly DispatcherTimer _placementTimer;
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _collapseTimer;
    private MetricSettings _metricSettings;
    private PlacementSettings _placementSettings;
    private TaskbarSettings _taskbarSettings;
    private SystemMetricsSnapshot _lastMetricsSnapshot;
    private IReadOnlyList<MediaSessionOption> _mediaSessions = [];
    private TaskbarEventWatcher? _taskbarEventWatcher;
    private readonly MouseHookService _mouseHookService;
    private TrayIconService? _trayIconService;
    private HwndSource? _windowSource;
    private NativeMethods.Rect? _lastTaskbarRect;
    private NativeMethods.Rect? _animationTaskbarRect;
    private nint _windowHandle;
    private int? _automaticLeft;
    private int? _lastPositionLeft;
    private int _metricCycleIndex;
    private int _metricCycleTicks;
    private int _marqueeVersion;
    private int _placementRefreshInProgress;
    private bool _hasPresented;
    private bool _isExpanded;
    private bool _isMenuOpen;
    private bool _isDragging;
    private bool _dragMoved;
    private NativeMethods.Point _dragStartCursor;
    private int _dragStartWindowLeft;

    public MainWindow()
    {
        TaskbarPlacementService.ValidateAlgorithm();
        InitializeComponent();

        Opacity = 0;
        _metricSettings = MetricSettingsService.Load();
        _placementSettings = PlacementSettingsService.Load();
        _taskbarSettings = TaskbarSettingsService.Read();
        _mouseHookService = new MouseHookService(Dispatcher);
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnPositionTimerTick,
            Dispatcher);
        _taskbarAnimationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnTaskbarAnimationTimerTick,
            Dispatcher);
        _taskbarAnimationTimer.Stop();
        _placementTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            OnPlacementTimerTick,
            Dispatcher);
        _metricsTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnMetricsTimerTick,
            Dispatcher);
        _collapseTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(160),
            DispatcherPriority.Input,
            OnCollapseTimerTick,
            Dispatcher);
        _collapseTimer.Stop();

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
        _trayIconService = new TrayIconService(_windowHandle);
        _trayIconService.ContextMenuRequested += TrayIcon_OnContextMenuRequested;
        _trayIconService.DoubleClicked += TrayIcon_OnDoubleClicked;

        _taskbarEventWatcher = new TaskbarEventWatcher(Dispatcher);
        _taskbarEventWatcher.TaskbarChanged += Taskbar_OnChanged;
        if (_taskbarSettings.AutoHide)
        {
            BeginTaskbarAnimationTracking();
        }

        ApplyMetricSettings();
        ApplyPlacementSettings();
        PositionOverTaskbar(force: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _positionTimer.Start();
        _placementTimer.Start();
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
        _positionTimer.Stop();
        _taskbarAnimationTimer.Stop();
        _placementTimer.Stop();
        _metricsTimer.Stop();
        _collapseTimer.Stop();
        _taskbarEventWatcher?.Dispose();
        _mouseHookService.Dispose();
        _trayIconService?.Dispose();
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

    private void Taskbar_OnChanged(object? sender, EventArgs e)
    {
        if (_taskbarSettings.AutoHide)
        {
            BeginTaskbarAnimationTracking();
            return;
        }

        PositionOverTaskbar(force: true);
    }

    private void RefreshTaskbarSettings()
    {
        var settings = TaskbarSettingsService.Read();
        if (settings.Alignment != _taskbarSettings.Alignment)
        {
            _automaticLeft = null;
            _lastPositionLeft = null;
            if (_placementSettings.AutomaticPlacement)
            {
                Opacity = 0;
            }
        }

        _taskbarSettings = settings;
        if (settings.AutoHide)
        {
            BeginTaskbarAnimationTracking();
        }
        else
        {
            _taskbarAnimationTimer.Stop();
        }
    }

    private void BeginTaskbarAnimationTracking()
    {
        if (!_taskbarAnimationTimer.IsEnabled)
        {
            _taskbarAnimationTimer.Start();
        }
    }

    private void OnTaskbarAnimationTimerTick(object? sender, EventArgs e)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !NativeMethods.GetWindowRect(taskbar, out var rect))
        {
            return;
        }

        if (!_taskbarSettings.AutoHide)
        {
            _taskbarAnimationTimer.Stop();
            return;
        }

        if (!_animationTaskbarRect.HasValue || !_animationTaskbarRect.Value.Equals(rect))
        {
            _animationTaskbarRect = rect;
            FollowTaskbarAnimation(taskbar, rect);
        }
    }

    private void FollowTaskbarAnimation(nint taskbar, NativeMethods.Rect taskbarRect)
    {
        if (!_hasPresented ||
            Visibility != Visibility.Visible ||
            !_lastPositionLeft.HasValue)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var marginY = (int)Math.Round(VerticalMarginAt96Dpi * scale);
        _lastTaskbarRect = taskbarRect;
        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            _lastPositionLeft.Value,
            taskbarRect.Top + marginY,
            0,
            0,
            NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpShowWindow |
                NativeMethods.SwpAsyncWindowPos);
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
            _lastTaskbarRect = null;
            _lastPositionLeft = null;
            if (Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
            }

            return;
        }

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero ||
            !NativeMethods.GetWindowRect(taskbar, out var taskbarRect) ||
            taskbarRect.Width < taskbarRect.Height)
        {
            _lastTaskbarRect = null;
            _lastPositionLeft = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        var scale = dpi > 0 ? dpi / 96d : 1d;
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
        if (!desiredLeft.HasValue)
        {
            Opacity = 0;
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
        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            clampedLeft,
            taskbarRect.Top + marginY,
            0,
            0,
            NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpShowWindow);
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

        return _taskbarSettings.Alignment == TaskbarAlignment.Center
            ? fallbackLeft
            : null;
    }

    private void RevealAfterPlacement()
    {
        if (_hasPresented && Opacity == 1)
        {
            return;
        }

        _hasPresented = true;
        Opacity = 1;
    }

    private async Task RefreshAutomaticPlacementAsync()
    {
        if (!_placementSettings.AutomaticPlacement ||
            _windowHandle == nint.Zero ||
            Interlocked.Exchange(ref _placementRefreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbar == nint.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
            {
                return;
            }

            var dpi = NativeMethods.GetDpiForWindow(taskbar);
            var scale = dpi > 0 ? dpi / 96d : 1d;
            var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
            var playerWidth = (int)Math.Ceiling(PlayerRoot.Width * scale);
            var placement = await _taskbarPlacementService.FindBestLeftAsync(
                taskbar,
                taskbarRect,
                playerWidth,
                margin);

            var hasReliablePlacement = placement.HasValue &&
                (_taskbarSettings.Alignment != TaskbarAlignment.Left ||
                    placement.Value.OccupiedElementCount > 0);
            if (_placementSettings.AutomaticPlacement && hasReliablePlacement)
            {
                _automaticLeft = placement!.Value.Left;
                var taskbarSettings = TaskbarSettingsService.Read();
                _taskbarSettings = taskbarSettings;
                var cachedSettings = _placementSettings with
                {
                    CachedAutomaticOffsetDip = (int)Math.Round(
                        (placement.Value.Left - taskbarRect.Left) / scale),
                    CachedTaskbarWidthDip = (int)Math.Round(taskbarRect.Width / scale),
                    CachedPlayerWidthDip = (int)Math.Round(PlayerRoot.Width),
                    CachedTaskbarAlignment = taskbarSettings.Alignment == TaskbarAlignment.Unknown
                        ? null
                        : taskbarSettings.Alignment
                };
                if (cachedSettings != _placementSettings)
                {
                    _placementSettings = cachedSettings;
                    SavePlacementSettings(showError: false);
                }

                PositionOverTaskbar(force: true);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _placementRefreshInProgress, 0);
        }
    }

    private void CollapseWhenPointerLeavesWindow()
    {
        if (!_isExpanded || _isMenuOpen || _isDragging ||
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

        ConnectionMenuItem.Header = snapshot.IsConnected
            ? $"{snapshot.SourceName}：{snapshot.Title}"
            : "等待媒体播放";
        ShowSourceMenuItem.Header = $"显示 {snapshot.SourceName}";
        ShowSourceMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.SourceId);
        _trayIconService?.UpdateTooltip(
            snapshot.IsConnected
                ? $"AF Shell · {snapshot.SourceName}：{snapshot.Title} - {snapshot.Artist}"
                : "AF Shell · Media Bar - 等待媒体播放");
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
        if (!_isMenuOpen && !_isDragging && !PlayerRoot.IsMouseOver)
        {
            SetExpanded(expanded: false, animate: true);
        }
    }

    private void SetExpanded(bool expanded, bool animate)
    {
        _isExpanded = expanded;
        ControlsHost.IsHitTestVisible = expanded;

        var duration = animate
            ? new Duration(TimeSpan.FromMilliseconds(220))
            : new Duration(TimeSpan.Zero);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        InfoHost.BeginAnimation(
            FrameworkElement.WidthProperty,
            CreateAnimation(
                expanded ? ExpandedInfoWidth : CollapsedInfoWidth,
                duration,
                easing));
        ControlsHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(expanded ? 1 : 0, duration, easing));
        ControlsTransform.BeginAnimation(
            TranslateTransform.XProperty,
            CreateAnimation(expanded ? 0 : 8, duration, easing));
        TitleTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(expanded ? -8 : 0, duration, easing));
        ArtistTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(expanded ? 0 : 3, duration, easing));
        ArtistText.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(expanded ? 1 : 0, duration, easing));
        ScheduleMarqueeUpdate();
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

    private async void ScheduleMarqueeUpdate()
    {
        var version = Interlocked.Increment(ref _marqueeVersion);
        await Task.Delay(260);
        if (version != _marqueeVersion || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        await Dispatcher.InvokeAsync(UpdateMarquees, DispatcherPriority.Render);
    }

    private void UpdateMarquees()
    {
        UpdateMarquee(TitleText, TitleViewport, TitleTransform);
        UpdateMarquee(ArtistText, ArtistViewport, ArtistTransform);
    }

    private static void UpdateMarquee(
        System.Windows.Controls.TextBlock text,
        FrameworkElement viewport,
        TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = text.DesiredSize.Width - viewport.ActualWidth;
        if (overflow <= 2 || viewport.ActualWidth <= 0)
        {
            return;
        }

        var travelSeconds = Math.Max(3, overflow / 22d);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -overflow,
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
        _lastMetricsSnapshot = _systemMetricsService.Sample();
        var values = BuildMetricValues(_lastMetricsSnapshot);
        if (values.Count == 0)
        {
            MetricsText.Text = string.Empty;
            MetricsHost.Visibility = Visibility.Collapsed;
            MetricsDivider.Visibility = Visibility.Collapsed;
            PlayerRoot.Width = 254;
            return;
        }

        MetricsHost.Visibility = Visibility.Visible;
        MetricsDivider.Visibility = Visibility.Visible;
        PlayerRoot.Width = 349;
        if (advanceCycle)
        {
            _metricCycleIndex = (_metricCycleIndex + 1) % values.Count;
        }
        else
        {
            _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, values.Count - 1);
        }

        SetMetricText(values[_metricCycleIndex], advanceCycle);
    }

    private List<string> BuildMetricValues(SystemMetricsSnapshot sample)
    {
        var values = new List<string>();
        if (_metricSettings.ShowSystemMemory)
        {
            values.Add($"MEM {sample.SystemMemoryPercent}%");
        }

        if (_metricSettings.ShowSystemCpu)
        {
            values.Add($"CPU {(sample.SystemCpuPercent is int cpu ? $"{cpu}%" : "--%")}");
        }

        if (_metricSettings.ShowProcessMemory)
        {
            var appMemory = sample.ProcessMemoryMegabytes < 1000
                ? $"{sample.ProcessMemoryMegabytes}M"
                : $"{sample.ProcessMemoryMegabytes / 1024d:0.0}G";
            values.Add($"APP {appMemory}");
        }

        return values;
    }

    private void SetMetricText(string text, bool animate)
    {
        if (!animate)
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
        SystemMemoryMenuItem.IsChecked = _metricSettings.ShowSystemMemory;
        SystemCpuMenuItem.IsChecked = _metricSettings.ShowSystemCpu;
        ProcessMemoryMenuItem.IsChecked = _metricSettings.ShowProcessMemory;
        _metricCycleIndex = 0;
        _metricCycleTicks = 0;
        UpdateMetrics(advanceCycle: false);
        _ = RefreshAutomaticPlacementAsync();
    }

    private void MetricSetting_OnClick(object sender, RoutedEventArgs e)
    {
        _metricSettings = new MetricSettings(
            SystemMemoryMenuItem.IsChecked,
            SystemCpuMenuItem.IsChecked,
            ProcessMemoryMenuItem.IsChecked);
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
            await RefreshAutomaticPlacementAsync();
        }
        else
        {
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
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero ||
            !NativeMethods.GetWindowRect(taskbar, out var taskbarRect) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return _placementSettings.ManualOffsetDip;
        }

        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        var scale = dpi > 0 ? dpi / 96d : 1d;
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

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar != nint.Zero &&
            NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
        {
            var dpi = NativeMethods.GetDpiForWindow(taskbar);
            var scale = dpi > 0 ? dpi / 96d : 1d;
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
        _mouseHookService.Start();
        SetExpanded(expanded: true, animate: true);
        StartupMenuItem.IsChecked = StartupService.IsEnabled;
        ApplyPlacementSettings();
        ApplyMetricSettings();
    }

    private void PlayerMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = false;
        _mouseHookService.Stop();
        ScheduleCollapse();
    }

    private void MouseHook_OnMouseButtonPressed(NativeMethods.Point point)
    {
        if (!_isMenuOpen ||
            IsPointInsideWindow(_windowHandle, point) ||
            IsPointInsideContextMenu(point))
        {
            return;
        }

        PlayerMenu.IsOpen = false;
    }

    private bool IsPointInsideContextMenu(NativeMethods.Point point)
    {
        var source = PresentationSource.FromVisual(PlayerMenu) as HwndSource;
        return source is not null && IsPointInsideWindow(source.Handle, point);
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
        NativeMethods.SetForegroundWindow(_windowHandle);
        PlayerMenu.Placement = PlacementMode.MousePoint;
        PlayerMenu.PlacementTarget = this;
        PlayerMenu.IsOpen = true;
    }

    private void TrayIcon_OnDoubleClicked(object? sender, EventArgs e)
    {
        ShowSelectedMediaSource();
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
        Close();
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

        if (_trayIconService?.HandleWindowMessage(message, wParam, lParam) == true)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }
}
