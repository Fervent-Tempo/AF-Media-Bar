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
            _ => rangeStart + (rangeEnd - rangeStart - primarySize) / 2
        } + manualPadding;
        var crossPosition = (crossLength - crossSize) / 2 +
                            (int)Math.Round(crossAxisOffsetDip * Math.Max(0, dpiScale));

        primaryPosition = Math.Clamp(primaryPosition, rangeStart, Math.Max(rangeStart, rangeEnd - primarySize));
        crossPosition = Math.Clamp(crossPosition, 0, Math.Max(0, crossLength - crossSize));
        return new TaskbarBarPlacement(primaryPosition, crossPosition);
    }
}

/// <summary>
/// 任务栏媒体栏的主轴和横轴物理像素位置。
/// Physical primary- and cross-axis position of the taskbar media bar.
/// </summary>
public readonly record struct TaskbarBarPlacement(int Primary, int Cross);
