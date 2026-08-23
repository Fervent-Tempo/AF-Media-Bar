using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutEditingTests
{
    [TestMethod]
    public void AddInlineContainer_AppendsWithoutMutatingSource()
    {
        var source = CreateProfile();
        var originalCount = source.InlineContainers.Count;

        var changed = LayoutEditorService.TryAddInlineContainer(
            source,
            LayoutContainerKind.HoverSwitch,
            out var updated,
            out var failure);

        Assert.IsTrue(changed);
        Assert.AreEqual(LayoutEditFailure.None, failure);
        Assert.HasCount(originalCount + 1, updated.InlineContainers);
        Assert.HasCount(originalCount, source.InlineContainers);
        Assert.AreEqual(LayoutContainerKind.HoverSwitch, updated.InlineContainers[^1].ContainerKind);
    }

    [TestMethod]
    public void AddEdgeContainer_RejectsUnavailableTaskbarEdge()
    {
        var source = CreateProfile();

        var changed = LayoutEditorService.TryAddEdgeContainer(
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
    public void AddWidget_RejectsDuplicateInstanceId()
    {
        var source = CreateProfile();
        var container = source.InlineContainers[0];
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

    private static LayoutProfile CreateProfile() =>
        LayoutMigrationService.CreateFromLegacy(WindowSettings.Default, MetricSettings.Default)
            .Horizontal;
}
