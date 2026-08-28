using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutSkinAssignmentTests
{
    [TestMethod]
    public void UpdatesWidgetInsideNestedContainerWithoutMutatingSource()
    {
        var widget = Widget("nested-widget");
        var nested = Container("nested", [widget]);
        var profile = Profile(Container("root", [nested]));
        var settings = new Dictionary<string, string> { ["accent"] = "red" };

        var changed = LayoutSkinAssignmentService.TryUpdateWidgetSkin(
            profile, widget.InstanceId, "example", 2, settings, out var updated);

        Assert.IsTrue(changed);
        Assert.IsNull(widget.SkinId);
        var resultContainer = Assert.IsInstanceOfType<LayoutContainerElement>(
            updated.Containers[0].PrimarySlot.Children[0]);
        var result = Assert.IsInstanceOfType<LayoutWidgetElement>(
            resultContainer.PrimarySlot.Children[0]);
        Assert.AreEqual("example", result.SkinId);
        Assert.AreEqual(2, result.SkinVersion);
        Assert.AreEqual(settings["accent"], result.SkinSettings!["accent"]);
    }

    [TestMethod]
    public void UpdatesWidgetInsideCollapseSlot()
    {
        var widget = Widget("collapse-widget");
        var anchor = Container("anchor", []);
        var collapse = new LayoutCollapseContainer(
            "collapse",
            true,
            new LayoutGridRect(0, 0, 4, 2),
            new LayoutAttachment(anchor.InstanceId, LayoutEdge.Top),
            4,
            32,
            LayoutAnimationSettings.Default,
            new LayoutSlot("expanded", [widget]));
        var profile = Profile(anchor, [collapse]);

        Assert.IsTrue(LayoutSkinAssignmentService.TryUpdateWidgetSkin(
            profile, widget.InstanceId, null, null, null, out var updated));
        Assert.IsNull(((LayoutWidgetElement)updated.CollapseContainers[0].ExpandedSlot.Children[0]).SkinId);
    }

    [TestMethod]
    public void ReturnsFalseWhenWidgetDoesNotExist()
    {
        var profile = Profile(Container("root", []));

        Assert.IsFalse(LayoutSkinAssignmentService.TryUpdateWidgetSkin(
            profile, "missing", "example", 1, null, out var updated));
        Assert.AreEqual(profile, updated);
    }

    private static LayoutWidgetElement Widget(string id) => new(
        id, true, LayoutGeometry.Auto, BuiltInWidgetTypeIds.Command,
        new CommandWidgetSettings(MediaCommandKind.PlayPause, 24));

    private static LayoutContainerElement Container(string id, IReadOnlyList<LayoutElement> children) =>
        LayoutGridConstraintService.CreateContainer(LayoutContainerKind.Static) with
        {
            InstanceId = id,
            PrimarySlot = new LayoutSlot($"{id}-primary", children),
            GridBounds = new LayoutGridRect(0, 0, 12, 6)
        };

    private static LayoutProfile Profile(LayoutContainerElement container, IReadOnlyList<LayoutCollapseContainer>? collapses = null) =>
        new(LayoutProfileKey.Horizontal, PlayerLayoutMode.Horizontal, LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default, [container], collapses ?? []);
}
