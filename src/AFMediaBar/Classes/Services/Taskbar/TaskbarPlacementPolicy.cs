// 以已应用锚点和最小布局选择位置，内容长度只决定受限宽度。
// 纯策略不执行 I/O 或计时；宿主提供探测快照并拥有提交状态。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>统一任务栏选区、限宽、避让与恢复的纯决策。</summary>
public static class TaskbarPlacementPolicy
{
    private const double ConfirmationSeconds = 2;
    private const double MaximumSampleGapSeconds = 4;

    /// <summary>计算下一次布局，重复查询同一发布不会推进确认。</summary>
    public static TaskbarPlacementDecision Evaluate(TaskbarPlacementState previous,
        TaskbarOccupancySnapshot snapshot, TaskbarBarPosition alignment,
        int taskbarLength, int manualPadding, int minimum, int desired)
    {
        var state = previous;
        if (minimum <= 0)
            return Build(ClearConfirmation(state) with
            {
                IsVisible = false,
                IsContentHidden = state.IsVisible || state.IsContentHidden
            }, alignment, desired, false);
        if (snapshot.Generation < state.Generation)
            return Build(state, alignment, desired, false);
        if (snapshot.Generation != state.Generation)
            state = ClearConfirmation(state) with { Generation = snapshot.Generation, LastPublicationId = 0, LastFailurePublicationId = 0 };
        var minimumChanged = state.Minimum != minimum;
        if (minimumChanged)
            state = ClearConfirmation(state) with { Minimum = minimum, MinimumNeedsValidation = true };
        if (snapshot.LastFailurePublicationId > state.LastFailurePublicationId)
            state = ClearConfirmation(state) with { LastFailurePublicationId = snapshot.LastFailurePublicationId };
        var fresh = snapshot.PublicationId > state.LastPublicationId;
        if (snapshot.Status != TaskbarProbeStatus.Success)
        {
            if (snapshot.Status == TaskbarProbeStatus.Failed && fresh)
                state = ClearConfirmation(state) with { LastPublicationId = snapshot.PublicationId };
            if (state.Budget < minimum)
                state = state with { IsVisible = false };
            return Build(state, alignment, desired, false);
        }
        var ranges = snapshot.Ranges.Where(range => range.Length >= minimum).OrderBy(range => range.Start).ToArray();
        var structuralRevalidation = state.MinimumNeedsValidation;
        var oldVisible = state.IsVisible || state.IsContentHidden ||
            (state.HasHome && (!state.NeedsSpaceRecovery || structuralRevalidation));
        state = state with { IsContentHidden = false, MinimumNeedsValidation = false };
        if (fresh)
            state = state with { LastPublicationId = snapshot.PublicationId };
        if (ranges.Length == 0)
        {
            var notify = !state.FallbackNotified;
            state = ClearConfirmation(state) with { IsVisible = false, Budget = 0, FallbackNotified = true, NeedsSpaceRecovery = true };
            return Build(state, alignment, desired, notify);
        }

        if (structuralRevalidation)
            state = state with { NeedsSpaceRecovery = false };
        if (!state.HasHome)
        {
            var target = alignment == TaskbarBarPosition.Center ? (long)taskbarLength + 2L * manualPadding : 0;
            var range = alignment switch
            {
                TaskbarBarPosition.Start => ranges[0],
                TaskbarBarPosition.End => ranges[^1],
                _ => Nearest(ranges, target, minimum, alignment, default)
            };
            var anchor = alignment switch
            {
                TaskbarBarPosition.Start => Project(2L * (range.Start + manualPadding), range, minimum, alignment),
                TaskbarBarPosition.End => Project(2L * (range.End + manualPadding), range, minimum, alignment),
                _ => Project(target, range, minimum, alignment)
            };
            var budget = Budget(range, anchor, alignment);
            // After a genuine no-room episode, reappearance also needs fresh stable observations.
            if (state.FallbackNotified)
            {
                state = Confirm(state, anchor, true, budget, snapshot, fresh, out var ready);
                if (!ready)
                    return Build(state with { IsVisible = false }, alignment, desired, false);
                budget = state.ConfirmationBudget;
            }
            state = ClearConfirmation(state) with
            {
                HasHome = true,
                HomeAnchorTwice = alignment == TaskbarBarPosition.Center ? target : anchor,
                AppliedAnchorTwice = anchor,
                Range = range,
                Budget = budget,
                IsVisible = true,
                FallbackNotified = false,
                NeedsSpaceRecovery = false
            };
            return Build(state, alignment, desired, false);
        }

        var homeRange = ranges.FirstOrDefault(range => Project(state.HomeAnchorTwice, range, minimum, alignment) == state.HomeAnchorTwice);
        if (state.AppliedAnchorTwice != state.HomeAnchorTwice || !oldVisible)
        {
            if (homeRange.Length > 0)
            {
                state = Confirm(state, state.HomeAnchorTwice, true,
                    Budget(homeRange, state.HomeAnchorTwice, alignment), snapshot, fresh, out var ready);
                if (ready)
                {
                    state = ClearConfirmation(state) with
                    {
                        AppliedAnchorTwice = state.HomeAnchorTwice,
                        Range = homeRange,
                        Budget = state.ConfirmationBudget,
                        IsVisible = true,
                        FallbackNotified = false,
                        NeedsSpaceRecovery = false
                    };
                    return Build(state, alignment, desired, false);
                }
            }
            else if (state.CandidateIsHome)
                state = ClearConfirmation(state);
        }

        var selected = ranges.FirstOrDefault(range => Project(state.AppliedAnchorTwice, range, minimum, alignment) == state.AppliedAnchorTwice);
        if (selected.Length == 0)
        {
            var sameRegion = ranges.Where(range => range.Start < state.Range.End && range.End > state.Range.Start).ToArray();
            selected = Nearest(sameRegion.Length > 0 ? sameRegion : ranges,
                state.AppliedAnchorTwice, minimum, alignment, state.Range);
        }
        var applied = Project(state.AppliedAnchorTwice, selected, minimum, alignment);
        var available = Budget(selected, applied, alignment);
        var moved = applied != state.AppliedAnchorTwice;
        var notifyMove = oldVisible && moved && applied != state.HomeAnchorTwice && !state.FallbackNotified;
        if (!oldVisible)
        {
            // A safe current alternative may reappear even while the home location is still blocked.
            if (homeRange.Length == 0)
                state = Confirm(state, applied, false, available, snapshot, fresh, out _);
            if (state.ConfirmationCount < 2 || state.LastConfirmationSeconds - state.FirstConfirmationSeconds < ConfirmationSeconds)
                return Build(state with { IsVisible = false }, alignment, desired, false);
            available = Math.Min(available, state.ConfirmationBudget);
            state = ClearConfirmation(state);
        }
        else if (!moved && available > state.Budget)
        {
            // A structural layout change is revalidated against this successful snapshot.
            // Admit its feasible minimum now; extra space still uses the expansion confirmation.
            if (!state.NeedsSpaceRecovery && state.Budget < minimum)
                state = state with { Budget = minimum };
            if (!(state.CandidateIsHome && state.AppliedAnchorTwice != state.HomeAnchorTwice))
            {
                state = Confirm(state, applied, false, available, snapshot, fresh, out var ready);
                available = ready ? state.ConfirmationBudget : state.Budget;
                if (ready)
                    state = ClearConfirmation(state);
            }
            else
                available = state.Budget;
        }
        else if (available < state.Budget || moved || (available == state.Budget && !state.CandidateIsHome))
        {
            if (!(state.CandidateIsHome && homeRange.Length > 0))
                state = ClearConfirmation(state);
        }
        if (applied == state.HomeAnchorTwice)
            state = (state.CandidateIsHome ? ClearConfirmation(state) : state) with { FallbackNotified = false };
        state = state with
        {
            AppliedAnchorTwice = applied,
            Range = selected,
            Budget = available,
            IsVisible = true,
            NeedsSpaceRecovery = false,
            FallbackNotified = state.FallbackNotified || notifyMove
        };
        return Build(state, alignment, desired, notifyMove);
    }

    /// <summary>暂停或真实失败清理确认，不改变正常和已应用锚点。</summary>
    public static TaskbarPlacementState ClearConfirmation(TaskbarPlacementState state) => state with
    {
        CandidateAnchorTwice = null,
        ConfirmationCount = 0,
        ConfirmationBudget = 0,
        FirstConfirmationSeconds = 0,
        LastConfirmationSeconds = 0,
        CandidateIsHome = false
    };

    /// <summary>由二倍锚点计算窗口的整数物理像素左缘。</summary>
    public static int Position(long anchorTwice, int width, TaskbarBarPosition alignment) => alignment switch
    {
        TaskbarBarPosition.Start => (int)(anchorTwice / 2),
        TaskbarBarPosition.End => (int)(anchorTwice / 2) - width,
        _ => (int)Math.Floor((anchorTwice - width) / 2.0)
    };

    private static TaskbarPlacementDecision Build(TaskbarPlacementState state,
        TaskbarBarPosition alignment, int desired, bool notify)
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

    private static TaskbarPrimaryRange Nearest(IReadOnlyList<TaskbarPrimaryRange> ranges,
        long anchor, int minimum, TaskbarBarPosition alignment, TaskbarPrimaryRange previous) =>
        ranges.OrderBy(range => Math.Abs(Project(anchor, range, minimum, alignment) - anchor))
            .ThenByDescending(range => range.Start < previous.End && range.End > previous.Start)
            .ThenByDescending(range => range.Length).ThenBy(range => range.Start).First();

    private static TaskbarPlacementState Confirm(TaskbarPlacementState state, long target, bool home,
        int budget, TaskbarOccupancySnapshot snapshot, bool fresh, out bool ready)
    {
        ready = false;
        if (snapshot.IsTrustedGeometry)
        {
            ready = true;
            return state with
            {
                ConfirmationBudget = budget,
                ConfirmationCount = 2,
                FirstConfirmationSeconds = snapshot.CompletedSeconds - ConfirmationSeconds,
                LastConfirmationSeconds = snapshot.CompletedSeconds
            };
        }
        if (!fresh)
            return state;
        var restart = state.CandidateAnchorTwice != target || state.CandidateIsHome != home ||
            snapshot.CompletedSeconds < state.LastConfirmationSeconds ||
            snapshot.CompletedSeconds - state.LastConfirmationSeconds > MaximumSampleGapSeconds;
        state = state with
        {
            CandidateAnchorTwice = target,
            CandidateIsHome = home,
            FirstConfirmationSeconds = restart ? snapshot.CompletedSeconds : state.FirstConfirmationSeconds,
            LastConfirmationSeconds = snapshot.CompletedSeconds,
            ConfirmationCount = restart ? 1 : state.ConfirmationCount + 1,
            ConfirmationBudget = restart ? budget : Math.Min(budget, state.ConfirmationBudget)
        };
        ready = state.ConfirmationCount >= 2 &&
            state.LastConfirmationSeconds - state.FirstConfirmationSeconds >= ConfirmationSeconds;
        return state;
    }
}
