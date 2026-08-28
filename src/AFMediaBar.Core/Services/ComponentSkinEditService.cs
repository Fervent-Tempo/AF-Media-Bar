using AFMediaBar.Layout.Editing;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>Core/WPF adapter for skin metadata; layout traversal is delegated to Layout.</summary>
public static class ComponentSkinEditService
{
    public static bool TryUpdateWidgetSkin(LayoutProfile profile, string instanceId, ComponentSkinAssignment? assignment, out LayoutProfile updated) =>
        LayoutSkinAssignmentService.TryUpdateWidgetSkin(profile, instanceId, assignment?.SkinId, assignment?.Version, assignment?.Settings, out updated);
}
