// Verifies physical taskbar anchors and safe-range selection as content widths change.
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
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

    [TestMethod]
    public void RightArrangementKeepsArtworkAndButtonsStableAcrossWidths()
    {
        foreach (var width in new[] { 400.0, 450.0, 600.0 })
        {
            var layout = TaskbarRestLayoutPolicy.Arrange(TaskbarRestLayoutPolicy.DefaultOrder,
                component => component == TaskbarRestComponent.Artwork ? 36 : 40,
                _ => true, 3, width, 8, 6, fromRight: true);
            Assert.AreEqual(width - 3, layout.Find(TaskbarRestComponent.Artwork)!.Value.Right);
            var actions = TaskbarArrangementPolicy.ActionsLeft(width, layout.TextLeft, layout.TextWidth,
                100, TaskbarArrangement.Right);
            Assert.AreEqual(width - 3 - 36 - 8 - 6,
                layout.TextLeft + actions + 100);
            var placements = layout.Placements;
            for (var i = 1; i < placements.Count; i++)
                Assert.IsTrue(placements[i - 1].Right <= placements[i].Left);
        }
    }

    [TestMethod]
    public void NarrowSafeRangeKeepsRightArtworkInsideTheWindow()
    {
        var layout = TaskbarRestLayoutPolicy.Arrange(TaskbarRestLayoutPolicy.DefaultOrder,
            component => component == TaskbarRestComponent.Artwork ? 36 : 40,
            _ => true, 3, 100, 8, 6, fromRight: true);
        Assert.AreEqual(97.0, layout.Find(TaskbarRestComponent.Artwork)!.Value.Right);
        Assert.AreEqual(0.0, layout.TextWidth);
    }

    [TestMethod]
    public void CenterButtonsUseBarCenterAndRevealClipsPreserveTheirAnchor()
    {
        foreach (var width in new[] { 400.0, 600.0 })
        {
            var left = TaskbarArrangementPolicy.ActionsLeft(width, 40, width - 100, 100, TaskbarArrangement.Center);
            Assert.AreEqual(width / 2, 40 + left + 50);
        }
        Assert.AreEqual(250.0, TaskbarArrangementPolicy.RevealLeft(300, 50, TaskbarArrangement.Right));
        Assert.AreEqual(125.0, TaskbarArrangementPolicy.RevealLeft(300, 50, TaskbarArrangement.Center));
        Assert.AreEqual(0.0, TaskbarArrangementPolicy.RevealLeft(300, 50, TaskbarArrangement.Left));
        Assert.AreEqual(5.0, TaskbarArrangementPolicy.ActionsLeft(200, 0, 40, 100, TaskbarArrangement.Right));
        Assert.IsTrue(TaskbarRestLayoutPolicy.Arrange([], _ => 40, _ => true, 3, 100, 8, 6, true).IsEmpty);
    }

    [TestMethod]
    public void ArrangementRoundTripsAndDisplayResetHonorsUserDefaults()
    {
        var old = SettingsManager.Current.Clone();
        var defaults = SettingsManager.UserDefaults;
        try
        {
            var settings = new AppSettings { Position = TaskbarBarPosition.Center };
            settings.TaskbarExperience = settings.TaskbarExperience with { Arrangement = TaskbarArrangement.Right };
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
            Assert.AreEqual(TaskbarArrangement.Right, restored.Clone().TaskbarExperience.Normalize().Arrangement);
            Assert.AreEqual(TaskbarArrangement.Automatic,
                new TaskbarExperienceSettings { Arrangement = (TaskbarArrangement)99 }.Normalize().Arrangement);
            SettingsManager.SetUserDefaults(settings);
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
            { Arrangement = TaskbarArrangement.Left };
            SettingsManager.ResetDisplayModes();
            Assert.AreEqual(TaskbarArrangement.Right, SettingsManager.Current.TaskbarExperience.Arrangement);
            Assert.AreEqual(TaskbarBarPosition.Center, SettingsManager.Current.Position);
        }
        finally
        {
            SettingsManager.SetUserDefaults(defaults);
            SettingsManager.Replace(old);
        }
    }

    [TestMethod]
    public void DragOffsetsRoundTripForAllAnchorsIncludingSafeRangeClamps()
    {
        foreach (var position in Enum.GetValues<TaskbarBarPosition>())
            foreach (var desired in new[] { -50, 450, 1200, 1950 })
            {
                var padding = TaskbarBarPlacementCalculator.ResolveManualPadding(desired, 100, 1800, 300, 2000, position);
                var result = TaskbarBarPlacementCalculator.Calculate(
                    2000, 300, 48, 40, new TaskbarPrimaryRange(100, 1800), position, padding, 0, 1, 20);
                Assert.AreEqual(Math.Clamp(desired, 100, 1500), result.Primary);
            }
        Assert.AreEqual(350, TaskbarBarPlacementCalculator.ResolveManualPadding(450, 100, 1800, 300));
    }

    private static TaskbarBarPlacement Place(int width, TaskbarPrimaryRange range, TaskbarBarPosition position) =>
        TaskbarBarPlacementCalculator.Calculate(2000, width, 48, 40, range, position, 0, 0, 1, 20);
}
