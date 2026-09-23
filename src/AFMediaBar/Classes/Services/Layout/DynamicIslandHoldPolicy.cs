namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 长按过程的渐进展开、提交门槛及短按判定；使用单调时钟提供的实际时长。
/// Progressive hold expansion, commit threshold, and tap classification driven by monotonic elapsed time.
/// </summary>
public static class DynamicIslandHoldPolicy
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MaximumTapDuration = TimeSpan.FromMilliseconds(180);
    private const double PreviewExpansion = 0.85;

    /// <summary>长按的归一化进度。/ Normalized progress toward the hold threshold.</summary>
    public static double GetProgress(double elapsedSeconds) =>
        double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds / HoldDuration.TotalSeconds, 0, 1) : 0;

    /// <summary>
    /// 从按下时的现有形态连续向展开态靠近，完整提交前保留少量弹性收敛空间。
    /// Progresses from the existing shape, leaving a small amount of spring travel for the committed expansion.
    /// </summary>
    public static double GetPreviewExpansion(double elapsedSeconds, double initialExpansion)
    {
        var start = double.IsFinite(initialExpansion) ? Math.Clamp(initialExpansion, 0, 1) : 0;
        var progress = GetProgress(elapsedSeconds);
        var eased = progress * progress * (3 - 2 * progress);
        return start + (Math.Max(start, PreviewExpansion) - start) * eased;
    }

    /// <summary>只有实际达到长按门槛才提交。/ Commits only once the actual hold threshold has elapsed.</summary>
    public static bool ShouldCommit(double elapsedSeconds) =>
        double.IsFinite(elapsedSeconds) && elapsedSeconds >= HoldDuration.TotalSeconds;

    /// <summary>中途放弃的长按不应被当作打开来源应用的短按。/ An abandoned hold is not a tap that opens the source app.</summary>
    public static bool IsTap(double elapsedSeconds) =>
        double.IsFinite(elapsedSeconds) && elapsedSeconds >= 0 && elapsedSeconds <= MaximumTapDuration.TotalSeconds;
}
