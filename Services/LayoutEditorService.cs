using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal enum LayoutSlotKind
{
    Primary = 0,
    Secondary = 1,
    Collapsed = 2
}

internal enum LayoutEditFailure
{
    None = 0,
    ContainerNotFound = 1,
    DuplicateInstanceId = 2,
    InteractiveNotAllowed = 3,
    UnsupportedWidget = 4
}

/// <summary>
/// 以不可变记录编辑布局树，集中处理插入、删除、移动和交互能力校验。
/// Edits the immutable layout tree and centralizes insertion, removal, reordering, and interaction-capability validation.
/// </summary>
internal static class LayoutEditorService
{
    internal static bool TryAdd(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutElement element,
        out LayoutProfile updated)
    {
        return TryAdd(profile, containerId, slotKind, element, out updated, out _);
    }

    internal static bool TryAdd(
        LayoutProfile profile,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutElement element,
        out LayoutProfile updated,
        out LayoutEditFailure failure)
    {
        var state = new EditState();
        failure = LayoutEditFailure.None;
        if (Find(profile.Root, element.InstanceId) is not null)
        {
            updated = profile;
            failure = LayoutEditFailure.DuplicateInstanceId;
            return false;
        }

        if (element is LayoutWidgetElement widget &&
            !ComponentCatalog.TryGet(widget.TypeId, out _))
        {
            updated = profile;
            failure = LayoutEditFailure.UnsupportedWidget;
            return false;
        }

        updated = profile with
        {
            Root = AddToContainer(profile.Root, containerId, slotKind, element, state)
        };
        if (!state.Changed && state.Failure == LayoutEditFailure.None)
        {
            state.Failure = LayoutEditFailure.ContainerNotFound;
        }
        if (state.Changed && !IsContainerCapabilityValid(updated.Root, parentAllowsInteractive: true))
        {
            updated = profile;
            state.Changed = false;
            state.Failure = LayoutEditFailure.InteractiveNotAllowed;
        }
        failure = state.Failure;
        return state.Changed;
    }

    internal static bool TryUpdateGeometry(
        LayoutProfile profile,
        string elementId,
        LayoutGeometry geometry,
        out LayoutProfile updated)
    {
        var state = new EditState();
        updated = profile with
        {
            Root = UpdateElement(profile.Root, elementId, element =>
            {
                state.Changed = true;
                return element with { Geometry = geometry };
            }, state)
        };
        return state.Changed;
    }

    internal static bool TryUpdateWidgetSettings(
        LayoutProfile profile,
        string elementId,
        WidgetSettings settings,
        out LayoutProfile updated)
    {
        var state = new EditState();
        updated = profile with
        {
            Root = UpdateElement(profile.Root, elementId, element =>
            {
                if (element is not LayoutWidgetElement widget)
                {
                    return element;
                }

                state.Changed = true;
                return widget with { Settings = settings };
            }, state)
        };
        return state.Changed;
    }

    internal static bool TryUpdateContainerSettings(
        LayoutProfile profile,
        string elementId,
        LayoutContainerKind kind,
        LayoutFlowOrientation orientation,
        LayoutTriggerMode trigger,
        int proximityDip,
        LayoutAnimationSettings animation,
        out LayoutProfile updated)
    {
        var state = new EditState();
        updated = profile with
        {
            Root = UpdateElement(profile.Root, elementId, element =>
            {
                if (element is not LayoutContainerElement container)
                {
                    return element;
                }

                var candidate = container with
                {
                    ContainerKind = kind,
                    Orientation = orientation,
                    Trigger = trigger,
                    ProximityDip = proximityDip,
                    Animation = animation
                };
                if (!candidate.PrimarySlot.Children.All(child =>
                        CanAddToSlot(candidate, LayoutSlotKind.Primary, child)) ||
                    !candidate.CollapsedSlot.Children.All(child =>
                        CanAddToSlot(candidate, LayoutSlotKind.Collapsed, child)))
                {
                    return element;
                }

                state.Changed = true;
                return candidate;
            }, state)
        };
        return state.Changed;
    }

    internal static bool TryRemove(
        LayoutProfile profile,
        string elementId,
        out LayoutProfile updated)
    {
        var state = new EditState();
        updated = profile with
        {
            Root = RemoveFromContainer(profile.Root, elementId, state)
        };
        return state.Changed;
    }

    internal static bool TryMove(
        LayoutProfile profile,
        string elementId,
        int offset,
        out LayoutProfile updated)
    {
        if (offset == 0)
        {
            updated = profile;
            return false;
        }

        var state = new EditState();
        updated = profile with
        {
            Root = MoveInContainer(profile.Root, elementId, offset, state)
        };
        return state.Changed;
    }

    internal static bool TrySetEnabled(
        LayoutProfile profile,
        string elementId,
        bool enabled,
        out LayoutProfile updated)
    {
        var state = new EditState();
        updated = profile with
        {
            Root = SetEnabledInContainer(profile.Root, elementId, enabled, state)
        };
        if (state.Changed && !IsContainerCapabilityValid(updated.Root, parentAllowsInteractive: true))
        {
            updated = profile;
            return false;
        }
        return state.Changed;
    }

    internal static LayoutElement? Find(
        LayoutContainerElement container,
        string elementId)
    {
        if (string.Equals(container.InstanceId, elementId, StringComparison.Ordinal))
        {
            return container;
        }

        foreach (var slot in GetSlots(container))
        {
            foreach (var child in slot.Children)
            {
                if (string.Equals(child.InstanceId, elementId, StringComparison.Ordinal))
                {
                    return child;
                }

                if (child is LayoutContainerElement nested)
                {
                    var result = Find(nested, elementId);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    private static LayoutContainerElement AddToContainer(
        LayoutContainerElement container,
        string containerId,
        LayoutSlotKind slotKind,
        LayoutElement element,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        if (string.Equals(container.InstanceId, containerId, StringComparison.Ordinal))
        {
            if (!CanAddToSlot(container, slotKind, element))
            {
                state.Failure = LayoutEditFailure.InteractiveNotAllowed;
                return container;
            }

            state.Changed = true;
            return WithSlot(
                container,
                slotKind,
                GetSlot(container, slotKind) with
                {
                    Children = GetSlot(container, slotKind).Children.Append(element).ToArray()
                });
        }

        return RewriteChildren(
            container,
            child => child is LayoutContainerElement nested
                 ? AddToContainer(nested, containerId, slotKind, element, state)
                : child);
    }

    private static LayoutContainerElement UpdateElement(
        LayoutContainerElement container,
        string elementId,
        Func<LayoutElement, LayoutElement> update,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        if (string.Equals(container.InstanceId, elementId, StringComparison.Ordinal))
        {
            var next = update(container);
            if (next is LayoutContainerElement updatedContainer && next != container)
            {
                state.Changed = true;
                return updatedContainer;
            }

            return container;
        }

        return RewriteSlots(container, slot =>
        {
            if (state.Changed)
            {
                return slot;
            }

            var children = slot.Children.ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (string.Equals(child.InstanceId, elementId, StringComparison.Ordinal))
                {
                    var next = update(child);
                    if (next != child)
                    {
                        children[index] = next;
                        return slot with { Children = children };
                    }

                    return slot;
                }

                if (child is LayoutContainerElement nested)
                {
                    var next = UpdateElement(nested, elementId, update, state);
                    if (state.Changed)
                    {
                        children[index] = next;
                        return slot with { Children = children };
                    }
                }
            }

            return slot;
        });
    }

    private static LayoutContainerElement RemoveFromContainer(
        LayoutContainerElement container,
        string elementId,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        return RewriteSlots(
            container,
            slot =>
            {
                if (state.Changed)
                {
                    return slot;
                }

                if (slot.Children.Any(child =>
                        string.Equals(child.InstanceId, elementId, StringComparison.Ordinal)))
                {
                    state.Changed = true;
                    return slot with
                    {
                        Children = slot.Children
                            .Where(child => !string.Equals(
                                child.InstanceId,
                                elementId,
                                StringComparison.Ordinal))
                            .ToArray()
                    };
                }

                return slot with
                {
                    Children = slot.Children
                        .Select(child => child is LayoutContainerElement nested
                             ? RemoveFromContainer(nested, elementId, state)
                            : child)
                        .ToArray()
                };
            });
    }

    private static LayoutContainerElement MoveInContainer(
        LayoutContainerElement container,
        string elementId,
        int offset,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        return RewriteSlots(
            container,
            slot =>
            {
                if (state.Changed)
                {
                    return slot;
                }

                var index = -1;
                for (var childIndex = 0; childIndex < slot.Children.Count; childIndex++)
                {
                    if (string.Equals(
                            slot.Children[childIndex].InstanceId,
                            elementId,
                            StringComparison.Ordinal))
                    {
                        index = childIndex;
                        break;
                    }
                }
                if (index < 0 || index >= slot.Children.Count)
                {
                    return slot;
                }

                var targetIndex = index + Math.Sign(offset);
                if (targetIndex < 0 || targetIndex >= slot.Children.Count)
                {
                    return slot;
                }

                var children = slot.Children.ToArray();
                (children[index], children[targetIndex]) =
                    (children[targetIndex], children[index]);
                state.Changed = true;
                return slot with { Children = children };
            });
    }

    private static LayoutContainerElement SetEnabledInContainer(
        LayoutContainerElement container,
        string elementId,
        bool enabled,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        return RewriteSlots(
            container,
            slot =>
            {
                if (state.Changed)
                {
                    return slot;
                }

                var children = slot.Children.ToArray();
                for (var index = 0; index < children.Length; index++)
                {
                    var child = children[index];
                    if (string.Equals(child.InstanceId, elementId, StringComparison.Ordinal))
                    {
                        if (child.Enabled == enabled)
                        {
                            return slot;
                        }

                        children[index] = child with { Enabled = enabled };
                        state.Changed = true;
                        return slot with { Children = children };
                    }

                    if (child is LayoutContainerElement nested)
                    {
                        children[index] = SetEnabledInContainer(
                            nested,
                            elementId,
                            enabled,
                            state);
                        if (state.Changed)
                        {
                            return slot with { Children = children };
                        }
                    }
                }

                return slot;
            });
    }

    private static LayoutContainerElement RewriteChildren(
        LayoutContainerElement container,
        Func<LayoutElement, LayoutElement> rewrite)
    {
        return RewriteSlots(
            container,
            slot => slot with
            {
                Children = slot.Children.Select(rewrite).ToArray()
            });
    }

    private static LayoutContainerElement RewriteSlots(
        LayoutContainerElement container,
        Func<LayoutSlot, LayoutSlot> rewrite)
    {
        return container with
        {
            PrimarySlot = rewrite(container.PrimarySlot),
            SecondarySlot = rewrite(container.SecondarySlot),
            CollapsedSlot = rewrite(container.CollapsedSlot)
        };
    }

    private static LayoutSlot GetSlot(
        LayoutContainerElement container,
        LayoutSlotKind slotKind)
    {
        return slotKind switch
        {
            LayoutSlotKind.Primary => container.PrimarySlot,
            LayoutSlotKind.Secondary => container.SecondarySlot,
            LayoutSlotKind.Collapsed => container.CollapsedSlot,
            _ => container.PrimarySlot
        };
    }

    private static LayoutContainerElement WithSlot(
        LayoutContainerElement container,
        LayoutSlotKind slotKind,
        LayoutSlot slot)
    {
        return slotKind switch
        {
            LayoutSlotKind.Primary => container with { PrimarySlot = slot },
            LayoutSlotKind.Secondary => container with { SecondarySlot = slot },
            LayoutSlotKind.Collapsed => container with { CollapsedSlot = slot },
            _ => container
        };
    }

    private static IEnumerable<LayoutSlot> GetSlots(LayoutContainerElement container)
    {
        yield return container.PrimarySlot;
        yield return container.SecondarySlot;
        yield return container.CollapsedSlot;
    }

    private static bool ContainsInteractiveElement(LayoutElement element)
    {
        if (element is LayoutWidgetElement widget)
        {
            return widget.Enabled &&
                ComponentCatalog.TryGet(widget.TypeId, out var definition) &&
                definition.Capabilities.HasFlag(WidgetCapabilities.Interactive);
        }

        return element is LayoutContainerElement { Enabled: true } container &&
            GetSlots(container)
                .SelectMany(slot => slot.Children)
                .Any(ContainsInteractiveElement);
    }

    private static bool CanAddToSlot(
        LayoutContainerElement container,
        LayoutSlotKind slotKind,
        LayoutElement element)
    {
        if (!ContainsInteractiveElement(element))
        {
            return true;
        }

        // 离开状态必须是可观察但不可操作的界面，避免用户把隐藏期间的点击命令暴露出来。
        // Leave-state surfaces must remain observable but non-interactive so hidden-state clicks cannot invoke commands.
        if (slotKind == LayoutSlotKind.Collapsed ||
            (container.ContainerKind == LayoutContainerKind.HoverSwitch &&
                slotKind == LayoutSlotKind.Primary) ||
            (container.ContainerKind == LayoutContainerKind.AutoCollapse &&
                slotKind == LayoutSlotKind.Collapsed))
        {
            return false;
        }

        if (element is LayoutWidgetElement widget &&
            ComponentCatalog.TryGet(widget.TypeId, out var definition))
        {
            return slotKind != LayoutSlotKind.Collapsed || definition.SupportsCollapsedSlot;
        }

        return true;
    }

    private static bool IsContainerCapabilityValid(
        LayoutContainerElement container,
        bool parentAllowsInteractive)
    {
        var primaryAllowsInteractive = parentAllowsInteractive &&
            container.ContainerKind != LayoutContainerKind.HoverSwitch;
        return IsSlotCapabilityValid(container.PrimarySlot, primaryAllowsInteractive) &&
            IsSlotCapabilityValid(container.SecondarySlot, parentAllowsInteractive) &&
            IsSlotCapabilityValid(container.CollapsedSlot, allowInteractive: false);
    }

    private static bool IsSlotCapabilityValid(LayoutSlot slot, bool allowInteractive)
    {
        foreach (var child in slot.Children)
        {
            if (child is LayoutWidgetElement widget)
            {
                if (widget.Enabled && !allowInteractive &&
                    ComponentCatalog.TryGet(widget.TypeId, out var definition) &&
                    definition.Capabilities.HasFlag(WidgetCapabilities.Interactive))
                {
                    return false;
                }
            }
            else if (child is LayoutContainerElement { Enabled: true } container &&
                !IsContainerCapabilityValid(container, allowInteractive))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class EditState
    {
        internal bool Changed { get; set; }
        internal LayoutEditFailure Failure { get; set; }
    }
}
