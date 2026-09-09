using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LayoutSizeCalculatorTests
{
    [TestMethod]
    public void SpacingScaleChangesOnlyThePrimaryGap()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 0, 1000, "base");

        Assert.AreEqual(302, request.Width, 0.01);
        Assert.AreEqual(44, request.Height, 0.01);
    }

    [TestMethod]
    public void ThicknessScaleScalesCanvasButPreservesGapContribution()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1.25, 0, 1000, "thick");

        Assert.AreEqual(375, request.Width, 0.01);
        Assert.AreEqual(55, request.Height, 0.01);
    }

    [TestMethod]
    public void MeasuredTextExpandsTheAutoSizedComponentAndHonorsMaximum()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 400, 350, "long");

        Assert.AreEqual(350, request.Width, 0.01);
    }

    [TestMethod]
    public void VerticalLayoutUsesHeightAsThePrimaryAxis()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Vertical);

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 0, 1000, "vertical");

        Assert.AreEqual(80, request.Width, 0.01);
        Assert.AreEqual(170, request.Height, 0.01);
    }

    [TestMethod]
    public void ContentFingerprintAndResetFlagArePreserved()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.DynamicIsland, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 120, 1000, "lyric-1", true);

        Assert.AreEqual("lyric-1", request.ContentFingerprint);
        Assert.IsTrue(request.IsResetToPreset);
    }

    [TestMethod]
    public void HiddenComponentsDoNotContributeSpacingOrContentWidth()
    {
        var layout = new LayoutSchema
        {
            Orientation = LayoutOrientation.Horizontal,
            Canvas = new CanvasConfig { Width = 200, Height = 40 },
            Components =
            [
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Artwork,
                    IsVisible = false,
                    SpacingAfter = 100,
                    Bounds = new ComponentBounds(0, 0, 40, 40)
                },
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Title,
                    AutoSizePrimary = true,
                    Bounds = new ComponentBounds(40, 0, 100, 40)
                }
            ]
        };

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 100, 500, "title");

        Assert.AreEqual(200, request.Width, 0.01);
    }

    [TestMethod]
    public void EmptyContentKeepsMinimumAutoSizedComponentWidth()
    {
        var layout = new LayoutSchema
        {
            Orientation = LayoutOrientation.Horizontal,
            Canvas = new CanvasConfig { Width = 100, Height = 40 },
            Components =
            [
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Title,
                    AutoSizePrimary = true,
                    Bounds = new ComponentBounds(0, 0, 20, 40)
                }
            ]
        };

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 0, 1000, "empty");

        Assert.AreEqual(128, request.Width, 0.01);
    }

    [TestMethod]
    public void TaskbarOccupiedRangesLeaveOnlySafeIntervals()
    {
        var ranges = TaskbarOccupiedAreaService.CalculateFreeRanges(
            primaryLength: 1000,
            occupied:
            [
                new TaskbarPrimaryRange(300, 500),
                new TaskbarPrimaryRange(700, 800)
            ],
            edgePaddingPixels: 20,
            gapPixels: 10);

        Assert.AreEqual(3, ranges.Count);
        Assert.AreEqual(20, ranges[0].Start);
        Assert.AreEqual(290, ranges[0].End);
        Assert.AreEqual(510, ranges[1].Start);
        Assert.AreEqual(690, ranges[1].End);
        Assert.AreEqual(810, ranges[2].Start);
        Assert.AreEqual(980, ranges[2].End);
    }
}
