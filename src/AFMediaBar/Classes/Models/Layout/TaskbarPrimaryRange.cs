namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 表示任务栏主轴上的物理像素区间。
/// Represents a physical-pixel interval along the taskbar primary axis.
/// </summary>
/// <param name="Start">区间起点（含）/ Inclusive interval start.</param>
/// <param name="End">区间终点（不含）/ Exclusive interval end.</param>
public readonly record struct TaskbarPrimaryRange(int Start, int End)
{
    /// <summary>获取非负区间长度。/ Gets the non-negative interval length.</summary>
    public int Length => Math.Max(0, End - Start);
}
