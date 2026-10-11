// 计算同一安全区内的位置与宽度恢复；宿主持有计时器和实际绘制帧。
using AFMediaBar.Classes.Services.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>短时三次缓出过渡，重定向从当前画面开始，目标不变时不重新计时。</summary>
public sealed class TaskbarReturnTransition
{
    private double _fromPosition;
    private double _fromWidth;
    private double _started;
    private double _duration;
    public bool IsActive { get; private set; }
    public double TargetPosition { get; private set; }
    public double TargetWidth { get; private set; }

    public void Retarget(double position, double width, double targetPosition, double targetWidth, double seconds, double duration)
    {
        if (IsActive && TargetPosition == targetPosition && TargetWidth == targetWidth) return;
        _fromPosition = position;
        _fromWidth = width;
        TargetPosition = targetPosition;
        TargetWidth = targetWidth;
        _started = seconds;
        _duration = duration;
        IsActive = duration > 0 && (Math.Abs(position - targetPosition) > 1 || Math.Abs(width - targetWidth) > 1);
    }

    public (double Position, double Width) Advance(double seconds)
    {
        if (!IsActive) return (TargetPosition, TargetWidth);
        var elapsed = Math.Max(0, seconds - _started) * 1000;
        var position = MediaBarSizeAnimationCalculator.Advance(_fromPosition, TargetPosition, 0, elapsed, _duration * 1000);
        var width = MediaBarSizeAnimationCalculator.Advance(_fromWidth, TargetWidth, 0, elapsed, _duration * 1000);
        IsActive = !position.IsCompleted;
        return (position.Value, width.Value);
    }

    public void Stop() => IsActive = false;
}
