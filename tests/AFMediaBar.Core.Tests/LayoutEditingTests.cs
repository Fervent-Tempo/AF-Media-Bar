using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditingTests
{
    [TestMethod]
    public void AddContainer_AppendsWithoutMutatingSource()
    {
        var source = CreateProfile();
        var originalCount = source.Containers.Count;

        var result = LayoutPlacementService.TryAddContainer(source, LayoutContainerKind.HoverSwitch);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        var updated = result.Updated!;
        Assert.HasCount(originalCount + 1, updated.Containers);
        Assert.HasCount(originalCount, source.Containers);
        Assert.AreEqual(LayoutContainerKind.HoverSwitch, updated.Containers[^1].ContainerKind);
        Assert.IsNotNull(updated.Containers[^1].GridBounds);
    }

    [TestMethod]
    public void AddCollapse_RejectsUnavailableTaskbarEdge()
    {
        var source = CreateProfile();

        var result = LayoutPlacementService.TryAddCollapse(source, LayoutEdge.Bottom, LayoutEdge.Bottom);

        Assert.IsFalse(result.Success);
        Assert.AreSame(null, result.Updated);
        Assert.AreEqual(LayoutGridFailure.InvalidAttachmentSide, result.Failure);
    }

    [TestMethod]
    public void AddCollapse_AttachesToFirstEnabledContainer()
    {
        var source = CreateProfile();

        var result = LayoutPlacementService.TryAddCollapse(source, LayoutEdge.Right, null);

        Assert.IsTrue(result.Success);
        var updated = result.Updated!;
        Assert.HasCount(1, updated.CollapseContainers);
        Assert.AreEqual(
            source.Containers[0].InstanceId,
            updated.CollapseContainers[0].Attachment.AnchorContainerId);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(updated));
    }

    [TestMethod]
    public void LayoutPlacementServiceAddsConnectedContainer()
    {
        var source = CreateProfile();
        var result = LayoutPlacementService.TryAddContainer(source, LayoutContainerKind.Static);

        Assert.IsTrue(result.Success);
        Assert.HasCount(source.Containers.Count + 1, result.Updated!.Containers);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(result.Updated));
    }

    [TestMethod]
    public void AddWidget_RejectsDuplicateInstanceId()
    {
        var source = CreateProfile();
        var container = source.Containers[0];
        var widget = new LayoutWidgetElement(
            "separator-test",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Separator,
            new SeparatorWidgetSettings(1, 22));
        var first = LayoutGridConstraintService.TryAddWidget(
            source, container.InstanceId, LayoutSlotKind.Primary,
            widget with { GridBounds = new LayoutGridRect(0, 0, 3, 3) });
        Assert.IsTrue(first.Success);
        var once = first.Updated!;

        var twice = LayoutGridConstraintService.TryAddWidget(
            once, container.InstanceId, LayoutSlotKind.Primary,
            widget with { GridBounds = new LayoutGridRect(4, 0, 3, 3) });

        Assert.IsFalse(twice.Success);
        Assert.AreEqual(LayoutGridFailure.DuplicateInstanceId, twice.Failure);
    }

    [TestMethod]
    public void LayoutConstraintServiceAddsCallerProvidedWidgetSettings()
    {
        var profile = CreateProfile();
        var container = profile.Containers[0];
        var widget = new LayoutWidgetElement(
            "custom-command",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.Next, 48),
            null,
            null,
            null,
            new LayoutGridRect(0, 0, 3, 3));

        var result = LayoutGridConstraintService.TryAddWidget(
            profile,
            container.InstanceId,
            LayoutSlotKind.Primary,
            widget);

        Assert.IsTrue(result.Success);
        var added = result.Updated!.Containers[0].PrimarySlot.Children
            .OfType<LayoutWidgetElement>()
            .Single(item => item.InstanceId == widget.InstanceId);
        Assert.AreEqual(MediaCommandKind.Next, ((CommandWidgetSettings)added.Settings).Command);
    }

    [TestMethod]
    public void AddWidget_ToCollapseExpandedSlot_IsAllowed()
    {
        var source = CreateProfile();
        var collapse = new LayoutCollapseContainer(
            "collapse-1",
            true,
            new LayoutGridRect(24, 0, 3, 3),
            new LayoutAttachment(source.Containers[0].InstanceId, LayoutEdge.Right),
            6,
            72,
            LayoutAnimationSettings.Default,
            LayoutSlot.Empty("expanded"));
        source = source with { CollapseContainers = [collapse] };

        var widget = new LayoutWidgetElement(
            "command-1",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 24));

        var result = LayoutGridConstraintService.TryAddWidget(
            source,
            collapse.InstanceId,
            LayoutSlotKind.Expanded,
            widget with { GridBounds = new LayoutGridRect(0, 0, 3, 3) });

        Assert.IsTrue(result.Success);
        var updated = result.Updated!;
        Assert.IsNotNull(updated.CollapseContainers[0].ExpandedSlot.Children.Single().GridBounds);
    }

    [TestMethod]
    public void HistoryService_RecordsAndReturnsLatestSnapshot()
    {
        var source = CreateProfile();
        var history = new LayoutEditHistoryService();
        history.Record(source);

        Assert.IsTrue(history.CanUndo(source.Key));
        Assert.IsTrue(history.TryUndo(source.Key, out var restored));
        Assert.AreSame(source, restored);
        Assert.IsFalse(history.CanUndo(source.Key));
    }

    [TestMethod]
    public void HistoryService_OneUndoPerRecordedSnapshot()
    {
        var source = CreateProfile();
        var history = new LayoutEditHistoryService();
        history.Record(source);

        Assert.IsTrue(history.TryUndo(source.Key, out var first));
        Assert.AreSame(source, first);
        Assert.IsFalse(history.TryUndo(source.Key, out _));
    }

    [TestMethod]
    public void RuntimeService_DerivesPositiveSizeAndComponentCapabilities()
    {
        var source = CreateProfile();

        var size = LayoutRuntimeService.CalculateDesiredSize(source);
        var settings = LayoutRuntimeService.ResolveComponentSettings(
            source,
            MetricSettings.Default);

        Assert.IsGreaterThan(0, size.WidthDip);
        Assert.IsGreaterThan(0, size.HeightDip);
        Assert.AreEqual(
            LayoutRuntimeService.ContainsWidget(source, BuiltInWidgetTypeIds.Spectrum),
            settings.AudioMonitorEnabled);
    }

    [TestMethod]
    public void WidgetRequiredCells_ReflectIntrinsicRuntimeSize()
    {
        var profile = CreateProfile();
        var metrics = new LayoutWidgetElement(
            "metrics-size",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Metrics,
            new MetricsWidgetSettings(
                MetricKind.SystemMemory,
                false,
                2500,
                [MetricKind.SystemMemory]));
        var command = new LayoutWidgetElement(
            "command-size",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(MediaCommandKind.SelectOutputDevice, 36));
        var combined = new LayoutWidgetElement(
            "combined-size",
            true,
            LayoutGeometry.Auto,
            BuiltInWidgetTypeIds.MediaText,
            new MediaTextWidgetSettings(MediaTextKind.TitleAndArtist, false, 14, 1));

        Assert.AreEqual((10, 3), WidgetMeasurementService.MeasureRequiredCells(profile, metrics));
        Assert.AreEqual((5, 5), WidgetMeasurementService.MeasureRequiredCells(profile, command));
        Assert.AreEqual((19, 5), WidgetMeasurementService.MeasureRequiredCells(profile, combined));
    }

    private static LayoutProfile CreateProfile()
    {
        var container = LayoutGridConstraintService.CreateContainer(LayoutContainerKind.Static) with
        {
            GridBounds = new LayoutGridRect(0, 0, 24, 8)
        };
        return new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [container],
            []);
    }
}
