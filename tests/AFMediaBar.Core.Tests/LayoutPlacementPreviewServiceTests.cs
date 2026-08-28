using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Components.Abstractions;
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
    public void WidgetCandidateUsesTheInjectedSchemaMapper()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var container = profile.Containers.First(item => item.GridBounds is not null) with
        {
            GridBounds = new LayoutGridRect(0, 0, 8, 8),
            PrimarySlot = LayoutSlot.Empty("preview-primary")
        };
        profile = profile with { Containers = [container], CollapseContainers = [] };
        var mapper = new TrackingSettingsMapper(new Schema5ComponentSettingsMapper());
        var tool = LayoutPlacementTool.Widget(
            BuiltInWidgetTypeIds.Command,
            string.Empty,
            LayoutSlotKind.Primary);

        var preview = LayoutPlacementPreviewService.Calculate(
            profile,
            tool,
            1,
            1,
            1,
            1,
            widgetSettings: null,
            settingsMapper: mapper);

        Assert.IsTrue(preview.IsValid);
        Assert.AreEqual(1, mapper.DefaultCalls);
        Assert.AreEqual(1, mapper.MeasureCalls);
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

    private sealed class TrackingSettingsMapper(IComponentSettingsMapper inner) : IComponentSettingsMapper
    {
        public int DefaultCalls { get; private set; }
        public int MeasureCalls { get; private set; }

        public bool TryCreateDefaultSettings(string typeId, out WidgetSettings settings)
        {
            DefaultCalls++;
            return inner.TryCreateDefaultSettings(typeId, out settings);
        }

        public bool TryMapSettings(LayoutWidgetElement widget, out IComponentSettings componentSettings) =>
            inner.TryMapSettings(widget, out componentSettings);

        public bool TryMapToSchema5(IComponentSettings componentSettings, out string typeId, out WidgetSettings settings) =>
            inner.TryMapToSchema5(componentSettings, out typeId, out settings);

        public bool TryMeasure(LayoutProfile profile, LayoutWidgetElement widget, out (int Width, int Height) measurement)
        {
            MeasureCalls++;
            return inner.TryMeasure(profile, widget, out measurement);
        }
    }
}
