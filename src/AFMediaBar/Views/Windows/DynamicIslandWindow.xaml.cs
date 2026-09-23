using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 灵动岛窗口：可自由拖动，贴边后在暂停时收起并支持悬停展开。
/// Dynamic island window: can be dragged freely and retracts to its selected edge while paused.
/// </summary>
/// <summary>
/// 灵动岛媒体宿主窗口，负责边缘状态、位置和媒体呈现适配。
/// Dynamic-island media host responsible for edge state, positioning, and media presentation adaptation.
/// </summary>
public partial class DynamicIslandWindow : Window
{
    private const double EdgeRevealDip = 5;
    private const double EdgeDockThresholdDip = 28;
    private readonly TaskbarWindowViewModel _viewModel;
    private readonly DispatcherTimer _sizeAnimationTimer;
    private readonly DispatcherTimer _foregroundSamplingTimer;
    private readonly AdaptiveForegroundSamplingSession _foregroundSamplingSession;
    private bool _isExpanded;
    private bool _isClosing;
    private bool _isDragging;
    private bool _dpiRecoveryQueued;
    private HwndSource? _windowSource;
    private Point? _dpiNormalizedCenter;
    private LayoutOrientation? _appliedOrientation;
    private double _appliedLengthScalePercent = double.NaN;
    private double _appliedThicknessScalePercent = double.NaN;

    /// <summary>
    /// 创建灵动岛媒体宿主窗口。
    /// Creates the dynamic-island media host window.
    /// </summary>
    public DynamicIslandWindow(
        TaskbarWindowViewModel viewModel,
        WindowAppearanceService appearanceService,
        ScreenBackgroundSampler screenBackgroundSampler)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        _viewModel = viewModel;
        _foregroundSamplingSession = new AdaptiveForegroundSamplingSession(
            screenBackgroundSampler,
            Dispatcher,
            GetAdaptiveForegroundSampleBounds,
            MediaControl.ApplyAdaptiveForegroundDecision);
        DataContext = new DynamicIslandDataContext(viewModel);
        ContextMenuHelper.AttachOutsideClickDismissal(PlayerMenu);
        appearanceService.Attach(PlayerMenu, this);
        MediaControl.TogglePlayPauseRequested += MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested += MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested += MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested += MediaControl_ActivateSourceRequested;
        MediaControl.DesiredSizeChanged += MediaControl_DesiredSizeChanged;
        _sizeAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sizeAnimationTimer.Tick += (_, _) => AdvanceSizeAnimation();
        _foregroundSamplingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _foregroundSamplingTimer.Tick += (_, _) => _foregroundSamplingSession.RequestRefresh();
        Loaded += (_, _) =>
        {
            RestoreSavedPosition();
            ApplyLayoutSettings(SettingsManager.Current.LayoutOrientationMode);
            MediaControl.ApplyAppearanceSettings();
            SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
            _foregroundSamplingTimer.Start();
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
        };
    }

    /// <summary>初始化灵动岛窗口句柄钩子。/ Initializes the dynamic-island window hook.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = (HwndSource)PresentationSource.FromDependencyObject(this);
        _windowSource.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DPICHANGED or NativeMethods.WM_DISPLAYCHANGE)
        {
            _foregroundSamplingSession.Invalidate(clearDecision: false);
            ScheduleDpiRecovery(hwnd);
        }

        return IntPtr.Zero;
    }

    private void ScheduleDpiRecovery(IntPtr hwnd)
    {
        if (_isClosing || _dpiRecoveryQueued)
            return;

        _dpiRecoveryQueued = true;
        _sizeAnimationTimer.Stop();
        StopPositionAnimationAtCurrentPosition();
        PlayerMenu.IsOpen = false;

        if (NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            var workArea = MonitorUtil.GetMonitor(hwnd).workArea;
            if (workArea.Width > 0 && workArea.Height > 0)
            {
                var centerX = (windowRect.Left + windowRect.Right) / 2.0;
                var centerY = (windowRect.Top + windowRect.Bottom) / 2.0;
                _dpiNormalizedCenter = new Point(
                    Math.Clamp((centerX - workArea.Left) / workArea.Width, 0, 1),
                    Math.Clamp((centerY - workArea.Top) / workArea.Height, 0, 1));
            }
        }

        Dispatcher.BeginInvoke(RecoverAfterDpiChange, DispatcherPriority.ContextIdle);
    }

    private void RecoverAfterDpiChange()
    {
        _dpiRecoveryQueued = false;
        if (_isClosing)
            return;

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        UpdateLayout();

        var workArea = GetCurrentWorkArea();
        var center = _dpiNormalizedCenter ?? new Point(0.5, 0);
        _dpiNormalizedCenter = null;
        var dockedEdge = SettingsManager.Current.DynamicIslandEdgeDocked
            ? SettingsManager.Current.DynamicIslandEdge
            : (DynamicIslandEdge?)null;
        var restoredPosition = DynamicIslandPositionCalculator.GetDpiRestoredPosition(
            workArea,
            Width,
            Height,
            center,
            dockedEdge);
        SettingsManager.Current.DynamicIslandLeft = restoredPosition.X;
        SettingsManager.Current.DynamicIslandTop = restoredPosition.Y;
        SetPosition(_isExpanded ? restoredPosition : GetCollapsedPosition(), animated: false);
        MediaControl.RefreshDesiredSize();
        ApplyPendingSizeRequest();
    }

    /// <summary>
    /// 将不可变媒体快照转发到呈现控件，并在内容尺寸变化后保持当前边缘锚点。
    /// Applies an immutable media snapshot to the presentation control while preserving the current edge anchor after size changes.
    /// </summary>
    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (_isClosing)
            return;

        Dispatcher.Invoke(() =>
        {
            var wasVisible = Visibility == Visibility.Visible;
            if (!snapshot.IsConnected)
            {
                MediaControl.UpdateSongInfo(snapshot);
                MediaControl.ApplyAppearanceSettings();
                Visibility = Visibility.Visible;
                if (!wasVisible)
                    Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
                if (_isDragging)
                    return;
                if (SettingsManager.Current.DynamicIslandEdgeDocked && !IsCursorWithinWindow())
                    Collapse(animated: true);
                else
                    Expand(animated: true);
                return;
            }

            ApplyLayoutSettings(SettingsManager.Current.LayoutOrientationMode);
            MediaControl.UpdateSongInfo(snapshot);
            MediaControl.ApplyAppearanceSettings();
            Visibility = Visibility.Visible;
            if (!wasVisible)
                Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);

            if (_isDragging)
                return;

            if (snapshot.IsPlaying)
            {
                Expand(animated: true);
            }
            else
            {
                if (SettingsManager.Current.DynamicIslandEdgeDocked && !IsCursorWithinWindow())
                    Collapse(animated: true);
                else
                    Expand(animated: true);
            }
        });
    }

    /// <summary>
    /// 应用已规范化的布局方向，并重新测量窗口和输入区域。
    /// Applies the normalized layout orientation and remeasures the window and its input region.
    /// </summary>
    public void ApplyLayoutSettings(LayoutOrientationMode mode)
    {
        var orientation = mode == LayoutOrientationMode.Vertical
            ? LayoutOrientation.Vertical
            : LayoutOrientation.Horizontal;

        var lengthScalePercent = SettingsManager.Current.LayoutLengthScalePercent;
        var thicknessScalePercent = SettingsManager.Current.LayoutThicknessScalePercent;
        if (_appliedOrientation == orientation &&
            lengthScalePercent.Equals(_appliedLengthScalePercent) &&
            thicknessScalePercent.Equals(_appliedThicknessScalePercent))
            return;

        MediaControl.ApplyLayout(WindowMode.DynamicIsland, orientation);
        var canvas = MediaControl.CurrentLayout?.Canvas;
        if (canvas is null)
            return;

        _appliedOrientation = orientation;
        _appliedLengthScalePercent = lengthScalePercent;
        _appliedThicknessScalePercent = thicknessScalePercent;
        Width = canvas.Width;
        Height = canvas.Height;
        MediaControl.RefreshDesiredSize();

        if (!IsLoaded)
        {
            RestoreSavedPosition();
            return;
        }

        if (!_isDragging)
            SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
    }

    /// <summary>
    /// 在主题或外观设置变化后重新应用窗口材质及控件资源。
    /// Reapplies window material and control resources after theme or appearance settings change.
    /// </summary>
    public void ApplyAppearanceSettings()
    {
        MediaControl.ApplyAppearanceSettings();
        if (SettingsManager.Current.Appearance.PlayerForegroundMode == PlayerForegroundMode.Automatic &&
            SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent)
        {
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
        }
        else
        {
            _foregroundSamplingSession.Invalidate(clearDecision: true);
        }
    }

    private Int32Rect? GetAdaptiveForegroundSampleBounds()
    {
        if (_isClosing || _isDragging || _dpiRecoveryQueued || _positionAnimationActive ||
            _sizeAnimationTimer.IsEnabled || Visibility != Visibility.Visible ||
            SettingsManager.Current.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic ||
            SettingsManager.Current.DynamicIslandBackgroundMode != DynamicIslandBackgroundMode.Transparent)
        {
            return null;
        }

        return MediaControl.TryGetForegroundSampleBounds(out var bounds) ? bounds : null;
    }

    private void MediaControl_DesiredSizeChanged(object? sender, MediaBarSizeRequestEventArgs eventArgs)
    {
        var request = eventArgs.Request;
        if (_isClosing || _appliedOrientation is not { } orientation)
            return;

        // 拖动或收起/展开的位置动画期间保留最新请求，动画结束后再应用。
        // Keep the latest request during drag or position animation and apply it afterwards.
        if (_isDragging || _positionAnimationActive || _dpiRecoveryQueued)
        {
            _pendingSizeRequest = request;
            return;
        }

        ApplyDesiredSizeRequest(request, orientation);
    }

    /// <summary>在全局左键点击位于菜单外时关闭右键菜单。 / Closes the context menu after a global left click outside it.</summary>
    public void CloseContextMenuIfOutside(int screenX, int screenY) =>
        ContextMenuHelper.CloseIfOutside(PlayerMenu, screenX, screenY);

    /// <summary>关闭灵动岛媒体菜单。/ Closes the dynamic-island media menu.</summary>
    internal void ClosePlayerMenu() => PlayerMenu.IsOpen = false;

    /// <summary>
    /// 用最新会话列表重建灵动岛的媒体源菜单。
    /// Rebuilds the dynamic-island media-source menu from the latest session list.
    /// </summary>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        if (_isClosing)
            return;

        Dispatcher.Invoke(() =>
        {
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
        });
    }

    private void Expand(bool animated)
    {
        if (_isDragging)
            return;

        if (_sizeAnimationTimer.IsEnabled)
        {
            _isExpanded = true;
            Visibility = Visibility.Visible;
            return;
        }

        var target = GetExpandedPosition();
        if (_isExpanded && IsPositionTarget(target))
        {
            Visibility = Visibility.Visible;
            return;
        }

        _isExpanded = true;
        Visibility = Visibility.Visible;
        SetPosition(target, animated);
    }

    private void Collapse(bool animated)
    {
        if (_isDragging)
            return;

        if (_sizeAnimationTimer.IsEnabled)
        {
            _isExpanded = false;
            return;
        }

        var target = GetCollapsedPosition();
        if (!_isExpanded && IsPositionTarget(target))
            return;

        _isExpanded = false;
        SetPosition(target, animated);
    }

    private void Canvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left ||
            e.OriginalSource is DependencyObject source &&
            (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null || IsMediaAction(source)))
        {
            return;
        }

        _isDragging = true;
        _foregroundSamplingSession.Invalidate(clearDecision: false);
        StopPositionAnimationAtCurrentPosition();
        _isExpanded = true;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            _isDragging = false;
        }

        var edgeDocked = SaveDraggedPositionAndEdge();
        if (!MediaControl.IsPlaying && edgeDocked)
        {
            Collapse(animated: true);
            if (!_positionAnimationActive)
                ApplyPendingSizeRequest();
        }
        else
        {
            ApplyPendingSizeRequest();
        }
        Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isExpanded && !_isDragging)
            Expand(animated: true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!MediaControl.IsPlaying && !_isDragging && SettingsManager.Current.DynamicIslandEdgeDocked &&
            !IsCursorWithinWindow())
            Collapse(animated: true);
    }

    /// <summary>
    /// 使用系统屏幕光标判断指针是否仍在灵动岛窗口内。
    /// Uses the native screen cursor position to determine whether the pointer remains inside the island.
    /// </summary>
    private bool IsCursorWithinWindow()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
            return false;

        try
        {
            var cursorInWindow = PointFromScreen(new Point(cursor.X, cursor.Y));
            return cursorInWindow.X >= 0 && cursorInWindow.X <= ActualWidth &&
                   cursorInWindow.Y >= 0 && cursorInWindow.Y <= ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>释放灵动岛窗口资源和消息钩子。/ Releases dynamic-island resources and message hooks.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowProc);
            _windowSource = null;
        }
        _sizeAnimationTimer.Stop();
        _foregroundSamplingTimer.Stop();
        _foregroundSamplingSession.Dispose();
        BeginAnimation(TopProperty, null);
        BeginAnimation(LeftProperty, null);
        MediaControl.TogglePlayPauseRequested -= MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested -= MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested -= MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested -= MediaControl_ActivateSourceRequested;
        MediaControl.DesiredSizeChanged -= MediaControl_DesiredSizeChanged;
        base.OnClosed(e);
    }

    private void MediaControl_TogglePlayPauseRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.TogglePlayPauseCommand);

    private void MediaControl_SkipPreviousRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.SkipPreviousCommand);

    private void MediaControl_SkipNextRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.SkipNextCommand);

    private void MediaControl_ActivateSourceRequested(object? sender, EventArgs e) =>
        Execute(_viewModel.ActivateMediaSourceCommand);

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void SaveCurrentExpandedPosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
            return;

        var position = DynamicIslandPositionCalculator.ClampPosition(
            GetCurrentWorkArea(),
            Width,
            Height,
            new Point(Left, Top));
        SettingsManager.Current.DynamicIslandLeft = position.X;
        SettingsManager.Current.DynamicIslandTop = position.Y;
    }

    private bool SaveDraggedPositionAndEdge()
    {
        var workArea = GetCurrentWorkArea();
        var clampedPosition = DynamicIslandPositionCalculator.ClampPosition(
            workArea,
            Width,
            Height,
            new Point(Left, Top));
        SetPosition(clampedPosition, animated: false);

        SettingsManager.Current.DynamicIslandLeft = clampedPosition.X;
        SettingsManager.Current.DynamicIslandTop = clampedPosition.Y;
        var edge = DynamicIslandPositionCalculator.FindDockedEdge(
            clampedPosition.X,
            clampedPosition.Y,
            Width,
            Height,
            workArea,
            EdgeDockThresholdDip);
        SettingsManager.Current.DynamicIslandEdgeDocked = edge is not null;
        if (edge is { } dockedEdge)
            SettingsManager.Current.DynamicIslandEdge = dockedEdge;
        return edge is not null;
    }

    private void RestoreSavedPosition()
    {
        if (SettingsManager.Current.DynamicIslandLeft is { } savedLeft &&
            SettingsManager.Current.DynamicIslandTop is { } savedTop)
        {
            SetPosition(new Point(savedLeft, savedTop), animated: false);
        }
    }

    private Point GetExpandedPosition()
    {
        return DynamicIslandPositionCalculator.GetExpandedPosition(
            GetCurrentWorkArea(),
            Width,
            Height,
            SettingsManager.Current.DynamicIslandLeft,
            SettingsManager.Current.DynamicIslandTop);
    }

    private Point GetCollapsedPosition()
    {
        return DynamicIslandPositionCalculator.GetCollapsedPosition(
            GetCurrentWorkArea(),
            Width,
            Height,
            SettingsManager.Current.DynamicIslandEdge,
            EdgeRevealDip,
            SettingsManager.Current.DynamicIslandLeft,
            SettingsManager.Current.DynamicIslandTop);
    }

    private Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return SystemParameters.WorkArea;

        var physicalArea = MonitorUtil.GetMonitor(handle).workArea;
        var transformFromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var relativeTopLeft = transformFromDevice?.Transform(physicalArea.TopLeft) ?? physicalArea.TopLeft;
        var relativeBottomRight = transformFromDevice?.Transform(physicalArea.BottomRight) ?? physicalArea.BottomRight;
        return new Rect(
            relativeTopLeft.X,
            relativeTopLeft.Y,
            relativeBottomRight.X - relativeTopLeft.X,
            relativeBottomRight.Y - relativeTopLeft.Y);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsMediaAction(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "MediaAction" })
                return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private sealed class DynamicIslandDataContext
    {
        public TaskbarWindowViewModel ViewModel { get; }

        public DynamicIslandDataContext(TaskbarWindowViewModel viewModel) => ViewModel = viewModel;
    }
}
