using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Editing;

public static class LayoutSkinAssignmentService
{
    public static bool TryUpdateWidgetSkin(LayoutProfile profile, string instanceId, string? skinId, int? skinVersion, IReadOnlyDictionary<string, string>? skinSettings, out LayoutProfile updated)
    {
        var state = new EditState();
        var containers = profile.Containers.Select(container => UpdateContainer(container, instanceId, skinId, skinVersion, skinSettings, state)).ToArray();
        if (state.Changed)
        {
            updated = profile with { Containers = containers };
            return true;
        }

        var collapses = profile.CollapseContainers.Select(collapse =>
        {
            var children = collapse.ExpandedSlot.Children.Select(child =>
                child.InstanceId == instanceId && child is LayoutWidgetElement widget
                    ? WithSkin(widget, skinId, skinVersion, skinSettings, state)
                    : child).ToArray();
            return state.Changed ? collapse with { ExpandedSlot = collapse.ExpandedSlot with { Children = children } } : collapse;
        }).ToArray();
        updated = state.Changed ? profile with { CollapseContainers = collapses } : profile;
        return state.Changed;
    }

    private static LayoutContainerElement UpdateContainer(LayoutContainerElement container, string instanceId, string? skinId, int? skinVersion, IReadOnlyDictionary<string, string>? skinSettings, EditState state)
    {
        if (state.Changed || container.InstanceId == instanceId) return container;
        LayoutSlot Rewrite(LayoutSlot slot)
        {
            var children = slot.Children.Select(child =>
            {
                if (state.Changed) return child;
                if (child.InstanceId == instanceId && child is LayoutWidgetElement widget)
                    return WithSkin(widget, skinId, skinVersion, skinSettings, state);
                return child is LayoutContainerElement nested
                    ? UpdateContainer(nested, instanceId, skinId, skinVersion, skinSettings, state)
                    : child;
            }).ToArray();
            return slot with { Children = children };
        }
        return container with { PrimarySlot = Rewrite(container.PrimarySlot), SecondarySlot = Rewrite(container.SecondarySlot) };
    }

    private static LayoutWidgetElement WithSkin(LayoutWidgetElement widget, string? skinId, int? skinVersion, IReadOnlyDictionary<string, string>? skinSettings, EditState state)
    {
        state.Changed = true;
        return widget with { SkinId = skinId, SkinVersion = skinVersion, SkinSettings = skinSettings };
    }

    private sealed class EditState { internal bool Changed { get; set; } }
}
