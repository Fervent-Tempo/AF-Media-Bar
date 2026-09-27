using System.Windows;
using System.Windows.Input;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏悬停层进度条的拖动跳转：按住任意位置即预览该时间、拖动调整、松开跳转。
///
/// 手势归本控件所有（进度条面的 Tag="MediaAction" 让宿主的整条媒体栏拖动放过按下事件）；
/// 拖动期间由 <see cref="_isSeekingHoverSeek"/> 挡住 250ms 的位置回写与悬停层收起，松手时只下发一次跳转。
/// Drag-to-seek on the taskbar hover-layer progress bar: pressing previews that time, dragging adjusts it, releasing seeks.
///
/// The gesture belongs to this control (the seek surface's Tag="MediaAction" keeps the host's whole-bar drag out of the
/// press); while dragging, <see cref="_isSeekingHoverSeek"/> blocks the 250 ms position write-back and the hover-layer
/// collapse, and exactly one seek is issued on release.
/// </summary>
public partial class TaskBarMediaControl
{
    private bool _isSeekingHoverSeek;
    private bool _isHoverSeekSurfaceHovered;

    private void TaskbarHoverSeekSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHoverSeekSurfaceHovered = true;
        UpdateHoverSeekAffordances();
    }

    private void TaskbarHoverSeekSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHoverSeekSurfaceHovered = false;
        UpdateHoverSeekAffordances();
    }

    private void TaskbarHoverSeekSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ResolveHoverSeekTarget(e.GetPosition(TaskbarHoverSeekSurface).X) is not { } target)
        {
            return;
        }

        _isSeekingHoverSeek = true;
        TaskbarHoverSeekSurface.CaptureMouse();
        TaskbarHoverSeekBubble.IsOpen = true;
        UpdateHoverSeekPreview(target);
        e.Handled = true;
    }

    private void TaskbarHoverSeekSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSeekingHoverSeek || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (ResolveHoverSeekTarget(e.GetPosition(TaskbarHoverSeekSurface).X) is { } target)
        {
            UpdateHoverSeekPreview(target);
        }
    }

    private void TaskbarHoverSeekSurface_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSeekingHoverSeek)
        {
            return;
        }

        var target = ResolveHoverSeekTarget(e.GetPosition(TaskbarHoverSeekSurface).X);
        EndHoverSeek();
        if (target is { } seconds)
        {
            SeekRequested?.Invoke(this, seconds);
        }

        e.Handled = true;
    }

    private void TaskbarHoverSeekSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isSeekingHoverSeek)
        {
            EndHoverSeek();
        }
    }

    /// <summary>按指针在条内的位置算出目标秒数；不可跳转或输入无效时为空（手势直接不开始）。
    /// Resolves the target seconds from the pointer position; null while seeking is unavailable or the input is invalid
    /// (the gesture then never starts).</summary>
    private double? ResolveHoverSeekTarget(double pointerX) =>
        TaskbarHoverSeekPolicy.ResolveTargetSeconds(
            _snapshot.CanSeek && !_isVertical,
            _snapshot.Duration,
            pointerX,
            TaskbarHoverSeekSurface.ActualWidth);

    private void UpdateHoverSeekPreview(double seconds)
    {
        TaskbarHoverProgress.Value = seconds;
        UpdateHoverSeekThumb(seconds);
        TaskbarHoverSeekBubbleText.Text = PlaybackTimeText.Format(seconds);
    }

    /// <summary>把圆点移到该时间的比例位置（两端夹取，避免越出条面）。
    /// Moves the dot to the ratio position of the given time (clamped so it never leaves the bar).</summary>
    private void UpdateHoverSeekThumb(double seconds)
    {
        var duration = Math.Max(1, _snapshot.Duration);
        var ratio = Math.Clamp(seconds / duration, 0, 1);
        var width = TaskbarHoverSeekSurface.ActualWidth;
        var thumbWidth = TaskbarHoverSeekThumb.Width;
        var left = width <= thumbWidth ? 0 : ratio * (width - thumbWidth);
        TaskbarHoverSeekThumb.Margin = new Thickness(left, 0, 0, 0);
    }

    /// <summary>刷新可拖拽的视觉提示：可跳转时光标为手型、悬停或拖动时显示圆点。
    /// Refreshes the drag affordances: a hand cursor while seeking is available and a dot shown on hover or while dragging.</summary>
    private void UpdateHoverSeekAffordances()
    {
        var seekable = _snapshot.CanSeek && _snapshot.Duration > 0;
        TaskbarHoverSeekSurface.Cursor = seekable ? Cursors.Hand : Cursors.Arrow;
        TaskbarHoverSeekThumb.Visibility = seekable && (_isSeekingHoverSeek || _isHoverSeekSurfaceHovered)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void EndHoverSeek()
    {
        _isSeekingHoverSeek = false;
        if (TaskbarHoverSeekSurface.IsMouseCaptured)
        {
            TaskbarHoverSeekSurface.ReleaseMouseCapture();
        }

        TaskbarHoverSeekBubble.IsOpen = false;
        UpdateHoverSeekAffordances();
        UpdateTaskbarProgress();

        // 拖动期间悬停层不收起；松手时指针可能已经停在卡片外，这里补一次判定让卡片按常规则收起。
        // The hover layer stays open during the drag; the pointer may be outside the card on release, so the usual collapse is
        // re-evaluated once here.
        if (!HoverRevealHost.IsMouseOver && !SongInfoStackPanel.IsMouseOver)
        {
            _hoverCloseTimer.Start();
        }
    }
}
