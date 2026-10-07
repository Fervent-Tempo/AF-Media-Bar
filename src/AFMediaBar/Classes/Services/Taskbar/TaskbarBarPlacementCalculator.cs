using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算任务栏媒体栏在安全区间内的物理像素位置。
/// Calculates the media bar's physical pixel position within a safe taskbar range.
/// </summary>
public static class TaskbarBarPlacementCalculator
{
    /// <summary>
    /// 根据主轴/横轴尺寸、位置偏好和偏移量计算放置结果。
    /// Calculates placement from primary/cross-axis sizes, position preference, and offsets.
    /// </summary>
    public static TaskbarBarPlacement Calculate(
        int primaryLength,
        int primarySize,
        int crossLength,
        int crossSize,
        TaskbarPrimaryRange preferredRange,
        TaskbarBarPosition position,
        int manualPadding,
        double crossAxisOffsetDip,
        double dpiScale,
        int edgePadding)
    {
        var rangeStart = preferredRange.Length > 0 ? preferredRange.Start : Math.Min(edgePadding, primaryLength);
        var rangeEnd = preferredRange.Length > 0
            ? preferredRange.End
            : Math.Max(Math.Min(edgePadding, primaryLength), primaryLength - edgePadding);
        var primaryPosition = position switch
        {
            TaskbarBarPosition.Start => rangeStart,
            TaskbarBarPosition.End => rangeEnd - primarySize,
            _ => (primaryLength - primarySize) / 2
        } + manualPadding;
        var crossPosition = (crossLength - crossSize) / 2 +
                            (int)Math.Round(crossAxisOffsetDip * Math.Max(0, dpiScale));

        primaryPosition = Math.Clamp(primaryPosition, rangeStart, Math.Max(rangeStart, rangeEnd - primarySize));
        crossPosition = Math.Clamp(crossPosition, 0, Math.Max(0, crossLength - crossSize));
        return new TaskbarBarPlacement(primaryPosition, crossPosition);
    }

    /// <summary>
    /// 把"鼠标想把媒体栏放到哪里"换算成手动偏移（相对**空闲区间起点**，与 <see cref="Calculate"/> 的加法基准一致）。
    ///
    /// 这一步 MUST 用同一个基准：`Calculate` 算的是 `rangeStart + manualPadding`，若拖动时按"任务栏边缘留白"为基准写偏移，
    /// 两者相差 `rangeStart - edgePadding`——媒体栏会整体偏离鼠标（自动避让打开、区间起点不在最左边时，实测偏出整整一个
    /// 区间起点的距离，表现为"拖不动/位置和鼠标不对应"）。这里同时把结果夹进区间，因此写回去的位置与拖到的位置完全一致。
    /// Converts "where the mouse wants the bar" into the manual offset, measured from the **free range's start**, which is the same base
    /// <see cref="Calculate"/> adds it to.
    ///
    /// Sharing the base matters: `Calculate` computes `rangeStart + manualPadding`, so an offset written against the taskbar's edge
    /// padding differs from it by `rangeStart - edgePadding` — the bar then misses the mouse by exactly that distance (with "avoid
    /// icons" on and a range that does not start at the left edge, that is a whole range offset, which feels like "it will not drag
    /// where I point"). Clamping into the range here keeps the stored offset and the resulting position identical.
    /// </summary>
    /// <param name="desiredPrimary">鼠标希望媒体栏所在的物理主轴位置。/ Physical primary-axis position the mouse asks for.</param>
    /// <param name="rangeStart">当前空闲区间的起点。/ Start of the current free range.</param>
    /// <param name="rangeEnd">当前空闲区间的终点。/ End of the current free range.</param>
    /// <param name="primarySize">媒体栏的主轴长度（物理像素）。/ Bar length along the primary axis, in physical pixels.</param>
    public static int ResolveManualPadding(int desiredPrimary, int rangeStart, int rangeEnd, int primarySize)
    {
        var maximum = Math.Max(rangeStart, rangeEnd - primarySize);
        return Math.Clamp(desiredPrimary, rangeStart, maximum) - rangeStart;
    }
}

/// <summary>
/// 任务栏媒体栏的主轴和横轴物理像素位置。
/// Physical primary- and cross-axis position of the taskbar media bar.
/// </summary>
public readonly record struct TaskbarBarPlacement(int Primary, int Cross);
