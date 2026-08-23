namespace AFMediaBar.Services;

/// <summary>
/// 统一鼠标滚轮增量的步进换算和循环索引计算，供多个紧凑控件复用。
/// Normalizes mouse-wheel deltas and circular indexes for reuse by compact controls.
/// </summary>
public static class WheelInput
{
    private const int DeltaPerStep = 120;

    public static int GetStepCount(int delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        return Math.Max(
            1,
            (Math.Abs(delta) + DeltaPerStep - 1) / DeltaPerStep);
    }

    public static int MoveCircular(int currentIndex, int stepCount, int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemCount, 1);
        var nextIndex = (currentIndex + stepCount) % itemCount;
        return nextIndex < 0 ? nextIndex + itemCount : nextIndex;
    }
}
