using System.Windows.Automation;
using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 通过 UI Automation 扫描任务栏占用区域，并选择尽量不遮挡图标的位置。
/// Scans taskbar occupancy through UI Automation and chooses a low-overlap position.
/// </summary>
internal sealed class TaskbarPlacementService
{
    private const int OccupiedPadding = 4;
    private readonly object _scanSync = new();
    // UI Automation 扫描较慢；并发请求共享同一个在途任务，避免重复遍历 Explorer。
    // UI Automation is slow; concurrent callers share one scan instead of rescanning Explorer.
    private Task<TaskbarPlacementResult?>? _activeScan;

    internal Task<TaskbarPlacementResult?> FindBestLeftAsync(
        nint taskbar,
        NativeMethods.Rect taskbarRect,
        int playerWidth,
        int margin,
        int? preferredLeft)
    {
        lock (_scanSync)
        {
            if (_activeScan is null || _activeScan.IsCompleted)
            {
                _activeScan = Task.Run(() =>
                    FindBestLeft(
                        taskbar,
                        taskbarRect,
                        playerWidth,
                        margin,
                        preferredLeft));
            }

            return _activeScan;
        }
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

        const int stableLeft = 90;
        var stable = FindBestLeft(
            taskbarLeft,
            taskbarRight,
            playerWidth,
            margin,
            [new OccupiedRange(642, 1193), new OccupiedRange(1265, 1920)],
            stableLeft);
        if (stable != stableLeft)
        {
            throw new InvalidOperationException(
                "A clear current position must remain stable.");
        }

        var overlapping = FindBestLeft(
            taskbarLeft,
            taskbarRight,
            playerWidth,
            margin,
            [new OccupiedRange(642, 1193), new OccupiedRange(1265, 1920)],
            700);
        if (overlapping == 700)
        {
            throw new InvalidOperationException(
                "An overlapping current position must be recalculated.");
        }
    }

    private static TaskbarPlacementResult? FindBestLeft(
        nint taskbar,
        NativeMethods.Rect taskbarRect,
        int playerWidth,
        int margin,
        int? preferredLeft)
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
                if (button.Current.ProcessId == Environment.ProcessId)
                {
                    // An embedded AF Media Bar is part of the taskbar UIA tree.
                    // It must not be treated as an occupied Explorer button range.
                    continue;
                }

                var bounds = button.Current.BoundingRectangle;
                if (bounds.Width <= 1 || bounds.Width > 260 || bounds.Height <= 1)
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

            return new TaskbarPlacementResult(
                FindBestLeft(
                    taskbarRect.Left,
                    taskbarRect.Right,
                    playerWidth,
                    margin,
                    occupied,
                    preferredLeft),
                occupied.Count);
        }
        catch
        {
            // Windows 更新或定制任务栏可能改变自动化树，失败时由调用方保留旧位置。
            // Windows updates or custom taskbars may alter the tree; callers keep the old position.
            return null;
        }
    }

    internal static int FindBestLeft(
        int taskbarLeft,
        int taskbarRight,
        int playerWidth,
        int margin,
        IEnumerable<OccupiedRange> occupiedRanges,
        int? preferredLeft = null)
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
            return preferredLeft.HasValue
                ? Math.Clamp(preferredLeft.Value, start, end - playerWidth)
                : start;
        }

        if (preferredLeft.HasValue)
        {
            var stableLeft = Math.Clamp(preferredLeft.Value, start, end - playerWidth);
            var stableRight = stableLeft + playerWidth;
            var overlapsOccupiedRange = merged.Any(range =>
                stableLeft < range.Right && stableRight > range.Left);
            if (!overlapsOccupiedRange)
            {
                return stableLeft;
            }
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
