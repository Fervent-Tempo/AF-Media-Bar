using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 从任务栏占用区间计算带边距的安全空闲区间。
/// Calculates safe free taskbar ranges from occupied intervals and edge padding.
/// </summary>
public static class TaskbarFreeRangeCalculator
{
    /// <summary>
    /// 合并重叠占用区间并返回按主轴排序的空闲区间。
    /// Merges overlapping occupied intervals and returns ordered free primary-axis ranges.
    /// </summary>
    public static IReadOnlyList<TaskbarPrimaryRange> Calculate(
        int primaryLength,
        IReadOnlyList<TaskbarPrimaryRange> occupied,
        int edgePaddingPixels,
        int gapPixels)
    {
        var start = Math.Clamp(edgePaddingPixels, 0, Math.Max(0, primaryLength));
        var end = Math.Clamp(primaryLength - edgePaddingPixels, start, primaryLength);
        if (end <= start)
            return [];

        var merged = occupied
            .Select(range => new TaskbarPrimaryRange(
                Math.Clamp(range.Start - gapPixels, start, end),
                Math.Clamp(range.End + gapPixels, start, end)))
            .Where(range => range.End > range.Start)
            .OrderBy(range => range.Start)
            .ToList();

        var result = new List<TaskbarPrimaryRange>();
        var cursor = start;
        foreach (var range in MergeRanges(merged))
        {
            if (range.Start > cursor)
                result.Add(new TaskbarPrimaryRange(cursor, range.Start));
            cursor = Math.Max(cursor, range.End);
        }

        if (cursor < end)
            result.Add(new TaskbarPrimaryRange(cursor, end));

        return result.Where(range => range.Length > 0).ToArray();
    }

    /// <summary>
    /// 按位置偏好在空闲区间里挑一个：Start 取第一个**放得下**的区间，End 取最后一个放得下的，Center 取最长的。
    ///
    /// "放得下"是必须的：空闲区间里可能有比媒体栏最小长度还窄的缝隙（例如图标之间被挤出来的那一段），取到它会把媒体栏
    /// 压成一条细条并钉在缝隙里。所有区间都放不下时退回最长的那个，此时长度由调用方夹到区间宽度——至少是"尽可能大的位置"，
    /// 而不是"最左边那条缝"。
    /// Picks a free range by position preference: Start takes the first one that **fits**, End the last that fits, and Center the
    /// longest.
    ///
    /// Fitting matters: the free ranges can include a gap narrower than the bar's minimum length (the sliver squeezed between two
    /// icons, for instance), and picking it would squash the bar into a strip pinned inside that gap. When nothing fits, the longest
    /// range is returned instead, so the caller clamps the length to a range's width — "as much room as possible" rather than "the
    /// leftmost sliver".
    /// </summary>
    /// <param name="ranges">按主轴排序的空闲区间。/ Free ranges ordered along the primary axis.</param>
    /// <param name="position">位置偏好。/ Position preference.</param>
    /// <param name="requiredPrimaryPixels">媒体栏当前占用的主轴长度（物理像素），0 表示不做放得下判断。/ The bar's current primary-axis length in physical pixels; 0 skips the fitting test.</param>
    public static TaskbarPrimaryRange Select(
        IReadOnlyList<TaskbarPrimaryRange> ranges,
        TaskbarBarPosition position,
        int requiredPrimaryPixels)
    {
        if (ranges.Count == 0)
            return default;

        var longest = ranges.OrderByDescending(range => range.Length).First();
        if (requiredPrimaryPixels <= 0)
        {
            return position switch
            {
                TaskbarBarPosition.End => ranges[^1],
                TaskbarBarPosition.Center => longest,
                _ => ranges[0]
            };
        }

        var fitting = ranges.Where(range => range.Length >= requiredPrimaryPixels).ToArray();
        if (fitting.Length == 0)
            return longest;

        return position switch
        {
            TaskbarBarPosition.End => fitting[^1],
            TaskbarBarPosition.Center => fitting.OrderByDescending(range => range.Length).First(),
            _ => fitting[0]
        };
    }

    /// <summary>
    /// 当前探测结果仍完整包含上一次选区时保留原选区，避免 UI Automation 的短暂缺项把媒体栏推入图标区。
    /// Keeps the previous selection while the current probe still fully contains it, preventing a transiently incomplete UI Automation result
    /// from moving the media bar into the icon band.
    /// </summary>
    public static bool TryKeepSelection(
        IReadOnlyList<TaskbarPrimaryRange> ranges,
        TaskbarPrimaryRange previous,
        int requiredPrimaryPixels,
        out TaskbarPrimaryRange selection)
    {
        selection = default;
        if (previous.Length <= 0 || requiredPrimaryPixels > 0 && previous.Length < requiredPrimaryPixels)
            return false;

        if (!ranges.Any(range => range.Start <= previous.Start && range.End >= previous.End))
            return false;

        selection = previous;
        return true;
    }

    private static IEnumerable<TaskbarPrimaryRange> MergeRanges(IEnumerable<TaskbarPrimaryRange> ranges)    {
        TaskbarPrimaryRange? current = null;
        foreach (var range in ranges)
        {
            if (current is not { } active)
            {
                current = range;
                continue;
            }

            if (range.Start <= active.End)
            {
                current = new TaskbarPrimaryRange(active.Start, Math.Max(active.End, range.End));
                continue;
            }

            yield return active;
            current = range;
        }

        if (current is { } last)
            yield return last;
    }
}
