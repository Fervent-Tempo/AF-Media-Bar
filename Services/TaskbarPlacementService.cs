using System.Windows.Automation;
using TaskbarPlayer.Interop;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal sealed class TaskbarPlacementService
{
    private const int OccupiedPadding = 4;

    internal Task<int?> FindBestLeftAsync(
        nint taskbar,
        NativeMethods.Rect taskbarRect,
        int playerWidth,
        int margin)
    {
        return Task.Run(() => FindBestLeft(taskbar, taskbarRect, playerWidth, margin));
    }

    [System.Diagnostics.Conditional("DEBUG")]
    internal static void ValidateAlgorithm()
    {
        const int taskbarLeft = 0;
        const int taskbarRight = 1920;
        const int playerWidth = 437;
        const int margin = 10;

        var centered = FindBestLeft(
            taskbarLeft,
            taskbarRight,
            playerWidth,
            margin,
            [new OccupiedRange(642, 1193), new OccupiedRange(1265, 1920)]);
        if (centered != margin)
        {
            throw new InvalidOperationException("居中任务栏的左侧空白区计算失败。");
        }

        var leftAligned = FindBestLeft(
            taskbarLeft,
            taskbarRight,
            playerWidth,
            margin,
            [new OccupiedRange(0, 600), new OccupiedRange(1265, 1920)]);
        if (leftAligned < 600 || leftAligned + playerWidth > 1265)
        {
            throw new InvalidOperationException("靠左任务栏的中间空白区计算失败。");
        }

        var crowded = FindBestLeft(
            taskbarLeft,
            taskbarRight,
            playerWidth,
            margin,
            [new OccupiedRange(0, 1050), new OccupiedRange(1265, 1920)]);
        if (crowded < margin || crowded + playerWidth > taskbarRight - margin)
        {
            throw new InvalidOperationException("拥挤任务栏的最小重叠位置计算失败。");
        }
    }

    private static int? FindBestLeft(
        nint taskbar,
        NativeMethods.Rect taskbarRect,
        int playerWidth,
        int margin)
    {
        try
        {
            var taskbarElement = AutomationElement.FromHandle(taskbar);
            var buttonCondition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            var buttons = taskbarElement.FindAll(TreeScope.Descendants, buttonCondition);
            var occupied = new List<OccupiedRange>(buttons.Count);

            foreach (AutomationElement button in buttons)
            {
                var bounds = button.Current.BoundingRectangle;
                if (bounds.Width <= 1 || bounds.Width > 260 ||
                    bounds.Height <= 1 ||
                    bounds.Bottom <= taskbarRect.Top ||
                    bounds.Top >= taskbarRect.Bottom)
                {
                    continue;
                }

                var left = Math.Max(
                    taskbarRect.Left + margin,
                    (int)Math.Floor(bounds.Left) - OccupiedPadding);
                var right = Math.Min(
                    taskbarRect.Right - margin,
                    (int)Math.Ceiling(bounds.Right) + OccupiedPadding);
                if (right > left)
                {
                    occupied.Add(new OccupiedRange(left, right));
                }
            }

            return FindBestLeft(
                taskbarRect.Left,
                taskbarRect.Right,
                playerWidth,
                margin,
                occupied);
        }
        catch
        {
            return null;
        }
    }

    internal static int FindBestLeft(
        int taskbarLeft,
        int taskbarRight,
        int playerWidth,
        int margin,
        IEnumerable<OccupiedRange> occupiedRanges)
    {
        var start = taskbarLeft + margin;
        var end = taskbarRight - margin;
        if (playerWidth <= 0 || end - start <= playerWidth)
        {
            return start;
        }

        var merged = MergeRanges(occupiedRanges, start, end);
        if (merged.Count == 0)
        {
            return start;
        }

        var gaps = BuildGaps(merged, start, end);
        var leftGap = gaps.FirstOrDefault(gap => gap.Left == start && gap.Width >= playerWidth);
        if (leftGap.Width >= playerWidth)
        {
            return start;
        }

        var fittingGap = gaps
            .Where(gap => gap.Width >= playerWidth)
            .OrderByDescending(gap => gap.Width)
            .ThenBy(gap => Math.Abs((gap.Left + gap.Right) / 2 - (start + end) / 2))
            .FirstOrDefault();
        if (fittingGap.Width >= playerWidth)
        {
            return fittingGap.Left + (fittingGap.Width - playerWidth) / 2;
        }

        return FindLowestOverlapPosition(start, end, playerWidth, merged);
    }

    private static List<OccupiedRange> MergeRanges(
        IEnumerable<OccupiedRange> ranges,
        int start,
        int end)
    {
        var sorted = ranges
            .Select(range => new OccupiedRange(
                Math.Clamp(range.Left, start, end),
                Math.Clamp(range.Right, start, end)))
            .Where(range => range.Right > range.Left)
            .OrderBy(range => range.Left)
            .ToList();

        var merged = new List<OccupiedRange>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Left > merged[^1].Right)
            {
                merged.Add(range);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = previous with { Right = Math.Max(previous.Right, range.Right) };
        }

        return merged;
    }

    private static List<OccupiedRange> BuildGaps(
        IReadOnlyList<OccupiedRange> occupied,
        int start,
        int end)
    {
        var gaps = new List<OccupiedRange>();
        var cursor = start;
        foreach (var range in occupied)
        {
            if (range.Left > cursor)
            {
                gaps.Add(new OccupiedRange(cursor, range.Left));
            }

            cursor = Math.Max(cursor, range.Right);
        }

        if (cursor < end)
        {
            gaps.Add(new OccupiedRange(cursor, end));
        }

        return gaps;
    }

    private static int FindLowestOverlapPosition(
        int start,
        int end,
        int playerWidth,
        IReadOnlyList<OccupiedRange> occupied)
    {
        var last = end - playerWidth;
        var bestLeft = start;
        var bestOverlap = int.MaxValue;
        var taskbarCenter = (start + end) / 2;

        for (var left = start; left <= last; left += 4)
        {
            var right = left + playerWidth;
            var overlap = occupied.Sum(range =>
                Math.Max(0, Math.Min(right, range.Right) - Math.Max(left, range.Left)));
            if (overlap < bestOverlap ||
                overlap == bestOverlap &&
                Math.Abs(left + playerWidth / 2 - taskbarCenter) <
                Math.Abs(bestLeft + playerWidth / 2 - taskbarCenter))
            {
                bestOverlap = overlap;
                bestLeft = left;
            }
        }

        return bestLeft;
    }
}
