namespace AFMediaBar.Classes.Services;

/// <summary>
/// 悬停层进度条的拖动跳转换算：把"指针在条内的位置"换算成要跳转的秒数。
///
/// 纯函数，便于单测覆盖两端夹取与非法输入；控件侧只负责把指针坐标与条宽传进来。
/// Drag-to-seek math for the hover-layer progress bar: converts a pointer position on the bar into the seconds to seek to.
///
/// Pure functions so tests can cover both clamps and the invalid inputs; the control only supplies the pointer coordinate
/// and the bar width.
/// </summary>
public static class TaskbarHoverSeekPolicy
{
    /// <summary>指针位置在条内的比例（0–1）；宽度或坐标非法时返回 null。
    /// Pointer ratio within the bar (0–1); null when the width or the coordinate is invalid.</summary>
    /// <param name="pointerX">指针相对进度条的横坐标（可越界，会被夹取）。/ Pointer X relative to the bar (clamped).</param>
    /// <param name="trackWidth">进度条宽度 / Bar width.</param>
    public static double? ResolveRatio(double pointerX, double trackWidth)
    {
        if (!double.IsFinite(pointerX) || !double.IsFinite(trackWidth) || trackWidth <= 0)
        {
            return null;
        }

        return Math.Clamp(pointerX / trackWidth, 0, 1);
    }

    /// <summary>
    /// 要跳转到的秒数；来源不支持跳转、时长无效或条宽无效时返回 null（进度条保持只读显示）。
    /// Seconds to seek to; null while the source cannot seek, the duration is invalid, or the bar has no width (the bar
    /// then stays display-only).
    /// </summary>
    /// <param name="canSeek">来源是否支持跳转 / Whether the source can seek.</param>
    /// <param name="durationSeconds">当前曲目时长（秒）/ Current track duration in seconds.</param>
    /// <param name="pointerX">指针相对进度条的横坐标 / Pointer X relative to the bar.</param>
    /// <param name="trackWidth">进度条宽度 / Bar width.</param>
    public static double? ResolveTargetSeconds(
        bool canSeek,
        double durationSeconds,
        double pointerX,
        double trackWidth)
    {
        if (!canSeek || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return null;
        }

        return ResolveRatio(pointerX, trackWidth) is { } ratio
            ? ratio * durationSeconds
            : null;
    }
}
