// 验证释放避让跨不连续区间时保持完整尺寸，并且不抢占静止组件。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏独立对象的空闲区扣除及最近落点回归。</summary>
[TestClass]
public sealed class ComponentPlacementPolicyTests
{
    [TestMethod]
    public void DropAcrossIconsChoosesNearestFullSizeInsteadOfOriginalRange()
    {
        TaskbarPrimaryRange[] ranges = [new(0, 120), new(400, 700), new(900, 1000)];
        Assert.AreEqual(400, ComponentPlacementPolicy.Nearest(ranges, 360, 150));
        Assert.AreEqual(20, ComponentPlacementPolicy.Nearest(ranges, 190, 100));
        Assert.IsNull(ComponentPlacementPolicy.Nearest(ranges, 360, 350));
    }

    [TestMethod]
    public void ReservedObjectsKeepTheirPlacesAndEqualDistanceUsesLowerCoordinate()
    {
        var free = ComponentPlacementPolicy.Subtract([new TaskbarPrimaryRange(0, 1000)],
            [new TaskbarPrimaryRange(200, 400), new TaskbarPrimaryRange(600, 800)], 10);
        Assert.AreEqual(3, free.Count);
        Assert.AreEqual(490, ComponentPlacementPolicy.Nearest(free, 550, 100));
        Assert.AreEqual(100, ComponentPlacementPolicy.Nearest([new(0, 200), new(400, 600)], 250, 100));
        Assert.IsNull(ComponentPlacementPolicy.Nearest(free, 500, 200));
    }

    [TestMethod]
    public void ExplicitBodyHomeSurvivesSubsequentStartAlignedUpdates()
    {
        var snapshot = new TaskbarOccupancySnapshot(TaskbarProbeStatus.Success, 1, 1, 0,
            [new(0, 300), new(700, 1100)], true);
        var first = TaskbarPlacementPolicy.Evaluate(new(), snapshot, AFMediaBar.Classes.Settings.TaskbarBarPosition.Start, 1200, 0, 100, 200, 1600);
        var next = TaskbarPlacementPolicy.Evaluate(first.State, snapshot with { PublicationId = 2, CompletedSeconds = 1 },
            AFMediaBar.Classes.Settings.TaskbarBarPosition.Start, 1200, 0, 100, 200, 1600);
        Assert.AreEqual(800, first.Position);
        Assert.AreEqual(800, next.Position, "普通刷新不能把显式落点重新拉到最左侧区间。");
        Assert.IsFalse(next.State.IsDragSeed, "持久位置不应让每一轮布局都进入拖动过渡。");
    }
}
