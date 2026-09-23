using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 固定顶部的媒体岛；窗口只承载一个连续变形的表面，不拥有媒体会话或音频采集。
/// Fixed top-center media island. One morphing surface consumes the existing media and audio services.
/// </summary>
public partial class DynamicIslandWindow : Window
{
    private const double HostWidth = 420;
    private const double HostHeight = 196;
    private const double SurfaceTop = 4;
    private const double TopInset = 12;
    private const double GestureSlop = 8;
    private const double SwipeDistance = 24;
    private readonly TaskbarWindowViewModel _viewModel;
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioMonitorService _audioMonitorService;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly DispatcherTimer _holdTimer;
    private readonly DispatcherTimer _environmentTimer;
    private readonly DispatcherTimer _presentationTimer;
    private readonly float[] _spectrumBands = new float[9];
    private readonly double[] _activityTargets = new double[5];
    private readonly double[] _activityLevels = new double[5];
    private readonly System.Windows.Shapes.Rectangle[] _activityBars;
    private MediaSnapshot _latestSnapshot = MediaSnapshot.Disconnected;
    private HwndSource? _windowSource;
    private TouchDevice? _activeTouch;
    private TouchDevice? _seekTouch;
    private Point _pointerOrigin;
    private bool _pointerDown;
    private bool _pointerCancelled;
    private bool _holdTriggered;
    private bool _expandedAtPress;
    private bool _wantsExpanded;
    private bool _isClosing;
    private bool _fullscreenSuppressed;
    private bool _placementQueued;
    private bool _isSeeking;
    private bool _updatingProgress;
    private bool _escapeWasDown;
    private string? _seekIdentity;
    private string? _monitorDeviceId;
    private double _effectiveScale = 1;
    private long _lastProgressUpdate;
    private long _holdStartedTimestamp;
    private double _holdExpansionStart;

    private double HoldElapsedSeconds => _holdStartedTimestamp == 0
        ? 0
        : Stopwatch.GetElapsedTime(_holdStartedTimestamp).TotalSeconds;

    private bool IsPreviewingHold => _pointerDown && !_pointerCancelled && !_holdTriggered &&
        !_expandedAtPress && !_wantsExpanded && _latestSnapshot.IsConnected && !_fullscreenSuppressed;

    public DynamicIslandWindow(
        TaskbarWindowViewModel viewModel,
        WindowAppearanceService appearanceService,
        IDisplayMonitorService displayMonitorService,
        MediaSessionService mediaSessionService,
        AudioMonitorService audioMonitorService)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        _viewModel = viewModel;
        _mediaSessionService = mediaSessionService;
        _audioMonitorService = audioMonitorService;
        _displayMonitorService = displayMonitorService;
        _activityBars = [ActivityBar0, ActivityBar1, ActivityBar2, ActivityBar3, ActivityBar4];
        DataContext = viewModel;
        ContextMenuHelper.AttachOutsideClickDismissal(PlayerMenu);
        appearanceService.Attach(PlayerMenu, this);
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = DynamicIslandHoldPolicy.HoldDuration };
        _holdTimer.Tick += HoldTimer_Tick;
        _environmentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _environmentTimer.Tick += EnvironmentTimer_Tick;
        _presentationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _presentationTimer.Tick += PresentationTimer_Tick;
        Loaded += Window_Loaded;
        IsVisibleChanged += Window_IsVisibleChanged;
        SystemParameters.StaticPropertyChanged += SystemParameters_Changed;
        DrawIsland();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutSettings(LayoutOrientationMode.Horizontal);
        ApplyAppearanceSettings();
        RefreshEnvironmentVisibility();
        _environmentTimer.Start();
        UpdatePresentationTimer();
        RetargetIsland();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowProc);
        QueuePlacement();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DPICHANGED or NativeMethods.WM_DISPLAYCHANGE)
            QueuePlacement();
        else if (msg == 0x0021) // WM_MOUSEACTIVATE: media controls must not activate their host.
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE
        }
        return IntPtr.Zero;
    }

    /// <summary>媒体变化不改变用户选择的展开状态。 / Media updates do not override the user's presentation state.</summary>
    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (_isClosing)
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
            return;
        }

        var previousIdentity = TrackChangeNotificationPolicy.CreateIdentity(_latestSnapshot);
        _latestSnapshot = snapshot;
        if (_isSeeking && previousIdentity != TrackChangeNotificationPolicy.CreateIdentity(snapshot))
            CancelSeek();
        if (!snapshot.IsConnected)
        {
            CancelPointer();
            CancelSeek();
            _wantsExpanded = false;
            Array.Clear(_activityTargets);
        }
        else
        {
            TitleText.Text = snapshot.Title;
            ArtistText.Text = snapshot.Artist;
            ArtworkImage.Source = snapshot.Artwork;
            ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        }
        PreviousButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        SeekSlider.IsEnabled = snapshot.IsConnected && snapshot.CanSeek && snapshot.Duration > 0;
        PlayGlyph.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        SetTransportHelp(PreviousButton, "Island.Accessibility.Previous", snapshot.IsConnected && snapshot.CanSkipPrevious);
        SetTransportHelp(NextButton, "Island.Accessibility.Next", snapshot.IsConnected && snapshot.CanSkipNext);
        SetTransportHelp(PlayPauseButton, snapshot.IsPlaying ? "Island.Accessibility.Pause" : "Island.Accessibility.Play",
            snapshot.IsConnected && snapshot.CanPlayPause);
        if (!snapshot.IsPlaying)
            Array.Clear(_activityTargets);
        UpdateProgress();
        if (!IsLoaded)
            Show();
        RefreshEnvironmentVisibility();
        UpdatePresentationTimer();
        RetargetIsland();
    }

    private static void SetTransportHelp(Button button, string labelKey, bool supported)
    {
        button.SetResourceReference(AutomationProperties.NameProperty, "Loc." + labelKey);
        button.SetResourceReference(ToolTipProperty, "Loc." + (supported ? labelKey : "Island.Transport.Unavailable"));
        if (supported)
            button.ClearValue(AutomationProperties.HelpTextProperty);
        else
            button.SetResourceReference(AutomationProperties.HelpTextProperty, "Loc.Island.Transport.Unavailable");
    }

    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing)
            return;
        SessionsMenuItem.Items.Clear();
        foreach (var option in options)
        {
            SessionsMenuItem.Items.Add(new MenuItem
            {
                Header = option.DisplayName,
                IsCheckable = true,
                IsChecked = option.IsSelected,
                Command = _viewModel.SelectMediaSessionCommand,
                CommandParameter = option.Key
            });
        }
    }

    /// <summary>灵动岛只使用独立的等比尺寸和显示器设置。 / The island ignores taskbar orientation and sizing.</summary>
    public void ApplyLayoutSettings(LayoutOrientationMode mode)
    {
        if (_isClosing)
            return;
        PlaceOnTargetMonitor();
        DrawIsland();
    }

    public void ApplyAppearanceSettings()
    {
        if (_isClosing)
            return;
        // The island remains black in both themes; system high contrast takes precedence for accessibility.
        IslandBody.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.Black;
        TitleText.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.White;
        ArtistText.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(157, 157, 163));
        Resources["IslandPrimaryBrush"] = TitleText.Foreground;
        Resources["IslandSecondaryBrush"] = ArtistText.Foreground;
        Resources["IslandTrackBrush"] = SystemParameters.HighContrast ? SystemColors.GrayTextBrush : new SolidColorBrush(Color.FromArgb(69, 255, 255, 255));
        Resources["IslandActivityBrush"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 83, 110));
        AutomationProperties.SetHelpText(IslandBody, Translations.Get("Island.Accessibility.Expand"));
        RetargetIsland();
    }

    public void RefreshDisplayEnvironment() => QueuePlacement();

    private void QueuePlacement()
    {
        if (_isClosing || _placementQueued)
            return;
        _placementQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _placementQueued = false;
            if (_isClosing)
                return;
            PlaceOnTargetMonitor();
            DrawIsland();
            RefreshEnvironmentVisibility();
        }, DispatcherPriority.Loaded);
    }

    private void PlaceOnTargetMonitor()
    {
        var monitor = _displayMonitorService.ResolveFixedMonitor(SettingsManager.Current.DynamicIslandMonitorDeviceId);
        var dpiX = monitor?.DpiX is > 0 ? monitor.DpiX / 96d : VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dpiY = monitor?.DpiY is > 0 ? monitor.DpiY / 96d : VisualTreeHelper.GetDpi(this).DpiScaleY;
        var physicalArea = monitor?.WorkArea ?? new Rect(
            SystemParameters.WorkArea.X * dpiX, SystemParameters.WorkArea.Y * dpiY,
            SystemParameters.WorkArea.Width * dpiX, SystemParameters.WorkArea.Height * dpiY);
        _monitorDeviceId = monitor?.DeviceId;
        var requestedScale = SettingsManager.Current.DynamicIslandScalePercent / 100;
        var availableScale = Math.Min(physicalArea.Width / (HostWidth * dpiX),
            Math.Max(1, physicalArea.Height - TopInset * dpiY) / (HostHeight * dpiY));
        _effectiveScale = Math.Max(0.1, Math.Min(requestedScale, availableScale));
        Width = IslandViewport.Width = HostWidth * _effectiveScale;
        Height = IslandViewport.Height = HostHeight * _effectiveScale;
        var workAreaDip = new Rect(physicalArea.X / dpiX, physicalArea.Y / dpiY,
            physicalArea.Width / dpiX, physicalArea.Height / dpiY);
        var position = DynamicIslandPositionCalculator.GetCenteredPosition(workAreaDip, Width, Height,
            Math.Max(0, TopInset - SurfaceTop * _effectiveScale));
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Left = position.X;
            Top = position.Y;
            return;
        }
        var x = (int)Math.Round(position.X * dpiX);
        var y = (int)Math.Round(position.Y * dpiY);
        var width = (int)Math.Ceiling(Width * dpiX);
        var height = (int)Math.Ceiling(Height * dpiY);
        if (!NativeMethods.GetWindowRect(hwnd, out var current) || current.Left != x || current.Top != y ||
            current.Right - current.Left != width || current.Bottom - current.Top != height)
        {
            NativeMethods.SetWindowPos(hwnd, -1, x, y, width, height, 0x0010); // HWND_TOPMOST, SWP_NOACTIVATE
        }
    }

    private void RefreshEnvironmentVisibility()
    {
        if (_isClosing || !IsLoaded)
            return;
        var suppressed = _displayMonitorService.IsForegroundWindowFullscreen(_monitorDeviceId);
        if (suppressed == _fullscreenSuppressed)
            return;
        _fullscreenSuppressed = suppressed;
        if (suppressed)
        {
            CancelPointer();
            CancelSeek();
            PlayerMenu.IsOpen = false;
            Visibility = Visibility.Hidden;
            StopRendering();
        }
        else
        {
            Visibility = Visibility.Visible;
            SnapPresentation();
            DrawIsland();
        }
        UpdatePresentationTimer();
    }

    private void EnvironmentTimer_Tick(object? sender, EventArgs e) => RefreshEnvironmentVisibility();

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
            StopRendering();
        UpdatePresentationTimer();
    }

    private void SystemParameters_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
            Dispatcher.BeginInvoke(ApplyAppearanceSettings);
    }

    private void SetExpanded(bool expanded)
    {
        expanded &= _latestSnapshot.IsConnected;
        if (_isClosing || _wantsExpanded == expanded)
            return;
        _wantsExpanded = expanded;
        _escapeWasDown = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;
        if (!expanded)
            CancelSeek();
        UpdateProgress();
        UpdatePresentationTimer();
        RetargetIsland();
    }

    private void BeginPointer(Point point, TouchDevice? touch)
    {
        if (_pointerDown || _isSeeking || !_latestSnapshot.IsConnected || PlayerMenu.IsOpen)
            return;
        _pointerOrigin = point;
        _activeTouch = touch;
        _pointerDown = true;
        _pointerCancelled = false;
        _holdTriggered = false;
        _expandedAtPress = _wantsExpanded;
        _holdStartedTimestamp = Stopwatch.GetTimestamp();
        _holdExpansionStart = Math.Clamp(_expansionSpring.Value, 0, 1);
        if (touch is null)
            Mouse.Capture(IslandBody);
        else
            touch.Capture(IslandBody);
        _holdTimer.Stop();
        _holdTimer.Interval = DynamicIslandHoldPolicy.HoldDuration;
        _holdTimer.Start();
        RetargetIsland();
    }

    private void HoldTimer_Tick(object? sender, EventArgs e)
    {
        if (TryCompleteHold())
            return;
        if (!_pointerDown || _pointerCancelled || _holdTriggered || _fullscreenSuppressed || !_latestSnapshot.IsConnected)
        {
            _holdTimer.Stop();
            return;
        }
        _holdTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.001,
            DynamicIslandHoldPolicy.HoldDuration.TotalSeconds - HoldElapsedSeconds));
    }

    private bool TryCompleteHold()
    {
        if (!_pointerDown || _pointerCancelled || _holdTriggered || _fullscreenSuppressed ||
            !_latestSnapshot.IsConnected || !DynamicIslandHoldPolicy.ShouldCommit(HoldElapsedSeconds))
            return false;
        _holdTimer.Stop();
        _holdTriggered = true;
        SetExpanded(true);
        RetargetIsland();
        return true;
    }

    private void MovePointer(Point point, bool touch)
    {
        if (!_pointerDown || _holdTriggered)
            return;
        var delta = point - _pointerOrigin;
        if (touch && Math.Abs(delta.X) >= SwipeDistance && Math.Abs(delta.X) > Math.Abs(delta.Y) * 1.5)
        {
            var centerOffset = _pointerOrigin.X - IslandBody.Width / 2;
            var side = Math.Abs(centerOffset) <= GestureSlop ? Math.Sign(delta.X) : Math.Sign(centerOffset);
            _holdTriggered = true;
            _holdTimer.Stop();
            SetExpanded(side * delta.X > 0);
            RetargetIsland();
            return;
        }
        if (delta.Length > GestureSlop)
        {
            _pointerCancelled = true;
            _holdTimer.Stop();
            RetargetIsland();
        }
    }

    private void EndPointer(Point point)
    {
        if (!_holdTriggered && (point - _pointerOrigin).Length > GestureSlop)
            _pointerCancelled = true;
        // Release can be dispatched before the deadline timer; classify by actual elapsed time, not callback order.
        TryCompleteHold();
        var activate = _pointerDown && !_pointerCancelled && !_holdTriggered && !_expandedAtPress &&
            IslandClip.FillContains(point) && _latestSnapshot.IsConnected && DynamicIslandHoldPolicy.IsTap(HoldElapsedSeconds);
        CancelPointer();
        if (activate && _viewModel.ActivateMediaSourceCommand.CanExecute(null))
            _viewModel.ActivateMediaSourceCommand.Execute(null);
    }

    private void CancelPointer()
    {
        _holdTimer.Stop();
        _pointerDown = false;
        _pointerCancelled = true;
        _holdStartedTimestamp = 0;
        _holdExpansionStart = 0;
        var touch = _activeTouch;
        _activeTouch = null;
        if (Mouse.Captured == IslandBody)
            Mouse.Capture(null);
        if (touch?.Captured == IslandBody)
            touch.Capture(null);
        if (!_isClosing)
            RetargetIsland();
    }

    private void Island_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null || IsInteractiveControl(e.OriginalSource as DependencyObject))
            return;
        BeginPointer(e.GetPosition(IslandBody), null);
        e.Handled = true;
    }

    private void Island_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeTouch is null)
            MovePointer(e.GetPosition(IslandBody), false);
    }

    private void Island_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown || _activeTouch is not null)
            return;
        EndPointer(e.GetPosition(IslandBody));
        e.Handled = true;
    }

    private void Island_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_pointerDown && _activeTouch is null && Mouse.Captured != IslandBody)
            CancelPointer();
    }

    private void Island_TouchDown(object sender, TouchEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
            return;
        BeginPointer(e.GetTouchPoint(IslandBody).Position, e.TouchDevice);
        e.Handled = true;
    }

    private void Island_TouchMove(object sender, TouchEventArgs e)
    {
        if (e.TouchDevice != _activeTouch)
            return;
        MovePointer(e.GetTouchPoint(IslandBody).Position, true);
        e.Handled = true;
    }

    private void Island_TouchUp(object sender, TouchEventArgs e)
    {
        if (e.TouchDevice != _activeTouch)
            return;
        EndPointer(e.GetTouchPoint(IslandBody).Position);
        e.Handled = true;
    }

    private void Island_LostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_pointerDown && e.TouchDevice == _activeTouch)
            CancelPointer();
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or RangeBase or Thumb)
                return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    /// <summary>使用既有全局鼠标通知收起展开层，仍让原始点击到达桌面。 / Dismiss without swallowing the underlying desktop click.</summary>
    public void CloseContextMenuIfOutside(int screenX, int screenY)
    {
        if (_isClosing || !IsVisible)
            return;
        ContextMenuHelper.CloseIfOutside(PlayerMenu, screenX, screenY);
        if (PlayerMenu.IsOpen || _pointerDown || !_wantsExpanded)
            return;
        var point = IslandBody.PointFromScreen(new Point(screenX, screenY));
        if (!IslandClip.FillContains(point))
            SetExpanded(false);
    }

    internal void ClosePlayerMenu() => PlayerMenu.IsOpen = false;

    private void UpdatePresentationTimer()
    {
        if (_isClosing || !IsVisible || _fullscreenSuppressed || (!_latestSnapshot.IsPlaying && !_wantsExpanded))
            _presentationTimer.Stop();
        else
            _presentationTimer.Start();
    }

    private void PresentationTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || !IsVisible || _fullscreenSuppressed)
            return;
        var escapeDown = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0;
        if (_wantsExpanded && escapeDown && !_escapeWasDown)
        {
            ClosePlayerMenu();
            CancelPointer();
            SetExpanded(false);
        }
        _escapeWasDown = escapeDown;
        var now = Stopwatch.GetTimestamp();
        if (_wantsExpanded && Stopwatch.GetElapsedTime(_lastProgressUpdate, now).TotalMilliseconds >= 200)
        {
            _lastProgressUpdate = now;
            UpdateProgress();
        }
        var hasSample = _latestSnapshot.IsPlaying && MotionPolicy.ResolveCurrent().UseDecorativeEffects &&
            _audioMonitorService.GetSpectrum(_spectrumBands, _spectrumBands.Length);
        var changed = false;
        for (var i = 0; i < _activityTargets.Length; i++)
        {
            var value = 0d;
            var first = i * _spectrumBands.Length / _activityTargets.Length;
            var last = (i + 1) * _spectrumBands.Length / _activityTargets.Length;
            if (hasSample)
            {
                for (var band = first; band < last; band++)
                    value += Math.Clamp(_spectrumBands[band], 0, 1);
                value /= last - first;
            }
            if (Math.Abs(value - _activityTargets[i]) > 0.005)
            {
                _activityTargets[i] = value;
                changed = true;
            }
        }
        if (changed)
            RetargetIsland();
    }

    private void UpdateProgress()
    {
        if (_isSeeking)
            return;
        _updatingProgress = true;
        var duration = double.IsFinite(_latestSnapshot.Duration) ? Math.Max(0, _latestSnapshot.Duration) : 0;
        SeekSlider.Maximum = Math.Max(1, duration);
        var position = TaskbarExperiencePolicy.GetPosition(_latestSnapshot, DateTimeOffset.UtcNow);
        SeekSlider.Value = double.IsFinite(position) ? Math.Clamp(position, 0, duration) : 0;
        UpdateTimeLabels(SeekSlider.Value);
        _updatingProgress = false;
    }

    private void UpdateTimeLabels(double position)
    {
        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = "−" + FormatTime(Math.Max(0, _latestSnapshot.Duration - position));
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 359999) : 0);
        return value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    private bool BeginSeek()
    {
        if (!SeekSlider.IsEnabled || !_latestSnapshot.IsConnected || !_latestSnapshot.CanSeek)
            return false;
        _isSeeking = true;
        _seekIdentity = TrackChangeNotificationPolicy.CreateIdentity(_latestSnapshot);
        return true;
    }

    private void UpdateSeekFromPoint(Point point)
    {
        if (SeekSlider.Template.FindName("PART_Track", SeekSlider) is not Track track)
            return;
        var value = track.ValueFromPoint(SeekSlider.TranslatePoint(point, track));
        if (double.IsFinite(value))
            SeekSlider.Value = Math.Clamp(value, SeekSlider.Minimum, SeekSlider.Maximum);
    }

    private void SeekSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null || !BeginSeek())
            return;
        SeekSlider.CaptureMouse();
        UpdateSeekFromPoint(e.GetPosition(SeekSlider));
        e.Handled = true;
    }

    private void SeekSlider_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSeeking && _seekTouch is null && Mouse.Captured == SeekSlider)
        {
            UpdateSeekFromPoint(e.GetPosition(SeekSlider));
            e.Handled = true;
        }
    }

    private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSeeking || _seekTouch is not null)
            return;
        UpdateSeekFromPoint(e.GetPosition(SeekSlider));
        CommitSeek();
        e.Handled = true;
    }

    private void SeekSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isSeeking && _seekTouch is null && Mouse.Captured != SeekSlider)
            CancelSeek();
    }

    private void SeekSlider_TouchDown(object sender, TouchEventArgs e)
    {
        if (_isSeeking || !BeginSeek())
            return;
        _seekTouch = e.TouchDevice;
        _seekTouch.Capture(SeekSlider);
        UpdateSeekFromPoint(e.GetTouchPoint(SeekSlider).Position);
        e.Handled = true;
    }

    private void SeekSlider_TouchMove(object sender, TouchEventArgs e)
    {
        if (!_isSeeking || e.TouchDevice != _seekTouch)
            return;
        UpdateSeekFromPoint(e.GetTouchPoint(SeekSlider).Position);
        e.Handled = true;
    }

    private void SeekSlider_TouchUp(object sender, TouchEventArgs e)
    {
        if (!_isSeeking || e.TouchDevice != _seekTouch)
            return;
        UpdateSeekFromPoint(e.GetTouchPoint(SeekSlider).Position);
        CommitSeek();
        e.Handled = true;
    }

    private void SeekSlider_LostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_isSeeking && e.TouchDevice == _seekTouch)
            CancelSeek();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingProgress)
            return;
        UpdateTimeLabels(e.NewValue);
        // Pointer capture batches a drag into one seek; keyboard/UI Automation changes are discrete seeks.
        if (!_isSeeking && BeginSeek())
            CommitSeek();
    }

    private async void CommitSeek()
    {
        if (!_isSeeking)
            return;
        var identity = _seekIdentity;
        var position = SeekSlider.Value;
        _isSeeking = false;
        _seekIdentity = null;
        ReleaseSeekCapture();
        if (identity != TrackChangeNotificationPolicy.CreateIdentity(_latestSnapshot) || !_latestSnapshot.CanSeek)
            return;
        try
        {
            await _mediaSessionService.SeekAsync(position);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[DynamicIsland] Seek failed: {exception}");
            if (!_isClosing)
                UpdateProgress();
        }
    }

    private void CancelSeek()
    {
        _isSeeking = false;
        _seekIdentity = null;
        ReleaseSeekCapture();
    }

    private void ReleaseSeekCapture()
    {
        var touch = _seekTouch;
        _seekTouch = null;
        if (Mouse.Captured == SeekSlider)
            Mouse.Capture(null);
        if (touch?.Captured == SeekSlider)
            touch.Capture(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        CancelPointer();
        CancelSeek();
        StopRendering();
        _holdTimer.Stop();
        _environmentTimer.Stop();
        _presentationTimer.Stop();
        _holdTimer.Tick -= HoldTimer_Tick;
        _environmentTimer.Tick -= EnvironmentTimer_Tick;
        _presentationTimer.Tick -= PresentationTimer_Tick;
        SystemParameters.StaticPropertyChanged -= SystemParameters_Changed;
        _windowSource?.RemoveHook(WindowProc);
        _windowSource = null;
        PlayerMenu.IsOpen = false;
        ArtworkImage.Source = null;
        base.OnClosed(e);
    }
}
