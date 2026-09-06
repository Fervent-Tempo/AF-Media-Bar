using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 灵动岛窗口：可自由拖动，贴边后在暂停时收起并支持悬停展开。
/// Dynamic island window: can be dragged freely and retracts to its selected edge while paused.
/// </summary>
public partial class DynamicIslandWindow : Window
{
    private const double EdgeRevealDip = 5;
    private const double EdgeDockThresholdDip = 28;
    private const double PositionToleranceDip = 0.5;
    private const int PositionAnimationDurationMs = 260;
    private readonly MainWindowViewModel _viewModel;
    private bool _isExpanded;
    private bool _isClosing;
    private bool _isDragging;
    private LayoutOrientation? _appliedOrientation;
    private double _appliedLengthScalePercent = double.NaN;
    private double _appliedThicknessScalePercent = double.NaN;
    private Point? _positionAnimationTarget;
    private bool _positionAnimationActive;

    public DynamicIslandWindow(MainWindowViewModel viewModel)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = new MainWindowDataContext(viewModel);
        MediaControl.TogglePlayPauseRequested += MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested += MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested += MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested += MediaControl_ActivateSourceRequested;
        Loaded += (_, _) =>
        {
            RestoreSavedPosition();
            ApplyLayoutSettings(SettingsManager.Current.LayoutOrientationMode);
            MediaControl.ApplyWindowsTheme();
            SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
        };
    }

    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (_isClosing)
            return;

        Dispatcher.Invoke(() =>
        {
            if (!snapshot.IsConnected)
            {
                MediaControl.UpdateSongInfo(snapshot);
                MediaControl.ApplyWindowsTheme();
                Visibility = Visibility.Visible;
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
            MediaControl.ApplyWindowsTheme();
            Visibility = Visibility.Visible;

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

        if (!IsLoaded)
        {
            RestoreSavedPosition();
            return;
        }

        if (!_isDragging)
            SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
    }

    public void ApplyAppearanceSettings() => MediaControl.ApplyWindowsTheme();

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

        var target = GetCollapsedPosition();
        if (!_isExpanded && IsPositionTarget(target))
            return;

        _isExpanded = false;
        SetPosition(target, animated);
    }

    private void SetPosition(Point target, bool animated)
    {
        var currentLeft = Left;
        var currentTop = Top;
        if (double.IsNaN(currentLeft) || double.IsNaN(currentTop))
        {
            animated = false;
            currentLeft = target.X;
            currentTop = target.Y;
        }

        if (animated && _positionAnimationActive &&
            _positionAnimationTarget is { } pendingTarget && IsClose(pendingTarget, target))
            return;

        if (!animated && IsClose(new Point(currentLeft, currentTop), target))
            return;

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        _positionAnimationActive = false;
        _positionAnimationTarget = null;
        // 以当前视觉位置作为动画基准，避免窗口先瞬移到目标位置再开始动画。
        // Keep the current visual position as the animation base to avoid a one-frame jump.
        Left = currentLeft;
        Top = currentTop;

        if (!animated)
        {
            Left = target.X;
            Top = target.Y;
            return;
        }

        _positionAnimationTarget = target;
        _positionAnimationActive = true;
        var easing = new CubicEase { EasingMode = _isExpanded ? EasingMode.EaseOut : EasingMode.EaseInOut };
        var leftAnimation = new DoubleAnimation
        {
            From = currentLeft,
            To = target.X,
            Duration = TimeSpan.FromMilliseconds(PositionAnimationDurationMs),
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var topAnimation = new DoubleAnimation
        {
            From = currentTop,
            To = target.Y,
            Duration = TimeSpan.FromMilliseconds(PositionAnimationDurationMs),
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        leftAnimation.Completed += (_, _) =>
        {
            if (_positionAnimationTarget is not { } completedTarget || !IsClose(completedTarget, target))
                return;

            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = target.X;
            Top = target.Y;
            _positionAnimationTarget = null;
            _positionAnimationActive = false;
        };
        BeginAnimation(LeftProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsPositionTarget(Point target)
    {
        if (_positionAnimationActive && _positionAnimationTarget is { } pendingTarget)
            return IsClose(pendingTarget, target);

        return !double.IsNaN(Left) && !double.IsNaN(Top) &&
               IsClose(new Point(Left, Top), target);
    }

    private static bool IsClose(Point first, Point second) =>
        Math.Abs(first.X - second.X) <= PositionToleranceDip &&
        Math.Abs(first.Y - second.Y) <= PositionToleranceDip;

    private void Canvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left ||
            e.OriginalSource is DependencyObject source &&
            (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null || IsMediaAction(source)))
        {
            return;
        }

        _isDragging = true;
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
            Collapse(animated: true);
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

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        BeginAnimation(TopProperty, null);
        BeginAnimation(LeftProperty, null);
        MediaControl.TogglePlayPauseRequested -= MediaControl_TogglePlayPauseRequested;
        MediaControl.SkipPreviousRequested -= MediaControl_SkipPreviousRequested;
        MediaControl.SkipNextRequested -= MediaControl_SkipNextRequested;
        MediaControl.ActivateSourceRequested -= MediaControl_ActivateSourceRequested;
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

    private void StopPositionAnimationAtCurrentPosition()
    {
        var currentLeft = Left;
        var currentTop = Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        _positionAnimationTarget = null;
        _positionAnimationActive = false;
        if (!double.IsNaN(currentLeft))
            Left = currentLeft;
        if (!double.IsNaN(currentTop))
            Top = currentTop;
    }

    private bool SaveDraggedPositionAndEdge()
    {
        var workArea = GetCurrentWorkArea();
        var clampedLeft = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        var clampedTop = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        SetPosition(new Point(clampedLeft, clampedTop), animated: false);

        SettingsManager.Current.DynamicIslandLeft = clampedLeft;
        SettingsManager.Current.DynamicIslandTop = clampedTop;
        var edge = FindDockedEdge(Left, Top, workArea);
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
        var workArea = GetCurrentWorkArea();
        var defaultLeft = (workArea.Left + workArea.Right - Width) / 2;
        var left = SettingsManager.Current.DynamicIslandLeft ?? defaultLeft;
        var top = SettingsManager.Current.DynamicIslandTop ?? workArea.Top;
        return new Point(
            Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height)));
    }

    private Point GetCollapsedPosition()
    {
        var workArea = GetCurrentWorkArea();
        var expanded = GetExpandedPosition();
        return SettingsManager.Current.DynamicIslandEdge switch
        {
            DynamicIslandEdge.Left => new Point(workArea.Left - Width + EdgeRevealDip, expanded.Y),
            DynamicIslandEdge.Right => new Point(workArea.Right - EdgeRevealDip, expanded.Y),
            DynamicIslandEdge.Bottom => new Point(expanded.X, workArea.Bottom - EdgeRevealDip),
            _ => new Point(expanded.X, workArea.Top - Height + EdgeRevealDip)
        };
    }

    private DynamicIslandEdge? FindDockedEdge(double left, double top, Rect workArea)
    {
        var distances = new (DynamicIslandEdge Edge, double Distance)[]
        {
            (DynamicIslandEdge.Top, Math.Abs(top - workArea.Top)),
            (DynamicIslandEdge.Right, Math.Abs(workArea.Right - (left + Width))),
            (DynamicIslandEdge.Bottom, Math.Abs(workArea.Bottom - (top + Height))),
            (DynamicIslandEdge.Left, Math.Abs(left - workArea.Left))
        };
        var nearest = distances.MinBy(item => item.Distance);
        return nearest.Distance <= EdgeDockThresholdDip ? nearest.Edge : null;
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

    private sealed class MainWindowDataContext
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindowDataContext(MainWindowViewModel viewModel) => ViewModel = viewModel;
    }
}
