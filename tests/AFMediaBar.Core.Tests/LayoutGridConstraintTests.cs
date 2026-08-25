using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

/// <summary>
/// 指南第 10 节自动化测试矩阵：最小尺寸、连接、重叠、删除、折叠依附、包含、缩小、四边缩放、HoverSwitch 与尺寸。
/// </summary>
[TestClass]
public sealed class LayoutGridConstraintTests
{
    // ---------- 最小尺寸 ----------

    [TestMethod]
    public void UnitContainer_IsValid()
    {
        var profile = Profile(Container("a", LayoutGridRect.Unit(0, 0)));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(profile));
    }

    [TestMethod]
    public void ZeroSizeContainer_CannotBePlaced()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        Assert.IsFalse(LayoutGridConstraintService.CanPlaceContainer(
            profile,
            new LayoutGridRect(4, 0, 0, 1)));
    }

    [TestMethod]
    public void ZeroSizeContainer_IsRejectedBySetGridBounds()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TrySetGridBounds(
            profile,
            "a",
            new LayoutGridRect(0, 0, 0, 2));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.OutOfGrid, result.Failure);
    }

    [TestMethod]
    public void NegativeSizeContainer_IsRejectedBySetGridBounds()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TrySetGridBounds(
            profile,
            "a",
            new LayoutGridRect(2, 0, -2, 2));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.OutOfGrid, result.Failure);
    }

    // ---------- 连接 ----------

    [TestMethod]
    public void EdgeAdjacentContainers_AreConnected()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(3, 0, 3, 2)));
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(profile));
    }

    [TestMethod]
    public void CornerContact_IsDisconnected()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(3, 2, 3, 2)));
        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "b" &&
            error.Failure == LayoutGridFailure.DisconnectedContainerGraph));
    }

    [TestMethod]
    public void Move_BreakingConnectivity_IsRejected()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(3, 0, 3, 2)));
        var result = LayoutGridConstraintService.TryMove(profile, "b", 0, 2);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.DisconnectedContainerGraph, result.Failure);
    }

    [TestMethod]
    public void Move_KeepingConnectivity_IsAccepted()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(3, 0, 3, 2)),
            Container("c", new LayoutGridRect(3, 2, 3, 2)));
        // c 移到 a 下方，与 a 共享水平边，整体仍是边连通图。
        var result = LayoutGridConstraintService.TryMove(profile, "c", -3, 0);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(LayoutGridFailure.None, result.Failure);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(result.Updated!));
    }

    [TestMethod]
    public void CanPlaceContainer_AppendedWithGap_IsRejected()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 24, 8)));
        // 与现有容器相隔一格的追加位置无法构成边连通图。
        Assert.IsFalse(LayoutGridConstraintService.CanPlaceContainer(
            profile,
            new LayoutGridRect(25, 0, 3, 3)));
    }

    [TestMethod]
    public void CanPlaceContainer_AppendedFlushToExisting_IsAccepted()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 24, 8)));
        Assert.IsTrue(LayoutGridConstraintService.CanPlaceContainer(
            profile,
            new LayoutGridRect(24, 0, 3, 3)));
    }

    // ---------- 重叠 ----------

    [TestMethod]
    public void OverlappingContainers_AreRejected()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(4, 0, 3, 2)));
        var result = LayoutGridConstraintService.TrySetGridBounds(
            profile,
            "b",
            new LayoutGridRect(1, 0, 3, 2));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.Overlap, result.Failure);
    }

    [TestMethod]
    public void AdjacentNonOverlappingContainers_AreAccepted()
    {
        var profile = Profile(
            Container("a", new LayoutGridRect(0, 0, 3, 2)),
            Container("b", new LayoutGridRect(4, 0, 3, 2)));
        var result = LayoutGridConstraintService.TrySetGridBounds(
            profile,
            "b",
            new LayoutGridRect(3, 0, 3, 2));
        Assert.IsTrue(result.Success);
        Assert.AreEqual(LayoutGridFailure.None, result.Failure);
    }

    // ---------- 删除与禁用 ----------

    [TestMethod]
    public void Remove_LastNonCollapseContainer_IsRejected()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TryRemove(profile, "a");
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.LastNonCollapseContainer, result.Failure);
    }

    [TestMethod]
    public void Disable_LastNonCollapseContainer_IsRejected()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TrySetEnabled(profile, "a", false);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.LastNonCollapseContainer, result.Failure);
    }

    [TestMethod]
    public void Remove_AnchoredCollapseAnchor_IsRejected()
    {
        var anchor = Container("anchor", new LayoutGridRect(0, 0, 3, 2));
        var other = Container("other", new LayoutGridRect(3, 0, 2, 2));
        var collapse = Collapse("c", new LayoutGridRect(5, 0, 2, 2), "anchor", LayoutEdge.Right);
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [anchor, other],
            [collapse]);

        var result = LayoutGridConstraintService.TryRemove(profile, "anchor");
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LayoutGridFailure.AnchorInUse, result.Failure);
    }

    // ---------- 折叠依附 ----------

    [TestMethod]
    public void Collapse_FourSides_AreValid()
    {
        foreach (var side in Enum.GetValues<LayoutEdge>())
        {
            var anchor = Container("anchor", new LayoutGridRect(2, 2, 3, 2));
            var (x, y) = side switch
            {
                LayoutEdge.Top => (2, 0),
                LayoutEdge.Bottom => (2, 4),
                LayoutEdge.Left => (0, 2),
                _ => (5, 2)
            };
            var collapse = Collapse("c", new LayoutGridRect(x, y, 2, 2), "anchor", side);
            var profile = new LayoutProfile(
                LayoutProfileKey.Horizontal,
                PlayerLayoutMode.Horizontal,
                LayoutSurfaceSettings.Default,
                LayoutGridSettings.Default,
                [anchor],
                [collapse]);
            Assert.IsTrue(
                LayoutGridConstraintService.IsProfileValid(profile),
                $"attachment side {side} must be valid");
        }
    }

    [TestMethod]
    public void Collapse_MissingAnchor_IsRejected()
    {
        var profile = Profile(Container("anchor", new LayoutGridRect(0, 0, 3, 2)));
        var collapse = Collapse("c", new LayoutGridRect(3, 0, 2, 2), "missing", LayoutEdge.Right);
        profile = profile with { CollapseContainers = [collapse] };

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "c" &&
            error.Failure == LayoutGridFailure.MissingAnchor));
    }

    [TestMethod]
    public void Collapse_TouchingSecondContainerOnSameSide_IsRejected()
    {
        var a = Container("a", new LayoutGridRect(0, 0, 3, 2));
        var b = Container("b", new LayoutGridRect(0, 2, 3, 2));
        // 折叠容器同时接触 a 与 b 的右侧边（跨越两个容器的纵向范围）。
        var collapse = Collapse("c", new LayoutGridRect(3, 0, 2, 4), "a", LayoutEdge.Right);
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [a, b],
            [collapse]);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "c" &&
            error.Failure == LayoutGridFailure.MultipleAttachmentSides));
    }

    [TestMethod]
    public void Collapse_TouchingSecondContainerOnOtherSide_IsRejected()
    {
        var a = Container("a", new LayoutGridRect(0, 0, 3, 2));
        var b = Container("b", new LayoutGridRect(3, 2, 2, 1));
        var collapse = Collapse("c", new LayoutGridRect(3, 0, 2, 2), "a", LayoutEdge.Right);
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [a, b],
            [collapse]);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "c" &&
            error.Failure == LayoutGridFailure.CollapseTouchesOtherContainer));
    }

    [TestMethod]
    public void Collapse_AnchoredToCollapseContainer_IsRejected()
    {
        var anchor = Container("anchor", new LayoutGridRect(0, 0, 3, 2));
        var first = Collapse("first", new LayoutGridRect(3, 0, 2, 2), "anchor", LayoutEdge.Right);
        var second = Collapse("second", new LayoutGridRect(5, 0, 2, 2), "first", LayoutEdge.Right);
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [anchor],
            [first, second]);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "second" &&
            error.Failure == LayoutGridFailure.MissingAnchor));
    }

    [TestMethod]
    public void Collapse_ResolvesSharedEdge()
    {
        var anchor = Container("anchor", new LayoutGridRect(0, 0, 4, 3));
        var collapse = Collapse("c", new LayoutGridRect(4, 1, 2, 1), "anchor", LayoutEdge.Right);
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default,
            LayoutGridSettings.Default,
            [anchor],
            [collapse]);

        var info = LayoutGridConstraintService.ResolveAttachment(collapse, profile);
        Assert.IsTrue(info.Valid);
        Assert.AreEqual("anchor", info.Anchor?.InstanceId);
        Assert.AreEqual(LayoutEdge.Right, info.Side);
        Assert.AreEqual(new LayoutGridRect(4, 1, 1, 1), info.SharedEdge);
    }

    // ---------- 组件包含 ----------

    [TestMethod]
    public void Widget_FlushToAllEdges_IsValid()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 3, 2));
        container = container with
        {
            PrimarySlot = new LayoutSlot("content", [Widget("w", new LayoutGridRect(0, 0, 3, 2))])
        };
        var profile = Profile(container);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(profile));
    }

    [TestMethod]
    public void Widget_OutsideContainer_IsRejected()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 3, 2));
        container = container with
        {
            PrimarySlot = new LayoutSlot("content", [Widget("w", new LayoutGridRect(0, 0, 4, 2))])
        };
        var profile = Profile(container);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "w" &&
            error.Failure == LayoutGridFailure.WidgetOutsideContainer));
    }

    [TestMethod]
    public void OverlappingWidgets_AreRejected()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 6, 4));
        container = container with
        {
            PrimarySlot = new LayoutSlot("content", [
                Widget("w1", new LayoutGridRect(0, 0, 3, 2)),
                Widget("w2", new LayoutGridRect(2, 1, 3, 2))
            ])
        };
        var profile = Profile(container);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.Failure == LayoutGridFailure.WidgetOverlap));
    }

    // ---------- 容器缩小 ----------

    [TestMethod]
    public void Resize_EmptyContainer_ShrinksToUnitAndNoFurther()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));

        var right = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Right, -2);
        Assert.IsTrue(right.Success);
        var bottom = LayoutGridConstraintService.TryResize(right.Updated!, "a", LayoutEdge.Bottom, -1);
        Assert.IsTrue(bottom.Success);
        var bounds = LayoutGridConstraintService.FindContainer(bottom.Updated!, "a")!.GridBounds;
        Assert.AreEqual(new LayoutGridRect(0, 0, 1, 1), bounds);

        var further = LayoutGridConstraintService.TryResize(bottom.Updated!, "a", LayoutEdge.Right, -1);
        Assert.IsFalse(further.Success);
        Assert.AreEqual(LayoutGridFailure.OutOfGrid, further.Failure);
    }

    [TestMethod]
    public void Resize_ContainerWithChildren_StopsAtUnionBoundary()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 4, 4));
        container = container with
        {
            PrimarySlot = new LayoutSlot("content", [Widget("w", new LayoutGridRect(0, 0, 2, 1))])
        };
        var profile = Profile(container);

        var shrink = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Right, -1);
        Assert.IsTrue(shrink.Success);
        Assert.AreEqual(3, shrink.Updated!.Containers[0].GridBounds!.Width);

        // 恰好贴住组件右边界仍然合法。
        var flush = LayoutGridConstraintService.TryResize(shrink.Updated, "a", LayoutEdge.Right, -1);
        Assert.IsTrue(flush.Success);
        Assert.AreEqual(2, flush.Updated!.Containers[0].GridBounds!.Width);

        // 再缩一格就会漏出组件，必须失败。
        var expose = LayoutGridConstraintService.TryResize(flush.Updated, "a", LayoutEdge.Right, -1);
        Assert.IsFalse(expose.Success);
        Assert.AreEqual(LayoutGridFailure.ContainerWouldExposeChild, expose.Failure);
    }

    [TestMethod]
    public void Resize_LeftShiftsChildrenToKeepScreenPosition()
    {
        var container = Container("a", new LayoutGridRect(2, 2, 5, 4));
        container = container with
        {
            PrimarySlot = new LayoutSlot("content", [Widget("w", new LayoutGridRect(1, 0, 2, 2))])
        };
        var profile = Profile(container);

        var result = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Left, 1);
        Assert.IsTrue(result.Success);
        var updated = result.Updated!.Containers[0];
        Assert.AreEqual(new LayoutGridRect(3, 2, 4, 4), updated.GridBounds);
        var widget = updated.PrimarySlot.Children.OfType<LayoutWidgetElement>().Single();
        Assert.AreEqual(0, widget.GridBounds!.X);
    }

    // ---------- 四边缩放 ----------

    [TestMethod]
    public void Resize_LeftEdge_UpdatesPositionAndWidth()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Left, 1);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(new LayoutGridRect(1, 0, 2, 2), result.Updated!.Containers[0].GridBounds);
    }

    [TestMethod]
    public void Resize_RightEdge_UpdatesWidthOnly()
    {
        var profile = Profile(Container("a", new LayoutGridRect(0, 0, 3, 2)));
        var result = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Right, -1);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(new LayoutGridRect(0, 0, 2, 2), result.Updated!.Containers[0].GridBounds);
    }

    [TestMethod]
    public void Drag_FromAnyDirection_NormalizesRect()
    {
        var rect = LayoutGridRect.FromDrag(5, 4, 2, 1);
        Assert.AreEqual(new LayoutGridRect(2, 1, 4, 4), rect);
    }

    [TestMethod]
    public void Drag_UnitClick_ProducesUnitRect()
    {
        var rect = LayoutGridRect.FromDrag(3, 3, 3, 3);
        Assert.AreEqual(LayoutGridRect.Unit(3, 3), rect);
    }

    // ---------- HoverSwitch ----------

    [TestMethod]
    public void HoverSwitch_TwoStates_AreEachValid()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 6, 4), LayoutContainerKind.HoverSwitch);
        container = container with
        {
            PrimarySlot = new LayoutSlot("leave", [Widget("leave", new LayoutGridRect(0, 0, 2, 1))]),
            SecondarySlot = new LayoutSlot("near", [Widget("near", new LayoutGridRect(0, 0, 3, 2))])
        };
        var profile = Profile(container);
        Assert.IsTrue(LayoutGridConstraintService.IsProfileValid(profile));
    }

    [TestMethod]
    public void HoverSwitch_ResizeMustContainBothStateUnions()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 4, 3), LayoutContainerKind.HoverSwitch);
        container = container with
        {
            PrimarySlot = new LayoutSlot("leave", [Widget("leave", new LayoutGridRect(0, 0, 3, 1))]),
            SecondarySlot = new LayoutSlot("near", [Widget("near", new LayoutGridRect(0, 0, 2, 2))])
        };
        var profile = Profile(container);

        var toUnion = LayoutGridConstraintService.TryResize(profile, "a", LayoutEdge.Right, -1);
        Assert.IsTrue(toUnion.Success);
        Assert.AreEqual(3, toUnion.Updated!.Containers[0].GridBounds!.Width);

        var belowUnion = LayoutGridConstraintService.TryResize(toUnion.Updated, "a", LayoutEdge.Right, -1);
        Assert.IsFalse(belowUnion.Success);
        Assert.AreEqual(LayoutGridFailure.ContainerWouldExposeChild, belowUnion.Failure);
    }

    [TestMethod]
    public void HoverSwitch_InteractiveWidgetInLeaveState_IsRejected()
    {
        var container = Container("a", new LayoutGridRect(0, 0, 6, 4), LayoutContainerKind.HoverSwitch);
        container = container with
        {
            PrimarySlot = new LayoutSlot("leave", [Widget("w", new LayoutGridRect(0, 0, 2, 2), BuiltInWidgetTypeIds.Command)])
        };
        var profile = Profile(container);

        var errors = LayoutGridConstraintService.ValidateProfile(profile);
        Assert.IsTrue(errors.Any(error =>
            error.InstanceId == "w" &&
            error.Failure == LayoutGridFailure.WidgetNotAllowed));
    }

    // ---------- 尺寸 ----------

    [TestMethod]
    public void GridRectToDip_MultipliesByCellSize()
    {
        var size = LayoutRuntimeService.GridRectToDip(new LayoutGridRect(0, 0, 3, 2), 8);
        Assert.AreEqual(24, size.WidthDip);
        Assert.AreEqual(16, size.HeightDip);
    }

    [TestMethod]
    public void DesiredSize_ExcludesLeadingEmptyCells()
    {
        var profile = Profile(Container("a", new LayoutGridRect(10, 5, 3, 2)));
        var size = LayoutRuntimeService.CalculateDesiredSize(profile);
        Assert.AreEqual(24, size.WidthDip);
        Assert.AreEqual(16, size.HeightDip);
    }

    // ---------- 辅助 ----------

    private static LayoutProfile Profile(params LayoutContainerElement[] containers) => new(
        LayoutProfileKey.Horizontal,
        PlayerLayoutMode.Horizontal,
        LayoutSurfaceSettings.Default,
        LayoutGridSettings.Default,
        containers,
        []);

    private static LayoutContainerElement Container(
        string id,
        LayoutGridRect bounds,
        LayoutContainerKind kind = LayoutContainerKind.Static) => new(
        id,
        true,
        LayoutGeometry.Auto,
        kind,
        LayoutFlowOrientation.Automatic,
        LayoutContentAlignment.Center,
        LayoutContentAlignment.Center,
        kind == LayoutContainerKind.HoverSwitch
            ? LayoutTriggerMode.PointerNear
            : LayoutTriggerMode.Always,
        0,
        kind == LayoutContainerKind.HoverSwitch
            ? LayoutAnimationSettings.Default
            : new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
        LayoutSlot.Empty(kind == LayoutContainerKind.HoverSwitch ? "leave" : "content"),
        LayoutSlot.Empty(kind == LayoutContainerKind.HoverSwitch ? "near" : "unused"),
        bounds);

    private static LayoutCollapseContainer Collapse(
        string id,
        LayoutGridRect bounds,
        string anchorId,
        LayoutEdge side) => new(
        id,
        true,
        bounds,
        new LayoutAttachment(anchorId, side),
        6,
        72,
        LayoutAnimationSettings.Default,
        LayoutSlot.Empty("expanded"));

    private static LayoutWidgetElement Widget(
        string id,
        LayoutGridRect bounds,
        string typeId = BuiltInWidgetTypeIds.MediaText) => new(
        id,
        true,
        LayoutGeometry.Auto,
        typeId,
        typeId == BuiltInWidgetTypeIds.Command
            ? new CommandWidgetSettings(MediaCommandKind.PlayPause, 24)
            : new MediaTextWidgetSettings(MediaTextKind.Title, false, 12, 1),
        null,
        null,
        null,
        bounds);
}