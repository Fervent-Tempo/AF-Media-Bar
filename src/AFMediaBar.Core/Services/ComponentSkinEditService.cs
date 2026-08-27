using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Core/WPF adapter for skin metadata. Skin catalogs and resource versions are
/// intentionally outside the UI-independent Layout assembly.
/// </summary>
public static class ComponentSkinEditService
{
    public static bool TryUpdateWidgetSkin(
        LayoutProfile profile,
        string instanceId,
        ComponentSkinAssignment? assignment,
        out LayoutProfile updated)
    {
        var state = new SkinEditState();
        var containers = profile.Containers.Select(container => UpdateContainer(container, instanceId, assignment, state)).ToArray();
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

                if (child is not LayoutWidgetElement widget)
                {
                    return child;
                }

                state.Changed = true;
                return widget with
                {
                    SkinId = assignment?.SkinId,
                    SkinVersion = assignment?.Version,
                    SkinSettings = assignment?.Settings
                };
            }).ToArray();
            return state.Changed ? collapse with { ExpandedSlot = collapse.ExpandedSlot with { Children = children } } : collapse;
        }).ToArray();

        updated = state.Changed ? profile with { CollapseContainers = collapses } : profile;
        return state.Changed;
    }

    private static LayoutContainerElement UpdateContainer(LayoutContainerElement container, string instanceId, ComponentSkinAssignment? assignment, SkinEditState state)
    {
        if (state.Changed)
        {
            return container;
        }

        if (container.InstanceId == instanceId)
        {
            return container;
        }

        LayoutSlot Rewrite(LayoutSlot slot)
        {
            var children = slot.Children.Select(child =>
            {
                if (state.Changed)
                {
                    return child;
                }

                if (child.InstanceId == instanceId && child is LayoutWidgetElement widget)
                {
                    state.Changed = true;
                    return widget with
                    {
                        SkinId = assignment?.SkinId,
                        SkinVersion = assignment?.Version,
                        SkinSettings = assignment?.Settings
                    };
                }

                return child is LayoutContainerElement nested
                    ? UpdateContainer(nested, instanceId, assignment, state)
                    : child;
            }).ToArray();
            return slot with { Children = children };
        }

        return container with { PrimarySlot = Rewrite(container.PrimarySlot), SecondarySlot = Rewrite(container.SecondarySlot) };
    }

    private sealed class SkinEditState
    {
        public bool Changed { get; set; }
    }
}
