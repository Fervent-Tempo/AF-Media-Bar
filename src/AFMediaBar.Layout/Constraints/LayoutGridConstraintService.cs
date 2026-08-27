using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Services;

/// <summary>
/// 结构化编辑失败原因；指南第 5 节要求的全部原因都在其中，另补充了若干实现必需的原因。
/// Structured edit failures; all reasons required by guide section 5 are present, plus a few implementation-required ones.
/// </summary>
public enum LayoutGridFailure
{
    None = 0,
    ContainerNotFound = 1,
    DuplicateInstanceId = 2,
    OutOfGrid = 3,
    Overlap = 4,
    WidgetOutsideContainer = 5,
    WidgetOverlap = 6,
    DisconnectedContainerGraph = 7,
    LastNonCollapseContainer = 8,
    MissingAnchor = 9,
    InvalidAttachmentSide = 10,
    MultipleAttachmentSides = 11,
    CollapseTouchesOtherContainer = 12,
    ContainerWouldExposeChild = 13,
    WidgetNotAllowed = 14,
    MissingGridBounds = 15,
    AnchorInUse = 16,
    NotSupported = 17
}

/// <summary>
/// 一次编辑尝试的不可变结果：成功时携带更新后的档案，失败时携带原因。
/// Immutable result of an edit attempt: the updated profile on success, or a failure reason.
/// </summary>
public sealed record LayoutGridEditResult(LayoutProfile? Updated, LayoutGridFailure Failure)
{
    public bool Success => Failure == LayoutGridFailure.None && Updated is not null;

    public static LayoutGridEditResult Ok(LayoutProfile updated) => new(updated, LayoutGridFailure.None);

    public static LayoutGridEditResult Fail(LayoutGridFailure failure) => new(null, failure);
}

/// <summary>
/// 全图验证报告中的一条错误；InstanceId 定位对象，Failure 说明原因。
/// One error in a full-profile validation report; InstanceId locates the object.
/// </summary>
public sealed record LayoutValidationError(string InstanceId, LayoutGridFailure Failure);

/// <summary>
/// 放置工具：选择组件目录工具后拖拽创建容器，或选择组件工具后在目标容器槽位内拖拽创建组件。
/// A placement tool: either a container kind dragged onto the canvas, or a widget type dragged into a target container slot.
/// </summary>
public sealed record LayoutPlacementTool(
    LayoutContainerKind? ContainerKind,
    string? WidgetTypeId,
    string? OwnerContainerId,
    LayoutSlotKind SlotKind)
{
    public static LayoutPlacementTool Container(LayoutContainerKind kind) =>
        new(kind, null, null, LayoutSlotKind.Primary);

    public static LayoutPlacementTool Widget(string typeId, string ownerContainerId, LayoutSlotKind slotKind) =>
        new(null, typeId, ownerContainerId, slotKind);

    public bool IsContainer => ContainerKind.HasValue;
    public bool IsWidget => WidgetTypeId is not null;
}

/// <summary>
/// 折叠容器依附解析结果：锚点、接边（锚点被依附的边）和公共边段。
/// Attachment resolution: the anchor, the attached anchor side, and the shared edge segment.
/// </summary>
public sealed record LayoutAttachmentInfo(
    LayoutContainerElement? Anchor,
    LayoutEdge Side,
    LayoutGridRect SharedEdge,
    bool Valid,
    LayoutGridFailure Failure);

/// <summary>
/// 集中的、UI 无关的网格布局约束服务：放置、移动、四边缩放、删除、启用的全图验证与结构化失败。
/// Central UI-agnostic grid layout constraint service: placement, movement, four-edge resize, removal, enable/disable with full-graph validation.
/// </summary>
public static class LayoutGridConstraintService
{
    // ---------- 创建工厂 ----------

    public static LayoutContainerElement CreateContainer(LayoutContainerKind kind)
    {
        var normalizedKind = kind == LayoutContainerKind.HoverSwitch
            ? LayoutContainerKind.HoverSwitch
            : LayoutContainerKind.Static;
        return new LayoutContainerElement(
            $"container-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            normalizedKind,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            normalizedKind == LayoutContainerKind.HoverSwitch
                ? LayoutTriggerMode.PointerNear
                : LayoutTriggerMode.Always,
            48,
            normalizedKind == LayoutContainerKind.HoverSwitch
                ? LayoutAnimationSettings.Default
                : new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            LayoutSlot.Empty(normalizedKind == LayoutContainerKind.HoverSwitch ? "leave" : "content"),
            LayoutSlot.Empty(normalizedKind == LayoutContainerKind.HoverSwitch ? "near" : "unused"));
    }

    public static LayoutWidgetElement CreateWidget(string typeId)
    {
        var settings = LayoutComponentCatalog.CreateDefaultSettings(typeId);
        return new LayoutWidgetElement(
            $"widget-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            typeId,
            settings);
    }

    public static LayoutCollapseContainer CreateCollapseContainer(
        LayoutContainerElement anchor,
        LayoutEdge attachmentSide,
        LayoutGridRect rect)
    {
        return new LayoutCollapseContainer(
            $"collapse-{Guid.NewGuid():N}",
            true,
            rect,
            new LayoutAttachment(anchor.InstanceId, attachmentSide),
            6,
            72,
            LayoutAnimationSettings.Default,
            LayoutSlot.Empty("expanded"));
    }

    // ---------- 对象查找 ----------

    public static LayoutContainerElement? FindContainer(LayoutProfile profile, string instanceId) =>
        profile.Containers.FirstOrDefault(container =>
            string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal));

    public static LayoutCollapseContainer? FindCollapse(LayoutProfile profile, string instanceId) =>
        profile.CollapseContainers.FirstOrDefault(container =>
            string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal));

    public static object? FindAny(LayoutProfile profile, string instanceId)
    {
        foreach (var container in profile.Containers)
        {
            if (string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return container;
            }

            foreach (var slot in Slots(container))
            {
                var widget = slot.Children.OfType<LayoutWidgetElement>()
                    .FirstOrDefault(item => string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
                if (widget is not null)
                {
                    return widget;
                }
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (string.Equals(collapse.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return collapse;
            }

            var widget = collapse.ExpandedSlot.Children.OfType<LayoutWidgetElement>()
                .FirstOrDefault(item => string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
            if (widget is not null)
            {
                return widget;
            }
        }

        return null;
    }

    private static IEnumerable<LayoutSlot> Slots(LayoutContainerElement container)
    {
        yield return container.PrimarySlot;
        yield return container.SecondarySlot;
    }

    // ---------- 全图验证 ----------

    /// <summary>
    /// 验证整个档案并返回全部错误；结构化失败原因与指南第 5 节一致。
    /// Validates the whole profile and returns every error with the structured reasons from guide section 5.
    /// </summary>
    public static IReadOnlyList<LayoutValidationError> ValidateProfile(LayoutProfile profile)
    {
        var errors = new List<LayoutValidationError>();
        var grid = profile.Grid ?? LayoutGridSettings.Default;

        ValidateUniqueIds(profile, errors);
        ValidateNonCollapseContainers(profile, grid, errors);
        ValidateCollapseContainers(profile, grid, errors);
        ValidateWidgets(profile, errors);
        ValidateConnectivity(profile, errors);
        return errors;
    }

    public static bool IsProfileValid(LayoutProfile profile) => ValidateProfile(profile).Count == 0;

    private static void ValidateUniqueIds(LayoutProfile profile, ICollection<LayoutValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in profile.Containers)
        {
            if (!seen.Add(container.InstanceId))
            {
                errors.Add(new LayoutValidationError(container.InstanceId, LayoutGridFailure.DuplicateInstanceId));
            }

            foreach (var slot in Slots(container))
            {
                foreach (var widget in slot.Children.OfType<LayoutWidgetElement>())
                {
                    if (!seen.Add(widget.InstanceId))
                    {
                        errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.DuplicateInstanceId));
                    }
                }
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (!seen.Add(collapse.InstanceId))
            {
                errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.DuplicateInstanceId));
            }

            foreach (var widget in collapse.ExpandedSlot.Children.OfType<LayoutWidgetElement>())
            {
                if (!seen.Add(widget.InstanceId))
                {
                    errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.DuplicateInstanceId));
                }
            }
        }
    }

    private static void ValidateNonCollapseContainers(
        LayoutProfile profile,
        LayoutGridSettings grid,
        ICollection<LayoutValidationError> errors)
    {
        var containers = profile.Containers.Where(container => container.GridBounds is not null).ToArray();
        for (var index = 0; index < profile.Containers.Count; index++)
        {
            var container = profile.Containers[index];
            var bounds = container.GridBounds;
            if (bounds is null)
            {
                errors.Add(new LayoutValidationError(container.InstanceId, LayoutGridFailure.MissingGridBounds));
                continue;
            }

            if (bounds.IsEmpty)
            {
                errors.Add(new LayoutValidationError(container.InstanceId, LayoutGridFailure.OutOfGrid));
                continue;
            }

            if (!IsInGrid(bounds, grid))
            {
                errors.Add(new LayoutValidationError(container.InstanceId, LayoutGridFailure.OutOfGrid));
            }

            for (var otherIndex = index + 1; otherIndex < profile.Containers.Count; otherIndex++)
            {
                var other = profile.Containers[otherIndex];
                if (other.GridBounds is { } otherBounds && bounds.Overlaps(otherBounds))
                {
                    errors.Add(new LayoutValidationError(other.InstanceId, LayoutGridFailure.Overlap));
                }
            }
        }
    }

    private static void ValidateCollapseContainers(
        LayoutProfile profile,
        LayoutGridSettings grid,
        ICollection<LayoutValidationError> errors)
    {
        foreach (var collapse in profile.CollapseContainers)
        {
            var bounds = collapse.GridBounds;
            if (bounds is null)
            {
                errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.MissingGridBounds));
                continue;
            }

            if (bounds.IsEmpty || !IsInGrid(bounds, grid))
            {
                errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.OutOfGrid));
            }

            if (!collapse.Enabled)
            {
                // 禁用的折叠容器不参与依附规则，避免重启后无法清理陈旧配置。
                // Disabled collapse containers skip attachment rules so stale configuration can be cleaned up.
                continue;
            }

            var anchor = FindContainer(profile, collapse.Attachment.AnchorContainerId);
            if (anchor is null || !anchor.Enabled || anchor.GridBounds is null)
            {
                errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.MissingAnchor));
                continue;
            }

            foreach (var other in AllContainers(profile))
            {
                if (string.Equals(other.InstanceId, collapse.InstanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (other.GridBounds is { } otherBounds && bounds.Overlaps(otherBounds))
                {
                    errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.Overlap));
                }
            }

            var connectionSide = ConnectionSide(collapse.Attachment);
            var anchorSide = ContactSide(bounds, anchor.GridBounds);
            if (anchorSide != connectionSide)
            {
                errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.InvalidAttachmentSide));
                continue;
            }

            foreach (var other in AllContainers(profile))
            {
                if (string.Equals(other.InstanceId, collapse.InstanceId, StringComparison.Ordinal) ||
                    other.GridBounds is null)
                {
                    continue;
                }

                var side = ContactSide(bounds, other.GridBounds);
                if (side is null)
                {
                    continue;
                }

                if (side == connectionSide)
                {
                    if (!string.Equals(other.InstanceId, anchor.InstanceId, StringComparison.Ordinal))
                    {
                        errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.MultipleAttachmentSides));
                    }
                }
                else
                {
                    errors.Add(new LayoutValidationError(collapse.InstanceId, LayoutGridFailure.CollapseTouchesOtherContainer));
                }
            }
        }
    }

    private static void ValidateWidgets(LayoutProfile profile, ICollection<LayoutValidationError> errors)
    {
        foreach (var container in profile.Containers)
        {
            if (container.GridBounds is not { } bounds)
            {
                continue;
            }

            ValidateSlot(container, container.PrimarySlot, bounds, errors);
            ValidateSlot(container, container.SecondarySlot, bounds, errors);
            if (container.ContainerKind == LayoutContainerKind.HoverSwitch)
            {
                foreach (var widget in container.PrimarySlot.Children.OfType<LayoutWidgetElement>())
                {
                    if (widget.Enabled && LayoutComponentCatalog.IsInteractive(widget))
                    {
                        errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.WidgetNotAllowed));
                    }
                }
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (collapse.GridBounds is { } bounds)
            {
                ValidateSlot(collapse, collapse.ExpandedSlot, bounds, errors);
            }
        }
    }

    private static void ValidateSlot(
        object owner,
        LayoutSlot slot,
        LayoutGridRect ownerBounds,
        ICollection<LayoutValidationError> errors)
    {
        foreach (var nested in slot.Children.OfType<LayoutContainerElement>())
        {
            errors.Add(new LayoutValidationError(nested.InstanceId, LayoutGridFailure.NotSupported));
        }

        var widgets = slot.Children.OfType<LayoutWidgetElement>()
            .Where(widget => widget.Enabled)
            .ToArray();
        foreach (var widget in widgets)
        {
            var bounds = widget.GridBounds;
            if (bounds is null)
            {
                errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.MissingGridBounds));
                continue;
            }

            if (bounds.IsEmpty)
            {
                errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.OutOfGrid));
                continue;
            }

            if (bounds.X < 0 ||
                bounds.Y < 0 ||
                bounds.Right > ownerBounds.Width ||
                bounds.Bottom > ownerBounds.Height)
            {
                errors.Add(new LayoutValidationError(widget.InstanceId, LayoutGridFailure.WidgetOutsideContainer));
            }
        }

        for (var index = 0; index < widgets.Length; index++)
        {
            for (var otherIndex = index + 1; otherIndex < widgets.Length; otherIndex++)
            {
                if (widgets[index].GridBounds is { } first &&
                    widgets[otherIndex].GridBounds is { } second &&
                    first.Overlaps(second))
                {
                    errors.Add(new LayoutValidationError(widgets[otherIndex].InstanceId, LayoutGridFailure.WidgetOverlap));
                }
            }
        }
    }

    private static void ValidateConnectivity(LayoutProfile profile, ICollection<LayoutValidationError> errors)
    {
        var enabled = profile.Containers
            .Where(container => container.Enabled && container.GridBounds is not null)
            .ToArray();
        if (enabled.Length == 0)
        {
            return;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(enabled[0].InstanceId);
        visited.Add(enabled[0].InstanceId);
        var byId = enabled.ToDictionary(container => container.InstanceId, StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var current = byId[queue.Dequeue()];
            foreach (var other in enabled)
            {
                if (!visited.Contains(other.InstanceId) &&
                    AreEdgeConnected(current.GridBounds!, other.GridBounds!))
                {
                    visited.Add(other.InstanceId);
                    queue.Enqueue(other.InstanceId);
                }
            }
        }

        foreach (var container in enabled.Where(container => !visited.Contains(container.InstanceId)))
        {
            errors.Add(new LayoutValidationError(container.InstanceId, LayoutGridFailure.DisconnectedContainerGraph));
        }
    }

    // ---------- 相邻图与依附 ----------

    /// <summary>
    /// 解析启用非折叠容器的连通分量；返回每个分量的容器 ID 列表。
    /// Resolves connected components of enabled non-collapse containers; returns container IDs per component.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ResolveContainerGraph(LayoutProfile profile)
    {
        var enabled = profile.Containers
            .Where(container => container.Enabled && container.GridBounds is not null)
            .ToArray();
        var components = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in enabled)
        {
            if (!visited.Add(start.InstanceId))
            {
                continue;
            }

            var component = new List<string> { start.InstanceId };
            var queue = new Queue<LayoutContainerElement>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var other in enabled)
                {
                    if (!visited.Contains(other.InstanceId) &&
                        AreEdgeConnected(current.GridBounds!, other.GridBounds!))
                    {
                        visited.Add(other.InstanceId);
                        component.Add(other.InstanceId);
                        queue.Enqueue(other);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// 解析折叠容器当前依附；公共边为 1 格厚的矩形，坐标使用档案全局网格。
    /// Resolves a collapse container's current attachment; the shared edge is a one-cell-thick grid rectangle.
    /// </summary>
    public static LayoutAttachmentInfo ResolveAttachment(LayoutCollapseContainer collapse, LayoutProfile profile)
    {
        var bounds = collapse.GridBounds;
        if (bounds is null)
        {
            return new LayoutAttachmentInfo(null, collapse.Attachment.AttachmentSide, LayoutGridRect.Unit(0, 0), false, LayoutGridFailure.MissingGridBounds);
        }

        var anchor = FindContainer(profile, collapse.Attachment.AnchorContainerId);
        if (anchor is null || !anchor.Enabled || anchor.GridBounds is null)
        {
            return new LayoutAttachmentInfo(null, collapse.Attachment.AttachmentSide, bounds, false, LayoutGridFailure.MissingAnchor);
        }

        var anchorBounds = anchor.GridBounds;
        var side = ContactSide(bounds, anchorBounds);
        if (side != ConnectionSide(collapse.Attachment))
        {
            return new LayoutAttachmentInfo(anchor, collapse.Attachment.AttachmentSide, bounds, false, LayoutGridFailure.InvalidAttachmentSide);
        }

        var shared = side switch
        {
            LayoutEdge.Top => new LayoutGridRect(
                Math.Max(bounds.X, anchorBounds.X),
                bounds.Y,
                Math.Min(bounds.Right, anchorBounds.Right) - Math.Max(bounds.X, anchorBounds.X),
                1),
            LayoutEdge.Bottom => new LayoutGridRect(
                Math.Max(bounds.X, anchorBounds.X),
                bounds.Bottom - 1,
                Math.Min(bounds.Right, anchorBounds.Right) - Math.Max(bounds.X, anchorBounds.X),
                1),
            LayoutEdge.Left => new LayoutGridRect(
                bounds.X,
                Math.Max(bounds.Y, anchorBounds.Y),
                1,
                Math.Min(bounds.Bottom, anchorBounds.Bottom) - Math.Max(bounds.Y, anchorBounds.Y)),
            _ => new LayoutGridRect(
                bounds.Right - 1,
                Math.Max(bounds.Y, anchorBounds.Y),
                1,
                Math.Min(bounds.Bottom, anchorBounds.Bottom) - Math.Max(bounds.Y, anchorBounds.Y))
        };
        return new LayoutAttachmentInfo(anchor, collapse.Attachment.AttachmentSide, shared, true, LayoutGridFailure.None);
    }

    // ---------- 编辑操作 ----------

    /// <summary>
    /// Adds a caller-provided widget to a container slot and validates the
    /// resulting immutable profile. The editor uses this overload when a
    /// palette entry carries semantic settings such as a specific command.
    /// </summary>
    public static LayoutGridEditResult TryAddWidget(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutWidgetElement widget)
    {
        if (FindAny(profile, widget.InstanceId) is not null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.DuplicateInstanceId);
        }

        if (FindContainer(profile, containerId) is { } container)
        {
            if (slotKind == LayoutSlotKind.Expanded ||
                slotKind == LayoutSlotKind.Secondary && container.ContainerKind != LayoutContainerKind.HoverSwitch)
            {
                return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
            }

            var slot = slotKind == LayoutSlotKind.Secondary
                ? container.SecondarySlot
                : container.PrimarySlot;
            var candidate = profile with
            {
                Containers = Replace(
                    profile.Containers,
                    container with
                    {
                        PrimarySlot = slotKind == LayoutSlotKind.Primary
                            ? slot with { Children = slot.Children.Append(widget).ToArray() }
                            : container.PrimarySlot,
                        SecondarySlot = slotKind == LayoutSlotKind.Secondary
                            ? slot with { Children = slot.Children.Append(widget).ToArray() }
                            : container.SecondarySlot
                    })
            };
            return ValidateCandidate(candidate, widget.InstanceId);
        }

        if (FindCollapse(profile, containerId) is { } collapse && slotKind == LayoutSlotKind.Expanded)
        {
            var candidate = profile with
            {
                CollapseContainers = Replace(
                    profile.CollapseContainers,
                    collapse with
                    {
                        ExpandedSlot = collapse.ExpandedSlot with
                        {
                            Children = collapse.ExpandedSlot.Children.Append(widget).ToArray()
                        }
                    })
            };
            return ValidateCandidate(candidate, widget.InstanceId);
        }

        return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
    }

    public static LayoutGridEditResult TryCreateFromDrag(
        LayoutProfile profile,
        LayoutPlacementTool tool,
        int startCellX,
        int startCellY,
        int currentCellX,
        int currentCellY)
    {
        var rect = LayoutGridRect.FromDrag(startCellX, startCellY, currentCellX, currentCellY);
        if (tool.IsContainer)
        {
            return TryCreateContainerFromRect(profile, tool.ContainerKind!.Value, rect);
        }

        return TryCreateWidgetFromRect(profile, tool, rect);
    }

    private static LayoutGridEditResult TryCreateContainerFromRect(
        LayoutProfile profile,
        LayoutContainerKind kind,
        LayoutGridRect rect)
    {
        var container = CreateContainer(kind) with { GridBounds = rect };
        var candidate = profile with
        {
            Containers = profile.Containers.Append(container).ToArray()
        };
        if (rect.IsEmpty || !IsInGrid(rect, profile.Grid))
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.OutOfGrid);
        }

        return ValidateCandidate(candidate, container.InstanceId);
    }

    private static LayoutGridEditResult TryCreateWidgetFromRect(
        LayoutProfile profile,
        LayoutPlacementTool tool,
        LayoutGridRect rect)
    {
        if (tool.OwnerContainerId is not { } ownerId)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        var owner = FindContainer(profile, ownerId);
        if (owner is null || !owner.Enabled || owner.GridBounds is not { } ownerBounds)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        if (tool.SlotKind != LayoutSlotKind.Primary &&
            tool.SlotKind != LayoutSlotKind.Secondary)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
        }

        if (tool.SlotKind == LayoutSlotKind.Secondary &&
            owner.ContainerKind != LayoutContainerKind.HoverSwitch)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
        }

        // 画布全局格转容器局部格。
        // Convert canvas-global cells to container-local cells.
        var local = new LayoutGridRect(
            rect.X - ownerBounds.X,
            rect.Y - ownerBounds.Y,
            rect.Width,
            rect.Height);
        var widget = CreateWidget(tool.WidgetTypeId!) with { GridBounds = local };
        if (!LayoutComponentCatalog.TryGet(tool.WidgetTypeId!, out _))
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
        }

        var slot = tool.SlotKind == LayoutSlotKind.Secondary
            ? owner.SecondarySlot
            : owner.PrimarySlot;
        var candidate = RewriteContainerSlot(
            profile,
            ownerId,
            tool.SlotKind,
            slot with { Children = slot.Children.Append(widget).ToArray() });
        return ValidateCandidate(candidate, widget.InstanceId);
    }

    public static LayoutGridEditResult TryMove(
        LayoutProfile profile,
        string instanceId,
        int deltaX,
        int deltaY)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            return LayoutGridEditResult.Ok(profile);
        }

        var target = FindAny(profile, instanceId);
        if (target is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        switch (target)
        {
            case LayoutContainerElement container when container.GridBounds is { } bounds:
            {
                var candidate = profile with
                {
                    Containers = Replace(profile.Containers, container with
                    {
                        GridBounds = new LayoutGridRect(
                            bounds.X + deltaX,
                            bounds.Y + deltaY,
                            bounds.Width,
                            bounds.Height)
                    })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutCollapseContainer collapse when collapse.GridBounds is { } bounds:
            {
                var candidate = profile with
                {
                    CollapseContainers = Replace(profile.CollapseContainers, collapse with
                    {
                        GridBounds = new LayoutGridRect(
                            bounds.X + deltaX,
                            bounds.Y + deltaY,
                            bounds.Width,
                            bounds.Height)
                    })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutWidgetElement widget when widget.GridBounds is { } bounds:
            {
                var next = widget with
                {
                    GridBounds = new LayoutGridRect(
                        bounds.X + deltaX,
                        bounds.Y + deltaY,
                        bounds.Width,
                        bounds.Height)
                };
                var owner = FindWidgetOwner(profile, instanceId);
                if (owner is null)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
                }

                return ValidateCandidate(ReplaceWidget(profile, owner.Value.ContainerId, owner.Value.SlotKind, next), instanceId);
            }
            default:
                return LayoutGridEditResult.Fail(LayoutGridFailure.MissingGridBounds);
        }
    }

    public static LayoutGridEditResult TryResize(
        LayoutProfile profile,
        string instanceId,
        LayoutEdge edge,
        int delta)
    {
        if (delta == 0)
        {
            return LayoutGridEditResult.Ok(profile);
        }

        var target = FindAny(profile, instanceId);
        if (target is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        switch (target)
        {
            case LayoutContainerElement container when container.GridBounds is { } bounds:
            {
                var resized = ResizeRect(bounds, edge, delta);
                if (resized.IsEmpty)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.OutOfGrid);
                }

                // 右/下缩放必须容纳子组件；左/上缩放通过平移子组件保持其屏幕位置。
                // Right/bottom resizing must contain children; left/top resizing shifts children to keep their screen position.
                if (edge == LayoutEdge.Right && resized.Width < ComputeMinWidth(container))
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerWouldExposeChild);
                }

                if (edge == LayoutEdge.Bottom && resized.Height < ComputeMinHeight(container))
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerWouldExposeChild);
                }

                var shiftX = edge == LayoutEdge.Left ? delta : 0;
                var shiftY = edge == LayoutEdge.Top ? delta : 0;
                if (shiftX > 0 || shiftY > 0)
                {
                    if (ChildrenExposeContainer(container, shiftX, shiftY))
                    {
                        return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerWouldExposeChild);
                    }
                }

                var shifted = ShiftChildren(container, shiftX, shiftY);
                var candidate = profile with
                {
                    Containers = Replace(profile.Containers, shifted with { GridBounds = resized })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutCollapseContainer collapse when collapse.GridBounds is { } bounds:
            {
                var resized = ResizeRect(bounds, edge, delta);
                if (resized.IsEmpty)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.OutOfGrid);
                }

                if (ChildrenExposeBounds(collapse.ExpandedSlot, resized))
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerWouldExposeChild);
                }

                var candidate = profile with
                {
                    CollapseContainers = Replace(profile.CollapseContainers, collapse with { GridBounds = resized })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutWidgetElement widget when widget.GridBounds is { } bounds:
            {
                var owner = FindWidgetOwner(profile, instanceId);
                if (owner is null)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
                }

                var ownerContainer = FindContainer(profile, owner.Value.ContainerId);
                if (ownerContainer?.GridBounds is not { } ownerBounds)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
                }

                var resized = ResizeRect(bounds, edge, delta);
                if (resized.IsEmpty || resized.X < 0 || resized.Y < 0 ||
                    resized.Right > ownerBounds.Width || resized.Bottom > ownerBounds.Height)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.WidgetOutsideContainer);
                }

                return ValidateCandidate(
                    ReplaceWidget(profile, owner.Value.ContainerId, owner.Value.SlotKind, widget with { GridBounds = resized }),
                    instanceId);
            }
            default:
                return LayoutGridEditResult.Fail(LayoutGridFailure.MissingGridBounds);
        }
    }

    public static LayoutGridEditResult TryRemove(LayoutProfile profile, string instanceId)
    {
        var target = FindAny(profile, instanceId);
        if (target is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        if (target is LayoutContainerElement container)
        {
            var enabledCount = profile.Containers.Count(item => item.Enabled);
            if (enabledCount == 1 && container.Enabled)
            {
                return LayoutGridEditResult.Fail(LayoutGridFailure.LastNonCollapseContainer);
            }

            if (profile.CollapseContainers.Any(collapse =>
                    collapse.Enabled &&
                    string.Equals(collapse.Attachment.AnchorContainerId, instanceId, StringComparison.Ordinal)))
            {
                return LayoutGridEditResult.Fail(LayoutGridFailure.AnchorInUse);
            }

            var candidate = profile with
            {
                Containers = profile.Containers
                    .Where(item => !string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal))
                    .ToArray()
            };
            var errors = ValidateProfile(candidate);
            if (errors.Count > 0)
            {
                return LayoutGridEditResult.Fail(PickFailure(errors, null));
            }

            return LayoutGridEditResult.Ok(candidate);
        }

        if (target is LayoutCollapseContainer)
        {
            var candidate = profile with
            {
                CollapseContainers = profile.CollapseContainers
                    .Where(item => !string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal))
                    .ToArray()
            };
            return LayoutGridEditResult.Ok(candidate);
        }

        // 组件删除：从所属槽位移除。
        var widgetOwner = FindWidgetOwner(profile, instanceId);
        if (widgetOwner is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        var slot = GetSlot(profile, widgetOwner.Value.ContainerId, widgetOwner.Value.SlotKind);
        if (slot is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        var rewritten = slot with
        {
            Children = slot.Children
                .Where(item => !string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal))
                .ToArray()
        };
        return LayoutGridEditResult.Ok(RewriteContainerSlot(profile, widgetOwner.Value.ContainerId, widgetOwner.Value.SlotKind, rewritten));
    }

    public static LayoutGridEditResult TrySetEnabled(
        LayoutProfile profile,
        string instanceId,
        bool enabled)
    {
        var target = FindAny(profile, instanceId);
        if (target is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        if (target is LayoutContainerElement container)
        {
            if (!enabled)
            {
                var enabledCount = profile.Containers.Count(item => item.Enabled);
                if (enabledCount == 1 && container.Enabled)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.LastNonCollapseContainer);
                }

                if (profile.CollapseContainers.Any(collapse =>
                        collapse.Enabled &&
                        string.Equals(collapse.Attachment.AnchorContainerId, instanceId, StringComparison.Ordinal)))
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.AnchorInUse);
                }
            }

            var candidate = profile with
            {
                Containers = Replace(profile.Containers, container with { Enabled = enabled })
            };
            return ValidateCandidate(candidate, instanceId);
        }

        if (target is LayoutCollapseContainer collapse)
        {
            var candidate = profile with
            {
                CollapseContainers = Replace(profile.CollapseContainers, collapse with { Enabled = enabled })
            };
            return ValidateCandidate(candidate, instanceId);
        }

        return LayoutGridEditResult.Fail(LayoutGridFailure.NotSupported);
    }

    /// <summary>
    /// 属性面板直接编辑 X/Y/W/H 时复用同一约束服务。
    /// Property-panel X/Y/W/H edits reuse this same constraint service.
    /// </summary>
    public static LayoutGridEditResult TrySetGridBounds(
        LayoutProfile profile,
        string instanceId,
        LayoutGridRect rect)
    {
        var target = FindAny(profile, instanceId);
        if (target is null)
        {
            return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
        }

        switch (target)
        {
            case LayoutContainerElement container:
            {
                var candidate = profile with
                {
                    Containers = Replace(profile.Containers, container with { GridBounds = rect })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutCollapseContainer collapse:
            {
                var candidate = profile with
                {
                    CollapseContainers = Replace(profile.CollapseContainers, collapse with { GridBounds = rect })
                };
                return ValidateCandidate(candidate, instanceId);
            }
            case LayoutWidgetElement widget:
            {
                var owner = FindWidgetOwner(profile, instanceId);
                if (owner is null)
                {
                    return LayoutGridEditResult.Fail(LayoutGridFailure.ContainerNotFound);
                }

                return ValidateCandidate(
                    ReplaceWidget(profile, owner.Value.ContainerId, owner.Value.SlotKind, widget with { GridBounds = rect }),
                    instanceId);
            }
            default:
                return LayoutGridEditResult.Fail(LayoutGridFailure.MissingGridBounds);
        }
    }

    // ---------- 查询 ----------

    /// <summary>
    /// 候选容器矩形是否可放置；ignoredId 表示移动中的对象自身。
    /// Whether a candidate container rectangle can be placed; ignoredId is the instance being moved.
    /// </summary>
    public static bool CanPlaceContainer(
        LayoutProfile profile,
        LayoutGridRect candidate,
        string? ignoredId = null)
    {
        if (candidate.IsEmpty || !IsInGrid(candidate, profile.Grid))
        {
            return false;
        }

        foreach (var container in profile.Containers)
        {
            if (string.Equals(container.InstanceId, ignoredId, StringComparison.Ordinal) ||
                container.GridBounds is not { } bounds)
            {
                continue;
            }

            if (candidate.Overlaps(bounds))
            {
                return false;
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (!collapse.Enabled || collapse.GridBounds is not { } collapseBounds)
            {
                continue;
            }

            if (candidate.Overlaps(collapseBounds))
            {
                return false;
            }

            var contact = ContactSide(candidate, collapseBounds);
            if (contact is null)
            {
                continue;
            }

            var isAnchor = string.Equals(collapse.Attachment.AnchorContainerId, ignoredId, StringComparison.Ordinal) &&
                contact == ConnectionSide(collapse.Attachment);
            if (!isAnchor)
            {
                return false;
            }
        }

        return IsConnectedAfterPlacement(profile, candidate, ignoredId);
    }

    /// <summary>
    /// 候选组件局部矩形是否可放入容器指定槽位；ignoredId 表示移动中的组件自身。
    /// Whether a candidate widget-local rectangle fits the container slot; ignoredId is the widget being moved.
    /// </summary>
    public static bool CanPlaceWidget(
        LayoutProfile profile,
        string ownerId,
        LayoutSlotKind slotKind,
        LayoutGridRect candidate,
        string? ignoredId = null)
    {
        var inlineOwner = FindContainer(profile, ownerId);
        var collapseOwner = FindCollapse(profile, ownerId);
        var ownerBounds = inlineOwner?.GridBounds ?? collapseOwner?.GridBounds;
        if ((inlineOwner is null && collapseOwner is null) ||
            inlineOwner is { Enabled: false } ||
            collapseOwner is { Enabled: false } ||
            ownerBounds is not { } bounds)
        {
            return false;
        }

        if (candidate.IsEmpty ||
            candidate.X < 0 ||
            candidate.Y < 0 ||
            candidate.Right > bounds.Width ||
            candidate.Bottom > bounds.Height)
        {
            return false;
        }

        var slot = GetSlot(profile, ownerId, slotKind);
        if (slot is null)
        {
            return false;
        }

        return slot.Children.OfType<LayoutWidgetElement>()
            .Where(widget => widget.Enabled &&
                !string.Equals(widget.InstanceId, ignoredId, StringComparison.Ordinal))
            .All(widget => widget.GridBounds is not { } bounds || !candidate.Overlaps(bounds));
    }

    // ---------- 内部工具 ----------

    private static LayoutGridEditResult ValidateCandidate(LayoutProfile candidate, string instanceId)
    {
        var errors = ValidateProfile(candidate);
        if (errors.Count == 0)
        {
            return LayoutGridEditResult.Ok(candidate);
        }

        return LayoutGridEditResult.Fail(PickFailure(errors, instanceId));
    }

    private static LayoutGridFailure PickFailure(IReadOnlyList<LayoutValidationError> errors, string? instanceId)
    {
        if (instanceId is not null)
        {
            var direct = errors.FirstOrDefault(error =>
                string.Equals(error.InstanceId, instanceId, StringComparison.Ordinal));
            if (direct is not null)
            {
                return direct.Failure;
            }
        }

        return errors.Count > 0 ? errors[0].Failure : LayoutGridFailure.NotSupported;
    }

    private static bool IsInGrid(LayoutGridRect rect, LayoutGridSettings grid) =>
        rect.Width >= 1 &&
        rect.Height >= 1 &&
        rect.X >= 0 &&
        rect.Y >= 0 &&
        rect.Right <= grid.Columns &&
        rect.Bottom <= grid.Rows;

    private static IEnumerable<ContainerRef> AllContainers(LayoutProfile profile)
    {
        foreach (var container in profile.Containers)
        {
            yield return new ContainerRef(container.InstanceId, container.Enabled, container.GridBounds);
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            yield return new ContainerRef(collapse.InstanceId, collapse.Enabled, collapse.GridBounds);
        }
    }

    private readonly record struct ContainerRef(string InstanceId, bool Enabled, LayoutGridRect? GridBounds);

    /// <summary>
    /// 两个矩形是否边相接（垂直接边或水平接边且交叉长度至少一格）。
    /// Whether two rectangles share a vertical or horizontal edge with an intersection of at least one cell.
    /// </summary>
    public static bool AreEdgeConnected(LayoutGridRect a, LayoutGridRect b) =>
        ((a.Right == b.X || b.Right == a.X) &&
            Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Y, b.Y)) ||
        ((a.Bottom == b.Y || b.Bottom == a.Y) &&
            Math.Min(a.Right, b.Right) > Math.Max(a.X, b.X));

    /// <summary>
    /// 返回 a 的哪条边与 b 相接；不相接时返回 null。
    /// Returns which side of a touches b, or null when they do not touch.
    /// </summary>
    public static LayoutEdge? ContactSide(LayoutGridRect a, LayoutGridRect b)
    {
        if (a.Bottom == b.Y && Math.Min(a.Right, b.Right) > Math.Max(a.X, b.X))
        {
            return LayoutEdge.Bottom;
        }

        if (a.Y == b.Bottom && Math.Min(a.Right, b.Right) > Math.Max(a.X, b.X))
        {
            return LayoutEdge.Top;
        }

        if (a.Right == b.X && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Y, b.Y))
        {
            return LayoutEdge.Right;
        }

        if (a.X == b.Right && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Y, b.Y))
        {
            return LayoutEdge.Left;
        }

        return null;
    }

    /// <summary>
    /// 折叠容器与锚点相接的自身边（AttachmentSide 是锚点被依附的边，故取对边）。
    /// The collapse container's own side that must touch the anchor (opposite of the anchor side).
    /// </summary>
    public static LayoutEdge ConnectionSide(LayoutAttachment attachment) => attachment.AttachmentSide switch
    {
        LayoutEdge.Top => LayoutEdge.Bottom,
        LayoutEdge.Bottom => LayoutEdge.Top,
        LayoutEdge.Left => LayoutEdge.Right,
        _ => LayoutEdge.Left
    };

    private static LayoutGridRect ResizeRect(LayoutGridRect rect, LayoutEdge edge, int delta) => edge switch
    {
        LayoutEdge.Left => new LayoutGridRect(rect.X + delta, rect.Y, rect.Width - delta, rect.Height),
        LayoutEdge.Top => new LayoutGridRect(rect.X, rect.Y + delta, rect.Width, rect.Height - delta),
        LayoutEdge.Right => new LayoutGridRect(rect.X, rect.Y, rect.Width + delta, rect.Height),
        _ => new LayoutGridRect(rect.X, rect.Y, rect.Width, rect.Height + delta)
    };

    private static int ComputeMinWidth(LayoutContainerElement container) =>
        Math.Max(1, Slots(container)
            .SelectMany(slot => slot.Children.OfType<LayoutWidgetElement>())
            .Where(widget => widget.Enabled && widget.GridBounds is not null)
            .Select(widget => widget.GridBounds!.Right)
            .DefaultIfEmpty(1)
            .Max());

    private static int ComputeMinHeight(LayoutContainerElement container) =>
        Math.Max(1, Slots(container)
            .SelectMany(slot => slot.Children.OfType<LayoutWidgetElement>())
            .Where(widget => widget.Enabled && widget.GridBounds is not null)
            .Select(widget => widget.GridBounds!.Bottom)
            .DefaultIfEmpty(1)
            .Max());

    private static bool ChildrenExposeContainer(
        LayoutContainerElement container,
        int shiftX,
        int shiftY)
    {
        return Slots(container)
            .SelectMany(slot => slot.Children.OfType<LayoutWidgetElement>())
            .Where(widget => widget.Enabled && widget.GridBounds is not null)
            .Any(widget =>
            {
                var bounds = widget.GridBounds!;
                return bounds.X - shiftX < 0 || bounds.Y - shiftY < 0;
            });
    }

    private static LayoutContainerElement ShiftChildren(
        LayoutContainerElement container,
        int shiftX,
        int shiftY)
    {
        if (shiftX == 0 && shiftY == 0)
        {
            return container;
        }

        LayoutSlot Shift(LayoutSlot slot) => slot with
        {
            Children = slot.Children
                .Select(child => child is LayoutWidgetElement { GridBounds: { } bounds } widget
                    ? widget with
                    {
                        GridBounds = new LayoutGridRect(
                            bounds.X - shiftX,
                            bounds.Y - shiftY,
                            bounds.Width,
                            bounds.Height)
                    }
                    : child)
                .ToArray()
        };
        return container with
        {
            PrimarySlot = Shift(container.PrimarySlot),
            SecondarySlot = Shift(container.SecondarySlot)
        };
    }

    private static bool ChildrenExposeBounds(LayoutSlot slot, LayoutGridRect owner)
    {
        return slot.Children.OfType<LayoutWidgetElement>()
            .Where(widget => widget.Enabled && widget.GridBounds is not null)
            .Any(widget =>
            {
                var bounds = widget.GridBounds!;
                return bounds.X < 0 ||
                    bounds.Y < 0 ||
                    bounds.Right > owner.Width ||
                    bounds.Bottom > owner.Height;
            });
    }

    private static bool IsConnectedAfterPlacement(
        LayoutProfile profile,
        LayoutGridRect candidate,
        string? ignoredId)
    {
        var rects = new List<LayoutGridRect>();
        foreach (var container in profile.Containers)
        {
            if (!container.Enabled || container.GridBounds is not { } bounds)
            {
                continue;
            }

            rects.Add(string.Equals(container.InstanceId, ignoredId, StringComparison.Ordinal)
                ? candidate
                : bounds);
        }

        if (ignoredId is null)
        {
            // 追加场景：候选矩形是尚未入图的新容器，必须参与连通性分析。
            // When appending, the candidate is a brand-new container and must join the graph.
            rects.Add(candidate);
        }

        if (rects.Count == 0)
        {
            return true;
        }

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(0);
        visited.Add(0);
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            for (var other = 0; other < rects.Count; other++)
            {
                if (visited.Contains(other))
                {
                    continue;
                }

                if (AreEdgeConnected(rects[index], rects[other]))
                {
                    visited.Add(other);
                    queue.Enqueue(other);
                }
            }
        }

        return visited.Count == rects.Count;
    }

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> source, T item)
    {
        return source.Select(existing => Match(existing, item) ? item : existing).ToArray();
    }

    private static bool Match<T>(T existing, T item) => (existing, item) switch
    {
        (LayoutElement a, LayoutElement b) =>
            string.Equals(a.InstanceId, b.InstanceId, StringComparison.Ordinal),
        (LayoutCollapseContainer a, LayoutCollapseContainer b) =>
            string.Equals(a.InstanceId, b.InstanceId, StringComparison.Ordinal),
        _ => Equals(existing, item)
    };

    private static (string ContainerId, LayoutSlotKind SlotKind)? FindWidgetOwner(
        LayoutProfile profile,
        string widgetId)
    {
        foreach (var container in profile.Containers)
        {
            if (container.PrimarySlot.Children.Any(child =>
                    string.Equals(child.InstanceId, widgetId, StringComparison.Ordinal)))
            {
                return (container.InstanceId, LayoutSlotKind.Primary);
            }

            if (container.SecondarySlot.Children.Any(child =>
                    string.Equals(child.InstanceId, widgetId, StringComparison.Ordinal)))
            {
                return (container.InstanceId, LayoutSlotKind.Secondary);
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (collapse.ExpandedSlot.Children.Any(child =>
                    string.Equals(child.InstanceId, widgetId, StringComparison.Ordinal)))
            {
                return (collapse.InstanceId, LayoutSlotKind.Expanded);
            }
        }

        return null;
    }

    private static LayoutSlot? GetSlot(LayoutProfile profile, string containerId, LayoutSlotKind slotKind)
    {
        if (FindContainer(profile, containerId) is { } container)
        {
            return slotKind == LayoutSlotKind.Secondary
                ? container.SecondarySlot
                : container.PrimarySlot;
        }

        if (FindCollapse(profile, containerId) is { } collapse)
        {
            return slotKind == LayoutSlotKind.Expanded ? collapse.ExpandedSlot : null;
        }

        return null;
    }

    private static LayoutProfile RewriteContainerSlot(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutSlot slot)
    {
        if (FindCollapse(profile, containerId) is { } collapse)
        {
            return profile with
            {
                CollapseContainers = Replace(
                    profile.CollapseContainers,
                    collapse with { ExpandedSlot = slot })
            };
        }

        var container = FindContainer(profile, containerId);
        if (container is null)
        {
            return profile;
        }

        var updated = slotKind == LayoutSlotKind.Secondary
            ? container with { SecondarySlot = slot }
            : container with { PrimarySlot = slot };
        return profile with
        {
            Containers = Replace(profile.Containers, updated)
        };
    }

    private static LayoutProfile ReplaceWidget(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutWidgetElement widget)
    {
        var current = GetSlot(profile, containerId, slotKind);
        if (current is null)
        {
            return profile;
        }

        var slot = current with
        {
            Children = current.Children
                .Select(child => string.Equals(child.InstanceId, widget.InstanceId, StringComparison.Ordinal)
                    ? widget
                    : child)
                .ToArray()
        };
        return RewriteContainerSlot(profile, containerId, slotKind, slot);
    }
}
