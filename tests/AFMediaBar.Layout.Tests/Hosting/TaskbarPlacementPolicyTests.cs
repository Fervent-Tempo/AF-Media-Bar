// 验证标题变长、区间变化和探测发布时序不会制造不必要位移；不创建真实任务栏窗口。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏锚点、预算和回退恢复回归。</summary>
[TestClass]
public sealed class TaskbarPlacementPolicyTests
{
    private static TaskbarPlacementDecision Plan(TaskbarPlacementState state, long id, double time,
        TaskbarBarPosition alignment, int desired, params TaskbarPrimaryRange[] ranges) =>
        TaskbarPlacementPolicy.Evaluate(state, new(TaskbarProbeStatus.Success, 1, id, time, ranges), alignment, 2000, 0, 200, desired);

    [DataTestMethod]
    [DataRow(TaskbarBarPosition.Start)]
    [DataRow(TaskbarBarPosition.Center)]
    [DataRow(TaskbarBarPosition.End)]
    public void ContentWidthChangesPreserveInteractionAtTheSameAnchor(TaskbarBarPosition alignment)
    {
        var range = new TaskbarPrimaryRange(20, 1900);
        var first = Plan(new(), 1, 0, alignment, 300, range);
        var longer = Plan(first.State, 1, 0, alignment, 601, range);
        var shorter = Plan(longer.State, 1, 0, alignment, 250, range);
        Assert.AreNotEqual(first.Width, longer.Width);
        if (alignment != TaskbarBarPosition.Start)
            Assert.AreNotEqual(first.Position, longer.Position, "左边界变化不能误判为逻辑锚点移动。");
        Assert.IsTrue(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, longer.State, true, true));
        Assert.IsTrue(TaskbarPlacementPolicy.CanPreserveInteraction(longer.State, shorter.State, true, true));
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, longer.State, false, true),
            "DPI、对齐或宿主环境变化不能沿用内容变化的交互状态。");
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, longer.State, true, false));
    }

    [TestMethod]
    public void SpaceChangesAndVisibilityLossStillRequireInteractionCleanup()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1200, 1900));
        var constrained = Plan(first.State, 2, 1, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1500, 1900));
        Assert.AreEqual(first.State.AppliedAnchorTwice, constrained.State.AppliedAnchorTwice);
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, constrained.State, true, true),
            "锚点未移动也必须识别真实空间收缩。");
        var freeing = Plan(constrained.State, 3, 2, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1200, 1900));
        var restored = Plan(freeing.State, 4, 2.3, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1200, 1900));
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(constrained.State, restored.State, true, true));
        var moved = Plan(first.State, 5, 3, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1200, 1800));
        Assert.AreNotEqual(first.State.AppliedAnchorTwice, moved.State.AppliedAnchorTwice);
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, moved.State, true, true));
        var hidden = Plan(first.State, 6, 4, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(1850, 1900));
        Assert.IsFalse(hidden.State.IsVisible);
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, hidden.State, true, true));
        Assert.IsFalse(TaskbarPlacementPolicy.CanPreserveInteraction(first.State, first.State with { Generation = 2 }, true, true));
    }

    [TestMethod]
    public void LongTitleClampsBeforeConsideringAnotherRange()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(20, 900), new TaskbarPrimaryRange(1400, 1900));
        var wide = Plan(first.State, 1, 0, TaskbarBarPosition.End, 1200, new TaskbarPrimaryRange(20, 900), new TaskbarPrimaryRange(1400, 1900));
        Assert.AreEqual(1900, wide.Position + wide.Width);
        Assert.AreEqual(500, wide.Width);
        Assert.IsFalse(wide.NotifyFallback);
    }

    [TestMethod]
    public void ExpansionFollowsCurrentBoundaryButCachedReadsDoNotConfirmSpace()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 800, new TaskbarPrimaryRange(400, 1000));
        var expanded = Plan(first.State, 2, 1.5, TaskbarBarPosition.Start, 800, new TaskbarPrimaryRange(200, 1200));
        Assert.AreEqual(400, expanded.Position);
        Assert.AreEqual(600, expanded.Width);
        var cached = Plan(expanded.State, 2, 99, TaskbarBarPosition.Start, 800, new TaskbarPrimaryRange(200, 1200));
        Assert.AreEqual(expanded.State.ConfirmationCount, cached.State.ConfirmationCount);
        var middle = Plan(cached.State, 3, 3, TaskbarBarPosition.Start, 800, new TaskbarPrimaryRange(200, 1190));
        var stable = Plan(middle.State, 4, 4.5, TaskbarBarPosition.Start, 800, new TaskbarPrimaryRange(180, 1200));
        Assert.AreEqual(200, stable.Position);
        Assert.AreEqual(800, stable.Width);
    }

    [TestMethod]
    public void IntrusionShrinksImmediatelyAndOnlyMinimumFailureMoves()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1400, 1900));
        var smaller = Plan(first.State, 2, 1, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1600, 1900));
        Assert.AreEqual(1900, smaller.Position + smaller.Width);
        Assert.AreEqual(300, smaller.Width);
        Assert.IsFalse(smaller.NotifyFallback);
        var moved = Plan(smaller.State, 3, 2, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1750, 1900));
        Assert.IsFalse(moved.NotifyFallback);
        var again = Plan(moved.State, 4, 3, TaskbarBarPosition.End, 800, new TaskbarPrimaryRange(20, 900), new TaskbarPrimaryRange(1750, 1900));
        Assert.IsFalse(again.NotifyFallback);
    }

    [TestMethod]
    public void RecoveryUsesHomeAndIgnoresPendingCacheReads()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(1400, 1900));
        var moved = Plan(first.State, 2, 1, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(20, 1000));
        var begin = Plan(moved.State, 3, 2, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1200, 1950));
        var pending = TaskbarPlacementPolicy.Evaluate(begin.State, new(TaskbarProbeStatus.Pending, 1, 0, 99, []), TaskbarBarPosition.End, 2000, 0, 200, 300);
        Assert.AreEqual(begin.State.ConfirmationCount, pending.State.ConfirmationCount);
        var next = Plan(pending.State, 4, 3.5, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1200, 1950));
        var restored = Plan(next.State, 5, 5, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(20, 1000), new TaskbarPrimaryRange(1200, 1950));
        Assert.AreEqual(1950, restored.Position + restored.Width);
        Assert.IsFalse(restored.NotifyFallback);
    }

    [TestMethod]
    public void FailureAndLargeSampleGapRestartConfirmationAndOldGenerationsDoNotApply()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 600));
        var begin = Plan(first.State, 2, 1, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 1000));
        var failed = TaskbarPlacementPolicy.Evaluate(begin.State, new(TaskbarProbeStatus.Failed, 1, 3, 2, []), TaskbarBarPosition.Start, 2000, 0, 200, 700);
        Assert.AreEqual(0, failed.State.ConfirmationCount);
        var restart = Plan(failed.State, 4, 3, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 1000));
        var gap = Plan(restart.State, 5, 8, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 1000));
        Assert.AreEqual(1, gap.State.ConfirmationCount);
        var stale = TaskbarPlacementPolicy.Evaluate(gap.State, new(TaskbarProbeStatus.Success, 0, 99, 9, []), TaskbarBarPosition.Start, 2000, 0, 200, 700);
        Assert.AreEqual(gap.State, stale.State);
    }

    [TestMethod]
    public void NoRoomHidesWithoutManufacturingWidthAndOnlyNotifiesOnce()
    {
        var hidden = Plan(new(), 1, 0, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(100, 150));
        Assert.AreEqual(0, hidden.Width);
        Assert.IsFalse(hidden.State.IsVisible);
        Assert.IsTrue(hidden.NotifyFallback);
        var repeated = Plan(hidden.State, 2, 1, TaskbarBarPosition.End, 900);
        Assert.IsFalse(repeated.NotifyFallback);
        var begin = Plan(repeated.State, 3, 2, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(1400, 1900));
        Assert.IsFalse(begin.State.IsVisible);
        var visible = Plan(begin.State, 4, 4, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(1400, 1900));
        Assert.IsTrue(visible.State.IsVisible);
        Assert.AreEqual(500, visible.Width);
        Assert.AreEqual(100, TaskbarExperiencePolicy.ResolvePrimaryLength(900, 200, 100, TaskbarLengthMode.Fixed, 900));
    }

    [TestMethod]
    public void CenterBudgetIsSymmetricAndPixelParityDoesNotChangeTheHome()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Center, 900, new TaskbarPrimaryRange(800, 1400));
        Assert.AreEqual(400, first.Width);
        Assert.AreEqual(2000L, first.State.HomeAnchorTwice);
        foreach (var width in new[] { 201, 202, 203 })
        {
            var frame = Plan(first.State, 1, 0, TaskbarBarPosition.Center, width, new TaskbarPrimaryRange(800, 1400));
            Assert.AreEqual((int)Math.Floor((2000 - width) / 2.0), frame.Position);
        }
        Assert.AreEqual(500.0, TaskbarArrangementPolicy.MinimumLength(200, 40, 100, TaskbarBarPosition.Center));
        Assert.AreEqual(340.0, TaskbarArrangementPolicy.MinimumLength(200, 40, 100, TaskbarBarPosition.Start));
    }
    [TestMethod]
    public void UnsafeInterveningSampleCancelsBudgetExpansion()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 500));
        var begin = Plan(first.State, 2, 1, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 800));
        var interrupted = Plan(begin.State, 3, 2, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 500));
        var again = Plan(interrupted.State, 4, 3, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 800));
        Assert.AreEqual(300, again.State.Budget);
        Assert.AreEqual(1, again.State.ConfirmationCount);
    }

    [TestMethod]
    public void StructuralMinimumChangeDoesNotHideAStillFeasibleAnchor()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 300, new TaskbarPrimaryRange(200, 500));
        var begin = Plan(first.State, 2, 1, TaskbarBarPosition.Start, 300, new TaskbarPrimaryRange(200, 800));
        var changed = TaskbarPlacementPolicy.Evaluate(begin.State,
            new(TaskbarProbeStatus.Success, 1, 3, 2, [new(200, 800)]), TaskbarBarPosition.Start, 2000, 0, 400, 500);
        Assert.IsTrue(changed.State.IsVisible);
        Assert.AreEqual(200, changed.Position);
        Assert.IsTrue(changed.Width >= 400);
    }

    [TestMethod]
    public void EmptyContentDoesNotBecomeSpaceFailureOrDelayReconnection()
    {
        var snapshot = new TaskbarOccupancySnapshot(TaskbarProbeStatus.Success, 1, 1, 0, [new(200, 1000)]);
        var first = TaskbarPlacementPolicy.Evaluate(new(), snapshot, TaskbarBarPosition.Start, 2000, 0, 200, 500);
        var empty = TaskbarPlacementPolicy.Evaluate(first.State, snapshot, TaskbarBarPosition.Start, 2000, 0, 0, 0);
        Assert.IsFalse(empty.NotifyFallback);
        var connected = TaskbarPlacementPolicy.Evaluate(empty.State, snapshot, TaskbarBarPosition.Start, 2000, 0, 200, 500);
        Assert.IsTrue(connected.State.IsVisible);
        Assert.AreEqual(200, connected.Position);
    }

    [TestMethod]
    public void SuccessSnapshotCarriesFailuresThatTheDispatcherDidNotYetConsume()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 500));
        var begin = Plan(first.State, 2, 1, TaskbarBarPosition.Start, 700, new TaskbarPrimaryRange(200, 800));
        var afterFailure = TaskbarPlacementPolicy.Evaluate(begin.State,
            new(TaskbarProbeStatus.Success, 1, 4, 3, [new(200, 800)], LastFailurePublicationId: 3),
            TaskbarBarPosition.Start, 2000, 0, 200, 700);
        Assert.AreEqual(300, afterFailure.State.Budget);
        Assert.AreEqual(1, afterFailure.State.ConfirmationCount);
    }

    [TestMethod]
    public void MinimumChangeWhileProbeIsPendingDoesNotBecomeSpaceRecovery()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Start, 300, new TaskbarPrimaryRange(200, 500));
        var pending = TaskbarPlacementPolicy.Evaluate(first.State,
            new(TaskbarProbeStatus.Pending, 1, 0, 1, []), TaskbarBarPosition.Start, 2000, 0, 400, 500);
        Assert.IsFalse(pending.State.IsVisible);
        Assert.IsFalse(pending.State.NeedsSpaceRecovery);
        var successful = TaskbarPlacementPolicy.Evaluate(pending.State,
            new(TaskbarProbeStatus.Success, 1, 2, 2, [new(200, 800)]), TaskbarBarPosition.Start, 2000, 0, 400, 500);
        Assert.IsTrue(successful.State.IsVisible);
        Assert.IsFalse(successful.NotifyFallback);
    }

    [TestMethod]
    public void ShrinkingFallbackRangeDoesNotCancelIndependentHomeConfirmation()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(1400, 1900));
        var moved = Plan(first.State, 2, 1, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(200, 1000));
        var begin = Plan(moved.State, 3, 2, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(210, 1000), new TaskbarPrimaryRange(1400, 1900));
        var next = Plan(begin.State, 4, 3.5, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(220, 1000), new TaskbarPrimaryRange(1400, 1900));
        var recovered = Plan(next.State, 5, 5, TaskbarBarPosition.End, 900, new TaskbarPrimaryRange(230, 1000), new TaskbarPrimaryRange(1400, 1900));
        Assert.AreEqual(1900, recovered.Position + recovered.Width);
        Assert.IsFalse(recovered.NotifyFallback);
    }

    [TestMethod]
    public void ExplicitDragHomeIsNotReplacedByANewEndmostRange()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.End, 300, new TaskbarPrimaryRange(200, 1000));
        var dragged = TaskbarPlacementPolicy.ClearConfirmation(first.State) with
        { HomeAnchorTwice = 1800, AppliedAnchorTwice = 1800, IsDragSeed = true };
        var next = Plan(dragged, 2, 1, TaskbarBarPosition.End, 300,
            new TaskbarPrimaryRange(200, 1000), new TaskbarPrimaryRange(1400, 1900));
        Assert.AreEqual(900, next.Position + next.Width);
        Assert.AreEqual(1800L, next.State.HomeAnchorTwice);
        Assert.IsFalse(next.NotifyFallback);
    }

    [DataTestMethod]
    [DataRow(TaskbarBarPosition.Start)]
    [DataRow(TaskbarBarPosition.End)]
    public void ConsecutiveClosuresRecoverBeyondTheFirstObservedBoundary(TaskbarBarPosition alignment)
    {
        TaskbarPrimaryRange Range(int boundary) => alignment == TaskbarBarPosition.Start ? new(boundary, 1900) : new(100, 2000 - boundary);
        var first = Plan(new(), 1, 0, alignment, 300, Range(400));
        var intrusion = Plan(first.State, 2, 1, alignment, 300, Range(600));
        var freeing = Plan(intrusion.State, 3, 1.1, alignment, 300, Range(500));
        var more = Plan(freeing.State, 4, 1.2, alignment, 300, Range(300));
        var partial = Plan(more.State, 5, 1.3, alignment, 300, Range(300));
        Assert.AreEqual(500, alignment == TaskbarBarPosition.Start ? partial.Position : 2000 - partial.Position - partial.Width);
        var final = Plan(partial.State, 6, 1.4, alignment, 300, Range(300));
        Assert.AreEqual(300, alignment == TaskbarBarPosition.Start ? final.Position : 2000 - final.Position - final.Width);
    }

    [TestMethod]
    public void CenterMovesCloserEvenBeforeItsExactCenterCanFit()
    {
        var first = Plan(new(), 1, 0, TaskbarBarPosition.Center, 200, new TaskbarPrimaryRange(1100, 1900));
        var freeing = Plan(first.State, 2, 1, TaskbarBarPosition.Center, 200, new TaskbarPrimaryRange(1000, 1900));
        var confirmed = Plan(freeing.State, 3, 1.2, TaskbarBarPosition.Center, 200, new TaskbarPrimaryRange(1000, 1900));
        Assert.AreEqual(1000, confirmed.Position);
        Assert.AreEqual(2000L, confirmed.State.HomeAnchorTwice);
    }

}
