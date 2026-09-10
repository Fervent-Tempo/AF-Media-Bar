namespace AFMediaBar.Classes.Services;

/// <summary>统一滚轮步进和循环索引。 / Normalizes wheel steps and circular indexes.</summary>
public static class WheelInput
{
    private const int DeltaPerStep = 120;

    /// <summary>把原始滚轮增量转换为步数。/ Converts a raw wheel delta into steps.</summary>
    public static int GetStepCount(int delta) => delta == 0
        ? 0
        : Math.Max(1, (Math.Abs(delta) + DeltaPerStep - 1) / DeltaPerStep);

    /// <summary>按循环列表规则移动索引。/ Moves an index using circular-list semantics.</summary>
    public static int MoveCircular(int currentIndex, int stepCount, int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemCount, 1);
        var nextIndex = (currentIndex + stepCount) % itemCount;
        return nextIndex < 0 ? nextIndex + itemCount : nextIndex;
    }
}
