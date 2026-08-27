using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutPlacementPreviewServiceTests
{
    [TestMethod]
    public void WidgetCandidateUsesDefaultMeasurementAndStaysInsideContainer()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var container = profile.Containers.First(item => item.GridBounds is not null);
        var bounds = new LayoutGridRect(0, 0, 6, 6);
        profile = profile with
        {
            Containers =
            [
                container with
                {
                    GridBounds = bounds,
                    PrimarySlot = LayoutSlot.Empty(container.PrimarySlot.SlotId)
                }
            ],
            CollapseContainers = []
        };
        var tool = LayoutPlacementTool.Widget(
            BuiltInWidgetTypeIds.Command,
            string.Empty,
            LayoutSlotKind.Primary);

        var preview = LayoutPlacementPreviewService.Calculate(
            profile,
            tool,
            bounds.X + 1,
            bounds.Y + 1,
            bounds.X + 1,
            bounds.Y + 1,
            widgetSettings: null);

        Assert.AreEqual(3, preview.Bounds.Width);
        Assert.AreEqual(3, preview.Bounds.Height);
        Assert.IsTrue(preview.IsValid);
        Assert.IsLessThanOrEqualTo(bounds.Right, preview.Bounds.Right);
        Assert.IsLessThanOrEqualTo(bounds.Bottom, preview.Bounds.Bottom);
    }

    [TestMethod]
    public void WidgetCandidateOutsideContainerIsInvalid()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal with
        {
            Containers = [],
            CollapseContainers = []
        };
        var tool = LayoutPlacementTool.Widget(
            BuiltInWidgetTypeIds.Command,
            string.Empty,
            LayoutSlotKind.Primary);

        var preview = LayoutPlacementPreviewService.Calculate(
            profile,
            tool,
            0,
            0,
            0,
            0,
            widgetSettings: null);

        Assert.IsFalse(preview.IsValid);
    }

    [TestMethod]
    public void ContainerCandidateReportsGridBoundaryValidity()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal with
        {
            Containers = [],
            CollapseContainers = []
        };
        var tool = LayoutPlacementTool.Container(LayoutContainerKind.Static);

        var valid = LayoutPlacementPreviewService.Calculate(
            profile,
            tool,
            0,
            0,
            0,
            0,
            widgetSettings: null);
        var occupied = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var existing = occupied.Containers.First(item => item.GridBounds is not null);
        occupied = occupied with
        {
            Containers = [existing],
            CollapseContainers = []
        };
        var invalid = LayoutPlacementPreviewService.Calculate(
            occupied,
            tool,
            existing.GridBounds!.X,
            existing.GridBounds.Y,
            existing.GridBounds.X,
            existing.GridBounds.Y,
            widgetSettings: null);

        Assert.IsTrue(valid.IsValid);
        Assert.IsFalse(invalid.IsValid);
    }
}
