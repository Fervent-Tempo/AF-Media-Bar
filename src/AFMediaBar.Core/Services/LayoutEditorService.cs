using AFMediaBar.Models;

namespace AFMediaBar.Services;

public enum LayoutSlotKind
{
    Primary = 0,
    Secondary = 1,
    Expanded = 2
}

public enum LayoutEditFailure
{
    None = 0,
    ContainerNotFound = 1,
    DuplicateInstanceId = 2,
    InteractiveNotAllowed = 3,
    UnsupportedWidget = 4,
    EdgeUnavailable = 5,
    InvalidContainerKind = 6,
    InvalidPlacement = 7
}

/// <summary>
/// schema 4 的不可变档案编辑门面：放置与删除委托给网格约束服务，属性类编辑由本服务集中执行。
/// Immutable schema-4 profile editing facade; placement and removal delegate to the grid constraint
/// service while property edits stay centralized here.
/// </summary>
public static class LayoutEditorService
{
    // ---------- 创建工厂 ----------

    public static LayoutContainerElement CreateContainer(LayoutContainerKind kind) =>
        LayoutGridConstraintService.CreateContainer(kind);

    public static LayoutWidgetElement CreateWidget(string typeId) =>
        LayoutGridConstraintService.CreateWidget(typeId);

    /// <summary>
    /// 返回组件按当前档案方向和设置完整显示所需的最小网格尺寸；结构约束仍允许用户缩小到 1x1。
    /// Returns the minimum grid footprint needed for a widget to render its content without clipping; structural constraints still allow 1x1.
    /// </summary>
    public static (int Width, int Height) ResolveWidgetRequiredCells(
        LayoutProfile profile,
        LayoutWidgetElement widget) =>
        MeasureWidgetCells(profile, widget);

    public static LayoutCollapseContainer CreateCollapse(
        LayoutContainerElement anchor,
        LayoutEdge attachmentSide,
        LayoutGridRect rect) =>
        LayoutGridConstraintService.CreateCollapseContainer(anchor, attachmentSide, rect);

    // ---------- 对象查找 ----------

    public static object? Find(LayoutProfile profile, string instanceId) =>
        LayoutGridConstraintService.FindAny(profile, instanceId);

    public static LayoutContainerElement? FindContainer(LayoutProfile profile, string instanceId) =>
        LayoutGridConstraintService.FindContainer(profile, instanceId);

    public static LayoutCollapseContainer? FindCollapse(LayoutProfile profile, string instanceId) =>
        LayoutGridConstraintService.FindCollapse(profile, instanceId);

    public static LayoutElement? Find(LayoutContainerElement container, string instanceId)
    {
        if (string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return container;
        }

        foreach (var child in container.PrimarySlot.Children.Concat(container.SecondarySlot.Children))
        {
            if (string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return child;
            }

            if (child is LayoutContainerElement nested && Find(nested, instanceId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    // ---------- 顶层添加 ----------

    /// <summary>
    /// 添加容器：自动在网格上寻找不与现有占用冲突的放置矩形，并保证容器图连通。
    /// Adds a container by finding a free, connected placement on the grid.
    /// </summary>
    public static bool TryAddContainer(
        LayoutProfile profile,
        LayoutContainerKind kind,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (kind == LayoutContainerKind.AutoCollapse)
        {
            updated = profile;
            failure = LayoutEditFailure.InvalidContainerKind;
            return false;
        }

        var container = CreateContainer(kind);
        var (width, height) = ResolveDefaultContainerCells(profile);
        if (!TryFindFreePlacement(profile, width, height, out var rect))
        {
            updated = profile;
            failure = LayoutEditFailure.InvalidPlacement;
            return false;
        }

        return ValidateEdit(
            profile with
            {
                Containers = profile.Containers.Append(container with { GridBounds = rect }).ToArray()
            },
            container.InstanceId,
            out updated,
            out failure);
    }

    /// <summary>
    /// 添加折叠容器：依附到第一个启用容器，沿指定边展开；任务栏宿主不可用边直接拒绝。
    /// Adds a collapse container attached to the first enabled container along the requested side.
    /// </summary>
    public static bool TryAddCollapse(
        LayoutProfile profile,
        LayoutEdge attachmentSide,
        LayoutEdge? unavailableSide,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (unavailableSide == attachmentSide)
        {
            updated = profile;
            failure = LayoutEditFailure.EdgeUnavailable;
            return false;
        }

        var anchor = profile.Containers.FirstOrDefault(container =>
            container.Enabled && container.GridBounds is not null);
        if (anchor is null || anchor.GridBounds is not { } anchorBounds)
        {
            updated = profile;
            failure = LayoutEditFailure.ContainerNotFound;
            return false;
        }

        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cells = ResolveDefaultCollapseCells(profile);
        var rect = ResolveCollapseRect(anchorBounds, attachmentSide, cells);
        if (!IsInGrid(rect, grid))
        {
            // 锚点贴着网格边缘时无法沿该侧展开，回退到对边。
            // When the anchor hugs a grid edge, fall back to the opposite side.
            var fallbackSide = attachmentSide switch
            {
                LayoutEdge.Top => LayoutEdge.Bottom,
                LayoutEdge.Bottom => LayoutEdge.Top,
                LayoutEdge.Left => LayoutEdge.Right,
                _ => LayoutEdge.Left
            };
            if (unavailableSide == fallbackSide)
            {
                updated = profile;
                failure = LayoutEditFailure.EdgeUnavailable;
                return false;
            }

            rect = ResolveCollapseRect(anchorBounds, fallbackSide, cells);
            if (!IsInGrid(rect, grid))
            {
                updated = profile;
                failure = LayoutEditFailure.InvalidPlacement;
                return false;
            }

            attachmentSide = fallbackSide;
        }

        var collapse = CreateCollapse(anchor, attachmentSide, rect);
        return ValidateEdit(
            profile with
            {
                CollapseContainers = profile.CollapseContainers.Append(collapse).ToArray()
            },
            collapse.InstanceId,
            out updated,
            out failure);
    }

    // ---------- 组件编辑 ----------

    public static bool TryAddWidget(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutWidgetElement widget,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (Find(profile, widget.InstanceId) is not null)
        {
            updated = profile;
            failure = LayoutEditFailure.DuplicateInstanceId;
            return false;
        }

        if (!ComponentCatalog.TryGet(widget.TypeId, out _))
        {
            updated = profile;
            failure = LayoutEditFailure.UnsupportedWidget;
            return false;
        }

        if (FindContainer(profile, containerId) is { } container)
        {
            if (slotKind == LayoutSlotKind.Expanded ||
                slotKind == LayoutSlotKind.Secondary &&
                container.ContainerKind != LayoutContainerKind.HoverSwitch)
            {
                updated = profile;
                failure = LayoutEditFailure.ContainerNotFound;
                return false;
            }

            var slot = slotKind == LayoutSlotKind.Secondary
                ? container.SecondarySlot
                : container.PrimarySlot;
            var placed = PlaceWidgetInSlot(profile, slot, widget, container.GridBounds, out failure);
            if (placed is null)
            {
                updated = profile;
                return false;
            }

            var candidate = RewriteContainerSlot(
                profile,
                containerId,
                slotKind,
                slot with { Children = slot.Children.Append(placed).ToArray() });
            return ValidateEdit(candidate, widget.InstanceId, out updated, out failure);
        }

        if (FindCollapse(profile, containerId) is { } collapse)
        {
            if (slotKind != LayoutSlotKind.Expanded)
            {
                updated = profile;
                failure = LayoutEditFailure.ContainerNotFound;
                return false;
            }

            var slot = collapse.ExpandedSlot;
            var placed = PlaceWidgetInSlot(profile, slot, widget, collapse.GridBounds, out failure);
            if (placed is null)
            {
                updated = profile;
                return false;
            }

            var candidate = profile with
            {
                CollapseContainers = Replace(
                    profile.CollapseContainers,
                    collapse with { ExpandedSlot = slot with { Children = slot.Children.Append(placed).ToArray() } })
            };
            return ValidateEdit(candidate, widget.InstanceId, out updated, out failure);
        }

        updated = profile;
        failure = LayoutEditFailure.ContainerNotFound;
        return false;
    }

    public static bool TryRelocateWidget(
        LayoutProfile profile,
        string instanceId,
        string targetContainerId,
        LayoutSlotKind targetSlot,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (Find(profile, instanceId) is not LayoutWidgetElement widget ||
            !TryRemove(profile, instanceId, out var removed))
        {
            updated = profile;
            failure = LayoutEditFailure.ContainerNotFound;
            return false;
        }

        if (!TryAddWidget(
                removed,
                targetContainerId,
                targetSlot,
                widget,
                out updated,
                out failure))
        {
            updated = profile;
            return false;
        }

        return updated != profile;
    }

    public static bool TryRemove(LayoutProfile profile, string instanceId, out LayoutProfile updated)
    {
        var result = LayoutGridConstraintService.TryRemove(profile, instanceId);
        updated = result.Updated ?? profile;
        return result.Success;
    }

    public static bool TrySetEnabled(
        LayoutProfile profile,
        string instanceId,
        bool enabled,
        out LayoutProfile updated)
    {
        var result = LayoutGridConstraintService.TrySetEnabled(profile, instanceId, enabled);
        updated = result.Updated ?? profile;
        return result.Success;
    }

    public static bool TryReorderTopLevel(
        LayoutProfile profile,
        string sourceId,
        string targetId,
        out LayoutProfile updated)
    {
        if (TryReorderList(profile.Containers, sourceId, targetId, out var containers))
        {
            updated = profile with { Containers = containers };
            return true;
        }

        if (TryReorderList(profile.CollapseContainers, sourceId, targetId, out var collapse))
        {
            updated = profile with { CollapseContainers = collapse };
            return true;
        }

        updated = profile;
        return false;
    }

    /// <summary>
    /// 上移/下移按钮：在对应顶层列表内按相邻兄弟重排。
    /// Move up/down buttons reorder within the owning top-level list.
    /// </summary>
    public static bool TryMove(LayoutProfile profile, string instanceId, int offset, out LayoutProfile updated)
    {
        if (offset == 0)
        {
            updated = profile;
            return false;
        }

        if (TryMoveList(profile.Containers, instanceId, offset, out var containers))
        {
            updated = profile with { Containers = containers };
            return true;
        }

        if (TryMoveList(profile.CollapseContainers, instanceId, offset, out var collapse))
        {
            updated = profile with { CollapseContainers = collapse };
            return true;
        }

        // 组件重排：在所属槽位内移动。
        if (FindWidgetSlot(profile, instanceId) is { } location &&
            TryMoveList(location.Slot.Children, instanceId, offset, out var children))
        {
            var rewritten = location.Slot with { Children = children };
            updated = RewriteContainerSlot(profile, location.ContainerId, location.SlotKind, rewritten);
            return true;
        }

        updated = profile;
        return false;
    }

    public static bool TrySetGridBounds(
        LayoutProfile profile,
        string instanceId,
        LayoutGridRect rect,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        var result = LayoutGridConstraintService.TrySetGridBounds(profile, instanceId, rect);
        updated = result.Updated ?? profile;
        failure = result.Success ? LayoutEditFailure.None : MapFailure(result.Failure);
        return result.Success;
    }

    public static bool TryTranslate(
        LayoutProfile profile,
        string instanceId,
        int deltaX,
        int deltaY,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        var result = LayoutGridConstraintService.TryMove(profile, instanceId, deltaX, deltaY);
        updated = result.Updated ?? profile;
        failure = result.Success ? LayoutEditFailure.None : MapFailure(result.Failure);
        return result.Success;
    }

    public static bool TryResize(
        LayoutProfile profile,
        string instanceId,
        LayoutEdge edge,
        int delta,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        var result = LayoutGridConstraintService.TryResize(profile, instanceId, edge, delta);
        updated = result.Updated ?? profile;
        failure = result.Success ? LayoutEditFailure.None : MapFailure(result.Failure);
        return result.Success;
    }

    // ---------- 属性编辑 ----------

    public static bool TryUpdateWidgetSettings(
        LayoutProfile profile,
        string instanceId,
        WidgetSettings settings,
        out LayoutProfile updated) =>
        TryUpdateElement(profile, instanceId, element =>
            element is LayoutWidgetElement widget ? widget with { Settings = settings } : element, out updated);

    public static bool TryUpdateWidgetSkin(
        LayoutProfile profile,
        string instanceId,
        ComponentSkinAssignment? assignment,
        out LayoutProfile updated) =>
        TryUpdateElement(profile, instanceId, element =>
            element is LayoutWidgetElement widget
                ? widget with
                {
                    SkinId = assignment?.SkinId,
                    SkinVersion = assignment?.Version,
                    SkinSettings = assignment?.Settings
                }
                : element,
            out updated);

    public static bool TryResetWidgetProperties(
        LayoutProfile profile,
        string instanceId,
        out LayoutProfile updated) =>
        TryUpdateElement(profile, instanceId, element =>
        {
            if (element is not LayoutWidgetElement widget)
            {
                return element;
            }

            var defaults = ComponentCatalog.CreateDefaultSettings(widget.TypeId);
            // 调色板中的“上一首/歌手”等条目共享底层类型；恢复属性时保留当前语义角色，避免按钮或文本类型被重置。
            // Palette entries share a storage type; preserve the current semantic role so reset does not change a command or text kind.
            defaults = (defaults, widget.Settings) switch
            {
                (CommandWidgetSettings defaultCommand, CommandWidgetSettings currentCommand) =>
                    defaultCommand with { Command = currentCommand.Command },
                (MediaTextWidgetSettings defaultText, MediaTextWidgetSettings currentText) =>
                    defaultText with { TextKind = currentText.TextKind },
                _ => defaults
            };
            return widget with
            {
                Settings = defaults,
                SkinId = null,
                SkinVersion = null,
                SkinSettings = null
            };
        }, out updated);

    public static bool TryUpdateGeometry(
        LayoutProfile profile,
        string instanceId,
        LayoutGeometry geometry,
        out LayoutProfile updated) =>
        TryUpdateElement(profile, instanceId, element => element with { Geometry = geometry }, out updated);

    public static bool TryUpdateContainer(
        LayoutProfile profile,
        string instanceId,
        int proximityDip,
        LayoutContentAlignment contentAlignment,
        LayoutContentAlignment secondaryContentAlignment,
        LayoutAnimationSettings animation,
        out LayoutProfile updated)
    {
        var container = FindContainer(profile, instanceId);
        if (container is null)
        {
            updated = profile;
            return false;
        }

        updated = profile with
        {
            Containers = Replace(profile.Containers, container with
            {
                Orientation = LayoutFlowOrientation.Automatic,
                ContentAlignment = Enum.IsDefined(contentAlignment)
                    ? contentAlignment
                    : LayoutContentAlignment.Center,
                SecondaryContentAlignment = Enum.IsDefined(secondaryContentAlignment)
                    ? secondaryContentAlignment
                    : LayoutContentAlignment.Center,
                Trigger = container.ContainerKind == LayoutContainerKind.HoverSwitch
                    ? LayoutTriggerMode.PointerNear
                    : LayoutTriggerMode.Always,
                ProximityDip = Math.Clamp(proximityDip, 0, 256),
                Animation = animation
            })
        };
        return true;
    }

    public static bool TryResetContainer(
        LayoutProfile profile,
        string instanceId,
        out LayoutProfile updated)
    {
        var container = FindContainer(profile, instanceId);
        if (container is null)
        {
            updated = profile;
            return false;
        }

        var defaults = CreateContainer(container.ContainerKind);
        updated = profile with
        {
            Containers = Replace(profile.Containers, container with
            {
                // 恢复默认时清除手动尺寸覆盖；网格矩形与槽位内容保留。
                // Reset clears manual geometry overrides; the grid rectangle and slot contents are preserved.
                Geometry = LayoutGeometry.Auto,
                Orientation = LayoutFlowOrientation.Automatic,
                ContentAlignment = defaults.ContentAlignment,
                SecondaryContentAlignment = defaults.SecondaryContentAlignment,
                Trigger = defaults.Trigger,
                ProximityDip = defaults.ProximityDip,
                Animation = defaults.Animation
            })
        };
        return true;
    }

    public static bool TryUpdateCollapse(
        LayoutProfile profile,
        string instanceId,
        LayoutEdge attachmentSide,
        LayoutEdge? unavailableSide,
        int triggerThicknessDip,
        int proximityDip,
        LayoutAnimationSettings animation,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (unavailableSide == attachmentSide)
        {
            updated = profile;
            failure = LayoutEditFailure.EdgeUnavailable;
            return false;
        }

        var collapse = FindCollapse(profile, instanceId);
        if (collapse is null)
        {
            updated = profile;
            failure = LayoutEditFailure.ContainerNotFound;
            return false;
        }

        var next = collapse with
        {
            Attachment = collapse.Attachment with { AttachmentSide = attachmentSide },
            TriggerThicknessDip = Math.Clamp(triggerThicknessDip, 2, 24),
            ProximityDip = Math.Clamp(proximityDip, 0, 256),
            Animation = animation
        };
        updated = profile with
        {
            CollapseContainers = Replace(profile.CollapseContainers, next)
        };
        failure = LayoutEditFailure.None;
        return true;
    }

    public static bool TryResetCollapse(
        LayoutProfile profile,
        string instanceId,
        out LayoutProfile updated)
    {
        var collapse = FindCollapse(profile, instanceId);
        if (collapse is null)
        {
            updated = profile;
            return false;
        }

        updated = profile with
        {
            CollapseContainers = Replace(profile.CollapseContainers, collapse with
            {
                TriggerThicknessDip = 6,
                ProximityDip = 72,
                Animation = LayoutAnimationSettings.Default
            })
        };
        return true;
    }

    // ---------- 内部工具 ----------

    private static LayoutProfile RewriteContainerSlot(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutSlot slot)
    {
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

    private static LayoutWidgetElement? PlaceWidgetInSlot(
        LayoutProfile profile,
        LayoutSlot slot,
        LayoutWidgetElement widget,
        LayoutGridRect? ownerBounds,
        out LayoutEditFailure failure)
    {
        var (width, height) = MeasureWidgetCells(profile, widget);
        var rect = FindFreeWidgetRect(slot, ownerBounds, width, height);
        if (rect is null)
        {
            failure = LayoutEditFailure.InvalidPlacement;
            return null;
        }

        failure = LayoutEditFailure.None;
        return widget with { GridBounds = rect };
    }

    private static LayoutGridRect? FindFreeWidgetRect(
        LayoutSlot slot,
        LayoutGridRect? ownerBounds,
        int width,
        int height)
    {
        var maxWidth = ownerBounds is null ? Math.Max(width, 8) : Math.Max(width, ownerBounds.Width);
        var maxHeight = ownerBounds is null ? Math.Max(height, 8) : Math.Max(height, ownerBounds.Height);
        var occupied = slot.Children
            .OfType<LayoutWidgetElement>()
            .Where(child => child.GridBounds is not null)
            .Select(child => child.GridBounds!)
            .ToArray();

        for (var y = 0; y + height <= maxHeight; y++)
        {
            for (var x = 0; x + width <= maxWidth; x++)
            {
                var candidate = new LayoutGridRect(x, y, width, height);
                if (occupied.All(other => !candidate.Overlaps(other)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool TryFindFreePlacement(
        LayoutProfile profile,
        int width,
        int height,
        out LayoutGridRect rect)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        rect = default!;

        // 优先从当前占用联合体的尾部开始寻找，让新容器自然地接在现有排布之后。
        // Prefer placements after the current occupied union so new containers append naturally.
        var union = LayoutRuntimeService.CalculateBodyGridBounds(profile);
        if (union is not null)
        {
            var startY = Math.Max(0, union.Y);
            // 紧贴联合矩形右边缘寻找，避免 startX 留出空隙导致新容器与现有容器断图。
            // Start flush against the union's right edge so the new container shares a real edge.
            var startX = profile.LayoutMode == PlayerLayoutMode.Vertical ? union.X : union.Right;
            if (FindPlacementFrom(profile, startX, startY, width, height, out rect))
            {
                return true;
            }
        }

        return FindPlacementFrom(profile, 0, 0, width, height, out rect);
    }

    private static bool FindPlacementFrom(
        LayoutProfile profile,
        int startX,
        int startY,
        int width,
        int height,
        out LayoutGridRect rect)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        for (var y = startY; y + height <= grid.Rows; y++)
        {
            for (var x = startX; x + width <= grid.Columns; x++)
            {
                var candidate = new LayoutGridRect(x, y, width, height);
                if (LayoutGridConstraintService.CanPlaceContainer(profile, candidate))
                {
                    rect = candidate;
                    return true;
                }
            }
        }

        rect = default!;
        return false;
    }

    private static (int Width, int Height) ResolveDefaultContainerCells(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var widthDip = profile.LayoutMode == PlayerLayoutMode.Vertical ? 48 : 168;
        var heightDip = profile.LayoutMode == PlayerLayoutMode.Vertical ? 168 : 48;
        return (ToCells(widthDip, cell), ToCells(heightDip, cell));
    }

    private static (int Width, int Height) ResolveDefaultCollapseCells(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var thickness = ToCells(80, cell);
        var length = ToCells(120, cell);
        return (length, thickness);
    }

    private static LayoutGridRect ResolveCollapseRect(
        LayoutGridRect anchor,
        LayoutEdge attachmentSide,
        (int Width, int Height) size) =>
        attachmentSide switch
        {
            LayoutEdge.Top => new LayoutGridRect(anchor.X, anchor.Y - size.Height, size.Width, size.Height),
            LayoutEdge.Bottom => new LayoutGridRect(anchor.X, anchor.Bottom, size.Width, size.Height),
            LayoutEdge.Left => new LayoutGridRect(anchor.X - size.Width, anchor.Y, size.Width, size.Height),
            _ => new LayoutGridRect(anchor.Right, anchor.Y, size.Width, size.Height)
        };

    private static (int Width, int Height) MeasureWidgetCells(
        LayoutProfile profile,
        LayoutWidgetElement widget)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var vertical = profile.LayoutMode == PlayerLayoutMode.Vertical;
        double width;
        double height;
        switch (widget.TypeId)
        {
            case BuiltInWidgetTypeIds.Artwork:
                width = 40;
                height = 40;
                break;
            case BuiltInWidgetTypeIds.MediaText:
            case BuiltInWidgetTypeIds.MediaSource:
            {
                var text = widget.Settings as MediaTextWidgetSettings;
                var fontSize = Math.Clamp(text?.FontSizeDip ?? 14, 6, 72);
                var combined = text?.TextKind == MediaTextKind.TitleAndArtist;
                width = widget.Geometry?.WidthDip ?? (vertical ? 68 : combined ? 150 : 210);
                if (combined)
                {
                    var titleHeight = Math.Max(22, Math.Ceiling(fontSize * 1.25));
                    var artistHeight = Math.Max(18, Math.Ceiling(Math.Max(6, fontSize - 3) * 1.25));
                    height = widget.Geometry?.HeightDip ?? titleHeight + artistHeight;
                }
                else
                {
                    var lineHeight = Math.Max(12, Math.Ceiling(fontSize * 1.25));
                    var lines = Math.Clamp(text?.MaxLines ?? 1, 1, 2);
                    height = widget.Geometry?.HeightDip ?? Math.Max(40, lineHeight * lines);
                }
                break;
            }
            case BuiltInWidgetTypeIds.Command:
                var command = widget.Settings as CommandWidgetSettings;
                var buttonSize = Math.Clamp(
                    command?.ButtonSizeDip ?? CommandWidgetSettings.DefaultButtonSizeDip,
                    20,
                    96);
                width = buttonSize;
                height = buttonSize;
                break;
            case BuiltInWidgetTypeIds.Metrics:
                width = 74;
                height = 24;
                break;
            case BuiltInWidgetTypeIds.Spectrum:
                width = 88;
                height = 24;
                break;
            case BuiltInWidgetTypeIds.Separator:
                var separator = widget.Settings as SeparatorWidgetSettings;
                width = (separator?.ThicknessDip ?? 1) + 16;
                height = separator?.LengthDip ?? 22;
                break;
            default:
                width = 24;
                height = 24;
                break;
        }

        return (ToCells(width, cell), ToCells(height, cell));
    }

    private static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / cellSizeDip));

    private static bool IsInGrid(LayoutGridRect rect, LayoutGridSettings grid) =>
        rect.Width >= 1 &&
        rect.Height >= 1 &&
        rect.X >= 0 &&
        rect.Y >= 0 &&
        rect.Right <= grid.Columns &&
        rect.Bottom <= grid.Rows;

    private static bool ValidateEdit(
        LayoutProfile candidate,
        string instanceId,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        var errors = LayoutGridConstraintService.ValidateProfile(candidate);
        if (errors.Count == 0)
        {
            updated = candidate;
            failure = LayoutEditFailure.None;
            return true;
        }

        updated = candidate;
        failure = errors.FirstOrDefault(error =>
            string.Equals(error.InstanceId, instanceId, StringComparison.Ordinal)) is { } direct
            ? MapFailure(direct.Failure)
            : MapFailure(errors[0].Failure);
        return false;
    }

    private static LayoutEditFailure MapFailure(LayoutGridFailure failure) => failure switch
    {
        LayoutGridFailure.ContainerNotFound or
        LayoutGridFailure.MissingAnchor or
        LayoutGridFailure.MissingGridBounds or
        LayoutGridFailure.AnchorInUse or
        LayoutGridFailure.LastNonCollapseContainer => LayoutEditFailure.ContainerNotFound,
        LayoutGridFailure.DuplicateInstanceId => LayoutEditFailure.DuplicateInstanceId,
        LayoutGridFailure.WidgetNotAllowed => LayoutEditFailure.InteractiveNotAllowed,
        LayoutGridFailure.NotSupported => LayoutEditFailure.InvalidContainerKind,
        LayoutGridFailure.InvalidAttachmentSide or
        LayoutGridFailure.MultipleAttachmentSides => LayoutEditFailure.EdgeUnavailable,
        _ => LayoutEditFailure.InvalidPlacement
    };

    private static bool TryUpdateElement(
        LayoutProfile profile,
        string instanceId,
        Func<LayoutElement, LayoutElement> update,
        out LayoutProfile updated)
    {
        var state = new EditState();
        var containers = profile.Containers
            .Select(container => UpdateChild(container, instanceId, update, state))
            .ToArray();
        if (state.Changed)
        {
            updated = profile with { Containers = containers };
            return true;
        }

        var collapse = profile.CollapseContainers.Select(item =>
        {
            // 折叠容器自身不是 LayoutElement，不参与基于 LayoutElement 的属性更新；只更新展开槽位内的组件。
            // A collapse container is not a LayoutElement; only widgets inside its ExpandedSlot receive element updates.
            if (string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return item;
            }

            var children = item.ExpandedSlot.Children.Select(child =>
            {
                if (!string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return child;
                }

                var next = update(child);
                state.Changed = next != child;
                return next;
            }).ToArray();
            return state.Changed
                ? item with { ExpandedSlot = item.ExpandedSlot with { Children = children } }
                : item;
        }).ToArray();
        updated = state.Changed ? profile with { CollapseContainers = collapse } : profile;
        return state.Changed;
    }

    private static LayoutContainerElement UpdateChild(
        LayoutContainerElement container,
        string instanceId,
        Func<LayoutElement, LayoutElement> update,
        EditState state)
    {
        if (string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
        {
            var next = update(container);
            state.Changed = next != container;
            return next as LayoutContainerElement ?? container;
        }

        LayoutSlot Rewrite(LayoutSlot slot)
        {
            var children = slot.Children.Select(child =>
            {
                if (state.Changed)
                {
                    return child;
                }

                if (string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    var next = update(child);
                    state.Changed = next != child;
                    return next;
                }

                return child is LayoutContainerElement nested
                    ? UpdateChild(nested, instanceId, update, state)
                    : child;
            }).ToArray();
            return slot with { Children = children };
        }

        return container with
        {
            PrimarySlot = Rewrite(container.PrimarySlot),
            SecondarySlot = Rewrite(container.SecondarySlot)
        };
    }

    private static (string ContainerId, LayoutSlotKind SlotKind, LayoutSlot Slot)? FindWidgetSlot(
        LayoutProfile profile,
        string instanceId)
    {
        foreach (var container in profile.Containers)
        {
            if (container.PrimarySlot.Children.Any(child =>
                    string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                return (container.InstanceId, LayoutSlotKind.Primary, container.PrimarySlot);
            }

            if (container.SecondarySlot.Children.Any(child =>
                    string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                return (container.InstanceId, LayoutSlotKind.Secondary, container.SecondarySlot);
            }
        }

        foreach (var collapse in profile.CollapseContainers)
        {
            if (collapse.ExpandedSlot.Children.Any(child =>
                    string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                return (collapse.InstanceId, LayoutSlotKind.Expanded, collapse.ExpandedSlot);
            }
        }

        return null;
    }

    private static bool TryMoveList<T>(
        IReadOnlyList<T> source,
        string instanceId,
        int offset,
        out IReadOnlyList<T> updated)
    {
        var items = source.ToArray();
        var index = Array.FindIndex(items, item => GetInstanceId(item) == instanceId);
        var target = index + Math.Sign(offset);
        if (index < 0 || target < 0 || target >= items.Length)
        {
            updated = source;
            return false;
        }

        (items[index], items[target]) = (items[target], items[index]);
        updated = items;
        return true;
    }

    private static bool TryReorderList<T>(
        IReadOnlyList<T> source,
        string sourceId,
        string targetId,
        out IReadOnlyList<T> updated)
    {
        var items = source.ToList();
        var sourceIndex = items.FindIndex(item => GetInstanceId(item) == sourceId);
        var targetIndex = items.FindIndex(item => GetInstanceId(item) == targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            updated = source;
            return false;
        }

        var item = items[sourceIndex];
        items.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }
        items.Insert(targetIndex, item);
        updated = items.ToArray();
        return true;
    }

    private static string? GetInstanceId<T>(T item) => item switch
    {
        LayoutElement element => element.InstanceId,
        LayoutCollapseContainer collapse => collapse.InstanceId,
        _ => null
    };

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

    private sealed class EditState
    {
        public bool Changed { get; set; }
    }
}
