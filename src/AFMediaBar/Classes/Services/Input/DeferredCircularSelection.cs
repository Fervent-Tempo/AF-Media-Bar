namespace AFMediaBar.Classes.Services;

/// <summary>滚轮驱动的纯循环选择策略。 / Pure circular-selection policy for wheel-driven pickers.</summary>
public static class DeferredCircularSelection
{
    public static int Move(int currentIndex, int delta, int count)
    {
        if (count <= 0 || delta == 0)
            return -1;
        var steps = Math.Max(1, Math.Abs(delta) / 120);
        var direction = delta > 0 ? -steps : steps;
        var start = Math.Clamp(currentIndex, 0, count - 1);
        return ((start + direction) % count + count) % count;
    }
}
