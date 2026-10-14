// 定位偏好决定目标，内容长度只限制宽度；恢复使用新发布的空闲区交集。
// 不执行探测、计时或原生写入，宿主负责提交目标和绘制实际帧。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>横向任务栏选区、限宽、紧急避让与边界恢复的纯策略。</summary>
public static class TaskbarPlacementPolicy
{
    public const double ConfirmationSeconds = 0.2;
    private const double MaximumSampleGapSeconds = 4;

    // 内容宽度不参与逻辑锚点判断；居中或靠右时，左边界会随宽度变化。
    internal static bool CanPreserveInteraction(TaskbarPlacementState previous,
        TaskbarPlacementState next, bool sameContext, bool sameCrossAxis) =>
        sameContext && sameCrossAxis && previous.IsVisible && next.IsVisible &&
        previous.HasHome && next.HasHome && !previous.NeedsSpaceRecovery && !next.NeedsSpaceRecovery &&
        !previous.IsDragSeed && !next.IsDragSeed && previous.Generation == next.Generation &&
        previous.Minimum == next.Minimum && previous.AppliedAnchorTwice == next.AppliedAnchorTwice &&
        previous.Range == next.Range && previous.Budget == next.Budget;

    public static TaskbarPlacementDecision Evaluate(TaskbarPlacementState previous,
        TaskbarOccupancySnapshot snapshot, TaskbarBarPosition alignment,
        int taskbarLength, int manualPadding, int minimum, int desired)
    {
        var state = previous;
        if (minimum <= 0)
            return Build(ClearConfirmation(state) with { IsVisible = false, IsContentHidden = true }, alignment, desired, false);
        if (snapshot.Generation < state.Generation)
            return Build(state, alignment, desired, false);
        if (snapshot.Generation != state.Generation)
            state = ClearConfirmation(state) with { Generation = snapshot.Generation, LastPublicationId = 0, LastFailurePublicationId = 0 };
        if (state.Minimum != minimum)
            state = ClearConfirmation(state) with { Minimum = minimum, MinimumNeedsValidation = true };
        if (snapshot.LastFailurePublicationId > state.LastFailurePublicationId)
            state = ClearConfirmation(state) with { LastFailurePublicationId = snapshot.LastFailurePublicationId };
        var fresh = snapshot.PublicationId > state.LastPublicationId;
        if (snapshot.Status != TaskbarProbeStatus.Success)
        {
            if (snapshot.Status == TaskbarProbeStatus.Failed && fresh)
                state = ClearConfirmation(state) with { LastPublicationId = snapshot.PublicationId };
            return Build(state with { IsVisible = state.IsVisible && state.Budget >= minimum }, alignment, desired, false);
        }
        if (fresh) state = state with { LastPublicationId = snapshot.PublicationId };
        var raw = Normalize(snapshot.Ranges);
        if (!raw.Any(range => range.Length >= minimum))
        {
            var notify = !state.FallbackNotified;
            state = ClearConfirmation(state) with { SafeRanges = [], Budget = 0, IsVisible = false,
                NeedsSpaceRecovery = true, FallbackNotified = true, MinimumNeedsValidation = false };
            return Build(state, alignment, desired, notify);
        }

        // 已知空间收缩立即生效。只有初次显示、拖动或必须离开失效区间时使用最新安全区直接落位。
        var safe = Intersect(state.SafeRanges, raw);
        if (snapshot.IsTrustedGeometry || !state.HasHome && !state.NeedsSpaceRecovery ||
            !state.NeedsSpaceRecovery && (state.IsVisible || state.MinimumNeedsValidation || state.IsContentHidden) &&
            !safe.Any(range => range.Length >= minimum))
            safe = raw;
        if (!SameRanges(safe, raw))
        {
            if (fresh)
            {
                var history = state.Observations.ToList();
                if (history.Count > 0 && (snapshot.CompletedSeconds < history[^1].Seconds ||
                    snapshot.CompletedSeconds - history[^1].Seconds > MaximumSampleGapSeconds)) history.Clear();
                history.Add(new(snapshot.CompletedSeconds, raw));
                if (history.Count > 32) history.RemoveRange(0, history.Count - 32);
                var start = history.FindLastIndex(sample => snapshot.CompletedSeconds - sample.Seconds >= ConfirmationSeconds - 0.000001);
                if (start >= 0)
                {
                    var confirmed = history[start].Ranges;
                    foreach (var sample in history.Skip(start + 1)) confirmed = Intersect(confirmed, sample.Ranges);
                    safe = Normalize(safe.Concat(confirmed));
                }
                state = state with { Observations = history.ToArray(), ConfirmationCount = history.Count,
                    FirstConfirmationSeconds = history[0].Seconds, LastConfirmationSeconds = snapshot.CompletedSeconds };
            }
        }
        else state = ClearConfirmation(state);
        var fitting = safe.Where(range => range.Length >= minimum).ToArray();
        if (fitting.Length == 0)
            return Build(state with { IsVisible = false, SafeRanges = safe, MinimumNeedsValidation = false }, alignment, desired, false);
        var range = state.IsDragSeed ? Nearest(fitting, state.AppliedAnchorTwice, minimum, alignment)
            : alignment switch
            {
                TaskbarBarPosition.Start => fitting[0],
                TaskbarBarPosition.End => fitting[^1],
                _ => Nearest(fitting, (long)taskbarLength + 2L * manualPadding, minimum, alignment)
            };
        var preferred = state.IsDragSeed ? state.AppliedAnchorTwice : alignment switch
        {
            TaskbarBarPosition.Start => 2L * (range.Start + manualPadding),
            TaskbarBarPosition.End => 2L * (range.End + manualPadding),
            _ => (long)taskbarLength + 2L * manualPadding
        };
        var anchor = Project(preferred, range, minimum, alignment);
        var budget = Budget(range, anchor, alignment);
        state = state with { SafeRanges = safe, Range = range, HasHome = true,
            HomeAnchorTwice = alignment == TaskbarBarPosition.Center ? preferred : anchor,
            AppliedAnchorTwice = anchor, Budget = budget, ConfirmationBudget = budget,
            IsVisible = true, IsContentHidden = false, NeedsSpaceRecovery = false, MinimumNeedsValidation = false,
            FallbackNotified = false };
        return Build(state, alignment, desired, false);
    }

    public static TaskbarPlacementState ClearConfirmation(TaskbarPlacementState state) => state with
    {
        Observations = [], CandidateAnchorTwice = null, ConfirmationCount = 0, ConfirmationBudget = 0,
        FirstConfirmationSeconds = 0, LastConfirmationSeconds = 0, CandidateIsHome = false
    };

    public static int Position(long anchorTwice, int width, TaskbarBarPosition alignment) => alignment switch
    {
        TaskbarBarPosition.Start => (int)(anchorTwice / 2),
        TaskbarBarPosition.End => (int)(anchorTwice / 2) - width,
        _ => (int)Math.Floor((anchorTwice - width) / 2.0)
    };

    private static TaskbarPlacementDecision Build(TaskbarPlacementState state, TaskbarBarPosition alignment, int desired, bool notify)
    {
        var visible = state.IsVisible && state.Budget >= state.Minimum && state.Minimum > 0;
        var width = visible ? Math.Clamp(desired, state.Minimum, state.Budget) : 0;
        return new(state with { IsVisible = visible }, Position(state.AppliedAnchorTwice, width, alignment), width, notify);
    }

    private static long Project(long anchor, TaskbarPrimaryRange range, int minimum, TaskbarBarPosition alignment)
    {
        var low = 2L * range.Start + (alignment == TaskbarBarPosition.End ? 2L * minimum : alignment == TaskbarBarPosition.Center ? minimum : 0);
        var high = 2L * range.End - (alignment == TaskbarBarPosition.Start ? 2L * minimum : alignment == TaskbarBarPosition.Center ? minimum : 0);
        return Math.Clamp(anchor, low, high);
    }

    private static int Budget(TaskbarPrimaryRange range, long anchor, TaskbarBarPosition alignment) => alignment switch
    {
        TaskbarBarPosition.Start => (int)(range.End - anchor / 2),
        TaskbarBarPosition.End => (int)(anchor / 2 - range.Start),
        _ => (int)Math.Min(anchor - 2L * range.Start, 2L * range.End - anchor)
    };

    private static TaskbarPrimaryRange Nearest(IReadOnlyList<TaskbarPrimaryRange> ranges, long anchor, int minimum, TaskbarBarPosition alignment) =>
        ranges.OrderBy(range => Math.Abs(Project(anchor, range, minimum, alignment) - anchor))
            .ThenByDescending(range => range.Length).ThenBy(range => range.Start).First();

    private static bool SameRanges(IReadOnlyList<TaskbarPrimaryRange> left, IReadOnlyList<TaskbarPrimaryRange> right) => left.SequenceEqual(right);

    private static TaskbarPrimaryRange[] Normalize(IEnumerable<TaskbarPrimaryRange> ranges)
    {
        var result = new List<TaskbarPrimaryRange>();
        foreach (var range in ranges.Where(range => range.Length > 0).OrderBy(range => range.Start))
        {
            if (result.Count > 0 && result[^1].End >= range.Start)
                result[^1] = new(result[^1].Start, Math.Max(result[^1].End, range.End));
            else result.Add(range);
        }
        return result.ToArray();
    }

    private static TaskbarPrimaryRange[] Intersect(IReadOnlyList<TaskbarPrimaryRange> left, IReadOnlyList<TaskbarPrimaryRange> right)
    {
        var result = new List<TaskbarPrimaryRange>();
        foreach (var first in left)
            foreach (var second in right)
            {
                var start = Math.Max(first.Start, second.Start);
                var end = Math.Min(first.End, second.End);
                if (end > start) result.Add(new(start, end));
            }
        return Normalize(result);
    }
}
