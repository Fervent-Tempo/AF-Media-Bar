// Verifies physical taskbar anchors and safe-range selection as content widths change.
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarAlignmentTests
{
    [TestMethod]
    public void WidthChangesKeepLeftRightAndCenterAnchors()
    {
        foreach (var width in new[] { 200, 400, 600 })
        {
            var range = new TaskbarPrimaryRange(100, 1800);
            Assert.AreEqual(100, Place(width, range, TaskbarBarPosition.Start).Primary);
            Assert.AreEqual(1800, Place(width, range, TaskbarBarPosition.End).Primary + width);
            Assert.AreEqual(1000, Place(width, range, TaskbarBarPosition.Center).Primary + width / 2);
        }
    }

    [TestMethod]
    public void CenterClampsToSafeRangeAndOversizedBarsStayAtItsStart()
    {
        var range = new TaskbarPrimaryRange(1200, 1800);
        Assert.AreEqual(1200, Place(200, range, TaskbarBarPosition.Center).Primary);
        Assert.AreEqual(1200, Place(800, range, TaskbarBarPosition.End).Primary);
        var placement = TaskbarBarPlacementCalculator.Calculate(
            2000, 200, 40, 80, range, TaskbarBarPosition.Center, 0, -20, 1.5, 20);
        Assert.AreEqual(0, placement.Cross);
    }

    [TestMethod]
    public void CenterSelectsClosestAttainableAnchorRatherThanLargestGap()
    {
        TaskbarPrimaryRange[] ranges = [new(20, 750), new(850, 1150), new(1500, 1980)];
        Assert.AreEqual(ranges[1], TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Center, 200, 2000));
        Assert.AreEqual(ranges[0], TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Start, 200, 2000));
        Assert.AreEqual(ranges[2], TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.End, 200, 2000));
        Assert.AreEqual(ranges[0], TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Center, 800, 2000));
        Assert.AreEqual(default(TaskbarPrimaryRange), TaskbarFreeRangeCalculator.Select([], TaskbarBarPosition.Center, 200, 2000));
    }

    private static TaskbarBarPlacement Place(int width, TaskbarPrimaryRange range, TaskbarBarPosition position) =>
        TaskbarBarPlacementCalculator.Calculate(2000, width, 48, 40, range, position, 0, 0, 1, 20);
}
