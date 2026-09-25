using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 灵动岛窗口的尺寸与位置动效协调部分。
/// Animation coordination for dynamic-island size and position transitions.
/// </summary>
public partial class DynamicIslandWindow
{
    private const double PositionToleranceDip = 0.5;
    private Point? _positionAnimationTarget;
    private bool _positionAnimationActive;
    private double _sizeAnimationStart;
    private double _sizeAnimationTarget;
    private double _sizeAnimationProgress;
    private double _sizeAnchorLeft;
    private double _sizeAnchorTop;
    private double _sizeAnchorRight;
    private double _sizeAnchorCenter;
    private double _sizeAnchorBottom;
    private double _sizeAnchorCenterY;
    private MediaBarSizeRequest? _pendingSizeRequest;

    private void ApplyDesiredSizeRequest(MediaBarSizeRequest request, LayoutOrientation orientation)
    {
        if (_isClosing || _isDragging)
            return;

        var target = request.PrimaryLength;
        var motion = MotionPolicy.ResolveCurrent();
        var maximum = GetAvailablePrimaryLengthDip(orientation);
        if (maximum > 0)
            target = Math.Min(target, maximum);

        var current = orientation == LayoutOrientation.Horizontal ? Width : Height;
        if (double.IsNaN(current) || current <= 0)
            current = MediaControl.CurrentLayout is { } layout
                ? orientation == LayoutOrientation.Horizontal ? layout.Canvas.Width : layout.Canvas.Height
                : target;

        CaptureSizeAnchors(orientation, current);
        // 歌词换行等离散内容切换必须立即落到目标长度：过渡动画期间新内容按旧长度渲染会被省略号截断。
        // Discrete content switches such as a lyric line change must land immediately: while the length animates the new content
        // renders at the previous length and gets clipped with an ellipsis.
        if (request.SkipTransition || !motion.UseContinuousMotion || Math.Abs(target - current) < LayoutSizeCalculator.MinimumChangeDip)
        {
            ApplyAnimatedSize(target, orientation);
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
            return;
        }

        _sizeAnimationStart = current;
        _sizeAnimationTarget = target;
        _sizeAnimationProgress = 0;
        _sizeAnimationTimer.Start();
    }

    private void CaptureSizeAnchors(LayoutOrientation orientation, double currentPrimary)
    {
        var left = double.IsNaN(Left) ? GetExpandedPosition().X : Left;
        var top = double.IsNaN(Top) ? GetExpandedPosition().Y : Top;
        var width = double.IsNaN(Width) || Width <= 0
            ? MediaControl.CurrentLayout?.Canvas.Width ?? (orientation == LayoutOrientation.Horizontal ? currentPrimary : 1)
            : Width;
        var height = double.IsNaN(Height) || Height <= 0
            ? MediaControl.CurrentLayout?.Canvas.Height ?? (orientation == LayoutOrientation.Vertical ? currentPrimary : 1)
            : Height;

        _sizeAnchorLeft = left;
        _sizeAnchorTop = top;
        _sizeAnchorRight = left + width;
        _sizeAnchorCenter = left + width / 2;
        _sizeAnchorBottom = top + height;
        _sizeAnchorCenterY = top + height / 2;
    }

    private double GetAvailablePrimaryLengthDip(LayoutOrientation orientation)
    {
        var area = GetCurrentWorkArea();
        return orientation == LayoutOrientation.Horizontal
            ? Math.Max(1, area.Width - 40)
            : Math.Max(1, area.Height - 40);
    }

    private void AdvanceSizeAnimation()
    {
        if (_isClosing || _isDragging)
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
        ApplyAnimatedSize(frame.Value, _appliedOrientation ?? LayoutOrientation.Horizontal);
        if (frame.IsCompleted)
        {
            ApplyAnimatedSize(_sizeAnimationTarget, _appliedOrientation ?? LayoutOrientation.Horizontal);
            _sizeAnimationTimer.Stop();

            // 展开状态下将尺寸动画后的最终位置作为新的持久化锚点，避免旧左上角覆盖中心锚点结果。
            // While expanded, persist the post-resize position so a stale top-left does not overwrite the center anchor.
            if (_isExpanded)
                SaveCurrentExpandedPosition();

            if (!_isDragging && !_positionAnimationActive)
                SetPosition(_isExpanded ? GetExpandedPosition() : GetCollapsedPosition(), animated: false);
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
        }
    }

    private void ApplyAnimatedSize(double primaryLength, LayoutOrientation orientation)
    {
        MediaControl.ApplyPrimaryLength(primaryLength);
        var layout = MediaControl.CurrentLayout;
        if (layout is null)
            return;

        if (orientation == LayoutOrientation.Horizontal)
        {
            Width = layout.Canvas.Width;
            Height = layout.Canvas.Height;
            var edge = SettingsManager.Current.DynamicIslandEdge;
            Left = SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Right
                ? _sizeAnchorRight - Width
                : SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Left
                    ? _sizeAnchorLeft
                    : _sizeAnchorCenter - Width / 2;
            Top = _sizeAnchorTop;
        }
        else
        {
            Width = layout.Canvas.Width;
            Height = layout.Canvas.Height;
            var edge = SettingsManager.Current.DynamicIslandEdge;
            Top = SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Bottom
                ? _sizeAnchorBottom - Height
                : SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Top
                    ? _sizeAnchorTop
                    : _sizeAnchorCenterY - Height / 2;

            if (SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Right)
                Left = _sizeAnchorRight - Width;
            else if (SettingsManager.Current.DynamicIslandEdgeDocked && edge == DynamicIslandEdge.Left)
                Left = _sizeAnchorLeft;
            else
                Left = _sizeAnchorCenter - Width / 2;
        }
    }

    private void SetPosition(Point target, bool animated)
    {
        if (!MotionPolicy.ResolveCurrent().UseTransitions)
            animated = false;

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

        _foregroundSamplingSession.Invalidate(clearDecision: false);
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
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
            return;
        }

        _positionAnimationTarget = target;
        _positionAnimationActive = true;
        var motion = MotionPolicy.ResolveCurrent();
        var easing = _isExpanded ? new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut }
            : new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
        var leftAnimation = new DoubleAnimation
        {
            From = currentLeft,
            To = target.X,
            Duration = motion.PositionDuration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        var topAnimation = new DoubleAnimation
        {
            From = currentTop,
            To = target.Y,
            Duration = motion.PositionDuration,
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
            ApplyPendingSizeRequest();
            Dispatcher.BeginInvoke(_foregroundSamplingSession.RequestRefresh, DispatcherPriority.ContextIdle);
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

    private void ApplyPendingSizeRequest()
    {
        if (_pendingSizeRequest is not { } request || _appliedOrientation is not { } orientation)
            return;

        _pendingSizeRequest = null;
        ApplyDesiredSizeRequest(request, orientation);
    }
}
