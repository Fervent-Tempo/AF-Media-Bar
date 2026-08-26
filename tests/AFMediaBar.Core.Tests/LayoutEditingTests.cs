using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditingTests
{
    [TestMethod]
    public void AddContainer_AppendsWithoutMutatingSource()
    {
        var source = CreateProfile();
        var originalCount = source.Containers.Count;

        var changed = LayoutEditorService.TryAddContainer(
            source,
            LayoutContainerKind.HoverSwitch,
            out var updated,
            out var failure);

        Assert.IsTrue(changed, failure.ToString());
        Assert.AreEqual(LayoutEditFailure.None, failure);
        Assert.HasCount(originalCount + 1, updated.Containers);
        Assert.HasCount(originalCount, source.Containers);
        Assert.AreEqual(LayoutContainerKind.HoverSwitch, updated.Containers[^1].ContainerKind);
        Assert.IsNotNull(updated.Containers[^1].GridBounds);
    }

    [TestMethod]
    public void AddCollapse_RejectsUnavailableTaskbarEdge()
    {
        var source = CreateProfile();

        var changed = LayoutEditorService.TryAddCollapse(
            source,
            LayoutEdge.Bottom,
            LayoutEdge.Bottom,
            out var updated,
            out var failure);

        Assert.IsFalse(changed);
        Assert.AreSame(source, updated);
        Assert.AreEqual(LayoutEditFailure.EdgeUnavailable, failure);
    }

    [TestMethod]
    public void AddCollapse_AttachesToFirstEnabledContainer()
    {
        var source = CreateProfile();

        var changed = LayoutEditorService.TryAddCollapse(
            source,
            LayoutEdge.Right,
            null,
            out var updated,
            out var failure);

        Assert.IsTrue(changed);
        Assert.AreEqual(LayoutEditFailure.None, failure);
        Assert.HasCount(1, updated.CollapseContainers);
        Assert.AreEqual(
            source.Containers[0].InstanceId,
            updated.CollapseContainers[0].Attachment.AnchorContainerId);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(updated));
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
        Assert.IsTrue(LayoutEditorService.TryAddWidget(
            source,
            container.InstanceId,
            LayoutSlotKind.Primary,
            widget,
            out var once,
            out _));

        var changed = LayoutEditorService.TryAddWidget(
            once,
            container.InstanceId,
            LayoutSlotKind.Primary,
            widget,
            out var twice,
            out var failure);

        Assert.IsFalse(changed);
        Assert.AreSame(once, twice);
        Assert.AreEqual(LayoutEditFailure.DuplicateInstanceId, failure);
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

        var changed = LayoutEditorService.TryAddWidget(
            source,
            collapse.InstanceId,
            LayoutSlotKind.Expanded,
            widget,
            out var updated,
            out var failure);

        Assert.IsTrue(changed);
        Assert.AreEqual(LayoutEditFailure.None, failure);
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

        Assert.AreEqual((10, 3), LayoutEditorService.ResolveWidgetRequiredCells(profile, metrics));
        Assert.AreEqual((5, 5), LayoutEditorService.ResolveWidgetRequiredCells(profile, command));
        Assert.AreEqual((19, 5), LayoutEditorService.ResolveWidgetRequiredCells(profile, combined));
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
