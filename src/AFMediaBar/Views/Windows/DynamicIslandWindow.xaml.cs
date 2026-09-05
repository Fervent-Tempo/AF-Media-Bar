using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Windows;

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
    private readonly MainWindowViewModel _viewModel;
    private bool _isExpanded;
    private bool _isClosing;
    private bool _isDragging;
    private LayoutOrientation? _appliedOrientation;

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
                if (SettingsManager.Current.DynamicIslandEdgeDocked)
                    Collapse(animated: true);
                else
                    Expand(animated: true);
                return;
            }

            ApplyLayoutSettings(SettingsManager.Current.LayoutOrientationMode);
            MediaControl.UpdateSongInfo(snapshot);
            MediaControl.ApplyWindowsTheme();
            Visibility = Visibility.Visible;

            if (snapshot.IsPlaying)
            {
                Expand(animated: true);
            }
            else
            {
                if (SettingsManager.Current.DynamicIslandEdgeDocked)
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

        if (_appliedOrientation == orientation)
            return;

        MediaControl.ApplyLayout(WindowMode.DynamicIsland, orientation);
        var canvas = MediaControl.CurrentLayout?.Canvas;
        if (canvas is null)
            return;

        _appliedOrientation = orientation;
        Width = canvas.Width;
        Height = canvas.Height;

        if (!IsLoaded)
        {
            RestoreSavedPosition();
            return;
        }

        SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
    }

    public void ApplyAppearanceSettings() => MediaControl.ApplyWindowsTheme();

    private void Expand(bool animated)
    {
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
        var target = GetCollapsedPosition();
        if (!_isExpanded && IsPositionTarget(target))
            return;

        _isExpanded = false;
        SetPosition(target, animated);
    }

    private void SetPosition(Point target, bool animated)
    {
        if (IsPositionTarget(target))
            return;

        var startLeft = double.IsNaN(Left) ? target.X : Left;
        var startTop = double.IsNaN(Top) ? target.Y : Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = target.X;
        Top = target.Y;

        if (!animated)
            return;

        var easing = new CubicEase { EasingMode = _isExpanded ? EasingMode.EaseOut : EasingMode.EaseInOut };
        var leftAnimation = new DoubleAnimation
        {
            From = startLeft,
            To = target.X,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var topAnimation = new DoubleAnimation
        {
            From = startTop,
            To = target.Y,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        BeginAnimation(LeftProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsPositionTarget(Point target)
    {
        var baseLeft = (double)GetAnimationBaseValue(LeftProperty);
        var baseTop = (double)GetAnimationBaseValue(TopProperty);
        return !double.IsNaN(baseLeft) && !double.IsNaN(baseTop) &&
               Math.Abs(baseLeft - target.X) <= PositionToleranceDip &&
               Math.Abs(baseTop - target.Y) <= PositionToleranceDip;
    }

    private void Canvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left ||
            e.OriginalSource is DependencyObject source &&
            (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null || IsMediaAction(source)))
        {
            return;
        }

        StopPositionAnimationAtCurrentPosition();
        _isDragging = true;
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
        if (!MediaControl.IsPlaying && !_isDragging && SettingsManager.Current.DynamicIslandEdgeDocked)
            Collapse(animated: true);
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
        Left = currentLeft;
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
