using AFMediaBar.Classes.Models.Layout;

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

    private static IEnumerable<TaskbarPrimaryRange> MergeRanges(IEnumerable<TaskbarPrimaryRange> ranges)
    {
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
