using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Services;

namespace AFMediaBar.Layout.Editing;

/// <summary>
/// Pure, immutable edits for widget and container properties. Skin assignment
/// remains in the WPF/Core adapter because it depends on the skin catalog.
/// </summary>
public static class LayoutPropertyEditService
{
    public static bool TryUpdateWidgetSettings(
        LayoutProfile profile,
        string instanceId,
        WidgetSettings settings,
        out LayoutProfile updated) =>
        TryUpdateElement(profile, instanceId,
            element => element is LayoutWidgetElement widget
                ? widget with { Settings = settings }
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

            var defaults = LayoutComponentCatalog.CreateDefaultSettings(widget.TypeId);
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
                ContentAlignment = Enum.IsDefined(contentAlignment) ? contentAlignment : LayoutContentAlignment.Center,
                SecondaryContentAlignment = Enum.IsDefined(secondaryContentAlignment) ? secondaryContentAlignment : LayoutContentAlignment.Center,
                Trigger = container.ContainerKind == LayoutContainerKind.HoverSwitch ? LayoutTriggerMode.PointerNear : LayoutTriggerMode.Always,
                ProximityDip = Math.Clamp(proximityDip, 0, 256),
                Animation = animation
            })
        };
        return true;
    }

    public static bool TryResetContainer(LayoutProfile profile, string instanceId, out LayoutProfile updated)
    {
        var container = FindContainer(profile, instanceId);
        if (container is null)
        {
            updated = profile;
            return false;
        }

        var defaults = LayoutGridConstraintService.CreateContainer(container.ContainerKind);
        updated = profile with
        {
            Containers = Replace(profile.Containers, container with
            {
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
        out LayoutProfile updated)
    {
        if (unavailableSide == attachmentSide || FindCollapse(profile, instanceId) is not { } collapse)
        {
            updated = profile;
            return false;
        }

        updated = profile with
        {
            CollapseContainers = Replace(profile.CollapseContainers, collapse with
            {
                Attachment = collapse.Attachment with { AttachmentSide = attachmentSide },
                TriggerThicknessDip = Math.Clamp(triggerThicknessDip, 2, 24),
                ProximityDip = Math.Clamp(proximityDip, 0, 256),
                Animation = animation
            })
        };
        return true;
    }

    public static bool TryResetCollapse(LayoutProfile profile, string instanceId, out LayoutProfile updated)
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

    private static LayoutContainerElement? FindContainer(LayoutProfile profile, string id) =>
        profile.Containers.SelectMany(EnumerateContainers).FirstOrDefault(item => item.InstanceId == id);

    private static IEnumerable<LayoutContainerElement> EnumerateContainers(LayoutContainerElement container)
    {
        yield return container;
        foreach (var nested in container.PrimarySlot.Children.Concat(container.SecondarySlot.Children).OfType<LayoutContainerElement>())
        {
            foreach (var item in EnumerateContainers(nested))
            {
                yield return item;
            }
        }
    }

    private static LayoutCollapseContainer? FindCollapse(LayoutProfile profile, string id) =>
        profile.CollapseContainers.FirstOrDefault(item => item.InstanceId == id);

    private static bool TryUpdateElement(
        LayoutProfile profile,
        string instanceId,
        Func<LayoutElement, LayoutElement> update,
        out LayoutProfile updated)
    {
        var state = new EditState();
        var containers = profile.Containers.Select(container => UpdateContainer(container, instanceId, update, state)).ToArray();
        if (state.Changed)
        {
            updated = profile with { Containers = containers };
            return true;
        }

        var collapses = profile.CollapseContainers.Select(collapse =>
        {
            var children = collapse.ExpandedSlot.Children.Select(child =>
            {
                if (child.InstanceId != instanceId)
                {
                    return child;
                }

                var next = update(child);
                state.Changed = next != child;
                return next;
            }).ToArray();
            return state.Changed ? collapse with { ExpandedSlot = collapse.ExpandedSlot with { Children = children } } : collapse;
        }).ToArray();

        updated = state.Changed ? profile with { CollapseContainers = collapses } : profile;
        return state.Changed;
    }

    private static LayoutContainerElement UpdateContainer(
        LayoutContainerElement container,
        string instanceId,
        Func<LayoutElement, LayoutElement> update,
        EditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        if (container.InstanceId == instanceId)
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

                if (child.InstanceId == instanceId)
                {
                    var next = update(child);
                    state.Changed = next != child;
                    return next;
                }

                return child is LayoutContainerElement nested
                    ? UpdateContainer(nested, instanceId, update, state)
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

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> source, T item) =>
        source.Select(existing => Match(existing, item) ? item : existing).ToArray();

    private static bool Match<T>(T existing, T item) => (existing, item) switch
    {
        (LayoutElement a, LayoutElement b) => a.InstanceId == b.InstanceId,
        (LayoutCollapseContainer a, LayoutCollapseContainer b) => a.InstanceId == b.InstanceId,
        _ => Equals(existing, item)
    };

    private sealed class EditState
    {
        public bool Changed { get; set; }
    }
}
