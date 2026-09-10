namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 计算媒体栏尺寸动画的三次缓出进度和当前长度，不依赖 WPF 计时器或控件。
/// Calculates cubic-eased media-bar size animation progress and current length without WPF timers or controls.
/// </summary>
public static class MediaBarSizeAnimationCalculator
{
    /// <summary>
    /// 单帧尺寸动画结果。
    /// Result of one size-animation frame.
    /// </summary>
    public readonly record struct Frame(double Value, double Progress, bool IsCompleted);

    /// <summary>
    /// 推进一个尺寸动画帧。
    /// Advances one size-animation frame.
    /// </summary>
    /// <param name="start">起始尺寸。/ Starting size.</param>
    /// <param name="target">目标尺寸。/ Target size.</param>
    /// <param name="progress">上一帧进度，范围为 0 到 1。/ Previous progress in the 0-1 range.</param>
    /// <param name="elapsedMilliseconds">本帧经过的毫秒数。/ Elapsed milliseconds for this frame.</param>
    /// <param name="durationMilliseconds">完整动画时长，必须为正数。/ Full animation duration, which must be positive.</param>
    /// <returns>当前尺寸、规范化进度和完成状态。/ Current size, normalized progress, and completion state.</returns>
    public static Frame Advance(
        double start,
        double target,
        double progress,
        double elapsedMilliseconds,
        double durationMilliseconds = 220)
    {
        if (durationMilliseconds <= 0)
        {
            return new Frame(target, 1, true);
        }

        var normalizedProgress = Math.Clamp(progress, 0, 1);
        var elapsed = Math.Max(0, elapsedMilliseconds);
        var nextProgress = Math.Clamp(
            normalizedProgress + elapsed / durationMilliseconds,
            0,
            1);
        var eased = 1 - Math.Pow(1 - nextProgress, 3);
        var value = start + (target - start) * eased;
        return new Frame(value, nextProgress, nextProgress >= 1);
    }
}
