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
        var primaryPosition = ResolveAnchor(primaryLength, primarySize, rangeStart, rangeEnd, position) + manualPadding;
        var crossPosition = (crossLength - crossSize) / 2 +
                            (int)Math.Round(crossAxisOffsetDip * Math.Max(0, dpiScale));

        primaryPosition = Math.Clamp(primaryPosition, rangeStart, Math.Max(rangeStart, rangeEnd - primarySize));
        crossPosition = Math.Clamp(crossPosition, 0, Math.Max(0, crossLength - crossSize));
        return new TaskbarBarPlacement(primaryPosition, crossPosition);
    }

    /// <summary>把拖动位置换算为相对所选锚点的物理像素偏移，与放置使用同一基准。</summary>
    /// <param name="desiredPrimary">鼠标要求的主轴位置。</param>
    /// <param name="rangeStart">当前安全区间起点。</param>
    /// <param name="rangeEnd">当前安全区间终点。</param>
    /// <param name="primarySize">媒体栏的主轴长度。</param>
    /// <param name="primaryLength">任务栏主轴长度，居中锚点以它为基准。</param>
    /// <param name="position">所选对齐方式，默认保留原有起点偏移语义。</param>
    public static int ResolveManualPadding(int desiredPrimary, int rangeStart, int rangeEnd, int primarySize,
        int primaryLength = 0, TaskbarBarPosition position = TaskbarBarPosition.Start)
    {
        var maximum = Math.Max(rangeStart, rangeEnd - primarySize);
        return Math.Clamp(desiredPrimary, rangeStart, maximum) -
            ResolveAnchor(primaryLength, primarySize, rangeStart, rangeEnd, position);
    }

    private static int ResolveAnchor(int primaryLength, int primarySize, int rangeStart, int rangeEnd,
        TaskbarBarPosition position) => position switch
        {
            TaskbarBarPosition.Start => rangeStart,
            TaskbarBarPosition.End => rangeEnd - primarySize,
            _ => (primaryLength - primarySize) / 2
        };
}

/// <summary>任务栏媒体栏的主轴和横轴物理像素位置。</summary>
public readonly record struct TaskbarBarPlacement(int Primary, int Cross);
