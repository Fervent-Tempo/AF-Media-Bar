using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal enum LayoutSlotKind
{
    Primary = 0,
    Secondary = 1,
    Expanded = 2
}

internal enum LayoutEditFailure
{
    None = 0,
    ContainerNotFound = 1,
    DuplicateInstanceId = 2,
    InteractiveNotAllowed = 3,
    UnsupportedWidget = 4,
    EdgeUnavailable = 5,
    InvalidContainerKind = 6
}

/// <summary>
/// 以不可变快照编辑长条容器和边缘容器，并集中执行放置及交互能力约束。
/// Edits strip and edge containers as immutable snapshots while centralizing placement and interaction-capability constraints.
/// </summary>
internal static class LayoutEditorService
{
    internal static LayoutContainerElement CreateInlineContainer(LayoutContainerKind kind)
    {
        var normalizedKind = kind == LayoutContainerKind.HoverSwitch
            ? LayoutContainerKind.HoverSwitch
            : LayoutContainerKind.Static;
        return new LayoutContainerElement(
            $"inline-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            normalizedKind,
            LayoutFlowOrientation.Automatic,
            normalizedKind == LayoutContainerKind.HoverSwitch
                ? LayoutTriggerMode.PointerNear
                : LayoutTriggerMode.Always,
            48,
            normalizedKind == LayoutContainerKind.HoverSwitch
                ? LayoutAnimationSettings.Default
                : new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            LayoutSlot.Empty(normalizedKind == LayoutContainerKind.HoverSwitch ? "leave" : "content"),
            LayoutSlot.Empty(normalizedKind == LayoutContainerKind.HoverSwitch ? "near" : "unused"),
            LayoutSlot.Empty("legacy-collapsed"));
    }

    internal static LayoutEdgeContainer CreateEdgeContainer(LayoutEdge edge)
    {
        return new LayoutEdgeContainer(
            $"edge-{Guid.NewGuid():N}",
            true,
            edge,
            0,
            6,
            72,
            LayoutAnimationSettings.Default,
            LayoutSlot.Empty("expanded"));
    }

    internal static bool TryAddInlineContainer(
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

        var container = CreateInlineContainer(kind);
        updated = profile with
        {
            InlineContainers = profile.InlineContainers.Append(container).ToArray()
        };
        failure = LayoutEditFailure.None;
        return true;
    }

    internal static bool TryAddEdgeContainer(
        LayoutProfile profile,
        LayoutEdge edge,
        LayoutEdge? unavailableEdge,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (profile.HostMode == WindowHostMode.Taskbar && unavailableEdge == edge)
        {
            updated = profile;
            failure = LayoutEditFailure.EdgeUnavailable;
            return false;
        }

        updated = profile with
        {
            EdgeContainers = profile.EdgeContainers.Append(CreateEdgeContainer(edge)).ToArray()
        };
        failure = LayoutEditFailure.None;
        return true;
    }

    internal static bool TryAddWidget(
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

        var changed = false;
        var inlineFailure = LayoutEditFailure.None;
        var inline = new LayoutContainerElement[profile.InlineContainers.Count];
        for (var index = 0; index < profile.InlineContainers.Count; index++)
        {
            inline[index] = RewriteContainer(
                profile.InlineContainers[index],
                containerId,
                slotKind,
                widget,
                ref changed,
                out var candidateFailure);
            if (candidateFailure != LayoutEditFailure.None)
            {
                inlineFailure = candidateFailure;
            }
        }
        if (changed)
        {
            updated = profile with { InlineContainers = inline };
            failure = LayoutEditFailure.None;
            return true;
        }

        var edgeFailure = LayoutEditFailure.None;
        var edges = profile.EdgeContainers.Select(edge =>
        {
            if (!string.Equals(edge.InstanceId, containerId, StringComparison.Ordinal))
            {
                return edge;
            }

            if (slotKind != LayoutSlotKind.Expanded)
            {
                edgeFailure = LayoutEditFailure.ContainerNotFound;
                return edge;
            }

            changed = true;
            return edge with
            {
                ExpandedSlot = edge.ExpandedSlot with
                {
                    Children = edge.ExpandedSlot.Children.Append(widget).ToArray()
                }
            };
        }).ToArray();
        updated = changed ? profile with { EdgeContainers = edges } : profile;
        failure = changed
            ? LayoutEditFailure.None
            : inlineFailure != LayoutEditFailure.None
                ? inlineFailure
                : edgeFailure == LayoutEditFailure.None
                    ? LayoutEditFailure.ContainerNotFound
                    : edgeFailure;
        return changed;
    }

    internal static bool TryRelocateWidget(
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

    internal static bool TryRemove(LayoutProfile profile, string instanceId, out LayoutProfile updated)
    {
        var inline = profile.InlineContainers.ToList();
        var inlineIndex = inline.FindIndex(container =>
            string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal));
        if (inlineIndex >= 0)
        {
            inline.RemoveAt(inlineIndex);
            updated = profile with { InlineContainers = inline.ToArray() };
            return true;
        }

        var edges = profile.EdgeContainers.ToList();
        var edgeIndex = edges.FindIndex(container =>
            string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal));
        if (edgeIndex >= 0)
        {
            edges.RemoveAt(edgeIndex);
            updated = profile with { EdgeContainers = edges.ToArray() };
            return true;
        }

        var state = new EditState();
        var rewrittenInline = inline.Select(container => RemoveChild(container, instanceId, state)).ToArray();
        if (state.Changed)
        {
            updated = profile with { InlineContainers = rewrittenInline };
            return true;
        }

        var rewrittenEdges = edges.Select(edge =>
        {
            if (state.Changed || !edge.ExpandedSlot.Children.Any(child =>
                    string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                return edge;
            }

            state.Changed = true;
            return edge with
            {
                ExpandedSlot = edge.ExpandedSlot with
                {
                    Children = edge.ExpandedSlot.Children
                        .Where(child => !string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                        .ToArray()
                }
            };
        }).ToArray();
        updated = state.Changed ? profile with { EdgeContainers = rewrittenEdges } : profile;
        return state.Changed;
    }

    internal static bool TryMove(LayoutProfile profile, string instanceId, int offset, out LayoutProfile updated)
    {
        if (offset == 0)
        {
            updated = profile;
            return false;
        }

        if (TryMoveList(profile.InlineContainers, instanceId, offset, out var inline))
        {
            updated = profile with { InlineContainers = inline };
            return true;
        }

        if (TryMoveList(profile.EdgeContainers, instanceId, offset, out var edges))
        {
            updated = profile with { EdgeContainers = edges };
            return true;
        }

        var changed = false;
        var rewrittenInline = profile.InlineContainers
            .Select(container => MoveChild(container, instanceId, offset, ref changed))
            .ToArray();
        if (changed)
        {
            updated = profile with { InlineContainers = rewrittenInline };
            return true;
        }

        var rewrittenEdges = profile.EdgeContainers.Select(edge =>
        {
            if (changed || !TryMoveList(edge.ExpandedSlot.Children, instanceId, offset, out var children))
            {
                return edge;
            }

            changed = true;
            return edge with { ExpandedSlot = edge.ExpandedSlot with { Children = children } };
        }).ToArray();
        updated = changed ? profile with { EdgeContainers = rewrittenEdges } : profile;
        return changed;
    }

    internal static bool TryReorderTopLevel(
        LayoutProfile profile,
        string sourceId,
        string targetId,
        out LayoutProfile updated)
    {
        if (TryReorderList(profile.InlineContainers, sourceId, targetId, out var inline))
        {
            updated = profile with { InlineContainers = inline };
            return true;
        }

        if (TryReorderList(profile.EdgeContainers, sourceId, targetId, out var edges))
        {
            updated = profile with { EdgeContainers = edges };
            return true;
        }

        updated = profile;
        return false;
    }

    internal static bool TrySetEnabled(LayoutProfile profile, string instanceId, bool enabled, out LayoutProfile updated)
    {
        var state = new EditState();
        var inline = profile.InlineContainers.Select(container =>
        {
            if (string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
            {
                state.Changed = container.Enabled != enabled;
                return container with { Enabled = enabled };
            }

            return SetChildEnabled(container, instanceId, enabled, state);
        }).ToArray();
        if (state.Changed)
        {
            updated = profile with { InlineContainers = inline };
            if (!IsProfileCapabilityValid(updated))
            {
                updated = profile;
                return false;
            }
            return true;
        }

        var edges = profile.EdgeContainers.Select(edge =>
        {
            if (string.Equals(edge.InstanceId, instanceId, StringComparison.Ordinal))
            {
                state.Changed = edge.Enabled != enabled;
                return edge with { Enabled = enabled };
            }

            var children = edge.ExpandedSlot.Children.Select(child =>
            {
                if (string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    state.Changed = child.Enabled != enabled;
                    return child with { Enabled = enabled };
                }

                return child;
            }).ToArray();
            return state.Changed ? edge with { ExpandedSlot = edge.ExpandedSlot with { Children = children } } : edge;
        }).ToArray();
        updated = state.Changed ? profile with { EdgeContainers = edges } : profile;
        if (state.Changed && !IsProfileCapabilityValid(updated))
        {
            updated = profile;
            return false;
        }
        return state.Changed;
    }

    internal static bool TryUpdateWidgetSettings(
        LayoutProfile profile,
        string instanceId,
        WidgetSettings settings,
        out LayoutProfile updated)
    {
        return TryUpdateElement(profile, instanceId, element =>
            element is LayoutWidgetElement widget ? widget with { Settings = settings } : element, out updated);
    }

    internal static bool TryUpdateGeometry(
        LayoutProfile profile,
        string instanceId,
        LayoutGeometry geometry,
        out LayoutProfile updated)
    {
        return TryUpdateElement(profile, instanceId, element => element with { Geometry = geometry }, out updated);
    }

    internal static bool TryUpdateInlineContainer(
        LayoutProfile profile,
        string instanceId,
        int proximityDip,
        LayoutAnimationSettings animation,
        out LayoutProfile updated)
    {
        var changed = false;
        var inline = profile.InlineContainers.Select(container =>
        {
            if (!string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return container;
            }

            changed = true;
            return container with
            {
                Orientation = LayoutFlowOrientation.Automatic,
                Trigger = container.ContainerKind == LayoutContainerKind.HoverSwitch
                    ? LayoutTriggerMode.PointerNear
                    : LayoutTriggerMode.Always,
                ProximityDip = Math.Clamp(proximityDip, 0, 256),
                Animation = animation
            };
        }).ToArray();
        updated = changed ? profile with { InlineContainers = inline } : profile;
        return changed;
    }

    internal static bool TryUpdateEdgeContainer(
        LayoutProfile profile,
        string instanceId,
        LayoutEdge edge,
        LayoutEdge? unavailableEdge,
        int offsetDip,
        int triggerThicknessDip,
        int proximityDip,
        LayoutAnimationSettings animation,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        if (profile.HostMode == WindowHostMode.Taskbar && unavailableEdge == edge)
        {
            updated = profile;
            failure = LayoutEditFailure.EdgeUnavailable;
            return false;
        }

        var changed = false;
        var edges = profile.EdgeContainers.Select(container =>
        {
            if (!string.Equals(container.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return container;
            }

            changed = true;
            return container with
            {
                Edge = edge,
                OffsetDip = Math.Clamp(offsetDip, -2_000, 2_000),
                TriggerThicknessDip = Math.Clamp(triggerThicknessDip, 2, 24),
                ProximityDip = Math.Clamp(proximityDip, 0, 256),
                Animation = animation
            };
        }).ToArray();
        updated = changed ? profile with { EdgeContainers = edges } : profile;
        failure = changed ? LayoutEditFailure.None : LayoutEditFailure.ContainerNotFound;
        return changed;
    }

    internal static object? Find(LayoutProfile profile, string instanceId)
    {
        foreach (var container in profile.InlineContainers)
        {
            if (Find(container, instanceId) is { } match)
            {
                return match;
            }
        }

        foreach (var edge in profile.EdgeContainers)
        {
            if (string.Equals(edge.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return edge;
            }

            var child = edge.ExpandedSlot.Children.FirstOrDefault(item =>
                string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    internal static LayoutElement? Find(LayoutContainerElement container, string instanceId)
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

    private static LayoutContainerElement RewriteContainer(
        LayoutContainerElement container,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutWidgetElement widget,
        ref bool changed,
        out LayoutEditFailure failure)
    {
        failure = LayoutEditFailure.None;
        if (!string.Equals(container.InstanceId, containerId, StringComparison.Ordinal))
        {
            return container;
        }

        if (slotKind == LayoutSlotKind.Expanded ||
            container.ContainerKind == LayoutContainerKind.Static && slotKind == LayoutSlotKind.Secondary)
        {
            failure = LayoutEditFailure.ContainerNotFound;
            return container;
        }

        if (slotKind == LayoutSlotKind.Primary &&
            container.ContainerKind == LayoutContainerKind.HoverSwitch &&
            ContainsInteractiveElement(widget))
        {
            failure = LayoutEditFailure.InteractiveNotAllowed;
            return container;
        }

        changed = true;
        return slotKind == LayoutSlotKind.Secondary
            ? container with
            {
                SecondarySlot = container.SecondarySlot with
                {
                    Children = container.SecondarySlot.Children.Append(widget).ToArray()
                }
            }
            : container with
            {
                PrimarySlot = container.PrimarySlot with
                {
                    Children = container.PrimarySlot.Children.Append(widget).ToArray()
                }
            };
    }

    private static bool TryUpdateElement(
        LayoutProfile profile,
        string instanceId,
        Func<LayoutElement, LayoutElement> update,
        out LayoutProfile updated)
    {
        var state = new EditState();
        var inline = profile.InlineContainers
            .Select(container => UpdateChild(container, instanceId, update, state))
            .ToArray();
        if (state.Changed)
        {
            updated = profile with { InlineContainers = inline };
            return true;
        }

        var edges = profile.EdgeContainers.Select(edge =>
        {
            var children = edge.ExpandedSlot.Children.Select(child =>
            {
                if (!string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return child;
                }

                var next = update(child);
                state.Changed = next != child;
                return next;
            }).ToArray();
            return state.Changed ? edge with { ExpandedSlot = edge.ExpandedSlot with { Children = children } } : edge;
        }).ToArray();
        updated = state.Changed ? profile with { EdgeContainers = edges } : profile;
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

    private static LayoutContainerElement RemoveChild(
        LayoutContainerElement container,
        string instanceId,
        EditState state)
    {
        LayoutSlot Rewrite(LayoutSlot slot)
        {
            if (slot.Children.Any(child => string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                state.Changed = true;
                return slot with
                {
                    Children = slot.Children.Where(child =>
                        !string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal)).ToArray()
                };
            }

            return slot;
        }

        return container with
        {
            PrimarySlot = Rewrite(container.PrimarySlot),
            SecondarySlot = Rewrite(container.SecondarySlot)
        };
    }

    private static LayoutContainerElement MoveChild(
        LayoutContainerElement container,
        string instanceId,
        int offset,
        ref bool changed)
    {
        if (TryMoveList(container.PrimarySlot.Children, instanceId, offset, out var primary))
        {
            changed = true;
            return container with { PrimarySlot = container.PrimarySlot with { Children = primary } };
        }

        if (TryMoveList(container.SecondarySlot.Children, instanceId, offset, out var secondary))
        {
            changed = true;
            return container with { SecondarySlot = container.SecondarySlot with { Children = secondary } };
        }

        return container;
    }

    private static LayoutContainerElement SetChildEnabled(
        LayoutContainerElement container,
        string instanceId,
        bool enabled,
        EditState state)
    {
        LayoutSlot Rewrite(LayoutSlot slot)
        {
            var children = slot.Children.Select(child =>
            {
                if (!string.Equals(child.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return child;
                }

                state.Changed = child.Enabled != enabled;
                return child with { Enabled = enabled };
            }).ToArray();
            return slot with { Children = children };
        }

        return container with
        {
            PrimarySlot = Rewrite(container.PrimarySlot),
            SecondarySlot = Rewrite(container.SecondarySlot)
        };
    }

    private static bool TryMoveList<T>(
        IReadOnlyList<T> source,
        string instanceId,
        int offset,
        out IReadOnlyList<T> updated)
    {
        var items = source.ToArray();
        var index = Array.FindIndex(items, item => item switch
        {
            LayoutElement element => string.Equals(element.InstanceId, instanceId, StringComparison.Ordinal),
            LayoutEdgeContainer edge => string.Equals(edge.InstanceId, instanceId, StringComparison.Ordinal),
            _ => false
        });
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
        LayoutEdgeContainer edge => edge.InstanceId,
        _ => null
    };

    private static bool ContainsInteractiveElement(LayoutElement element)
    {
        if (element is LayoutWidgetElement widget)
        {
            return widget.Enabled &&
                ComponentCatalog.TryGet(widget.TypeId, out var definition) &&
                definition.Capabilities.HasFlag(WidgetCapabilities.Interactive);
        }

        return element is LayoutContainerElement container &&
            container.PrimarySlot.Children.Concat(container.SecondarySlot.Children)
                .Any(ContainsInteractiveElement);
    }

    private static bool IsProfileCapabilityValid(LayoutProfile profile)
    {
        foreach (var container in profile.InlineContainers)
        {
            if (!IsContainerCapabilityValid(container))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsContainerCapabilityValid(LayoutContainerElement container)
    {
        if (container.ContainerKind == LayoutContainerKind.HoverSwitch &&
            container.PrimarySlot.Children.Any(ContainsInteractiveElement))
        {
            return false;
        }

        return container.PrimarySlot.Children
            .Concat(container.SecondarySlot.Children)
            .OfType<LayoutContainerElement>()
            .All(IsContainerCapabilityValid);
    }

    private sealed class EditState
    {
        internal bool Changed { get; set; }
    }
}
