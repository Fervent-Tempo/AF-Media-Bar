using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Resolves Core skin assignments to stable WPF resource keys. It owns no timers,
/// windows, or background work, so previews can be discarded with the visual tree.
/// </summary>
internal sealed class ComponentSkinService
{
    internal string ResolveResourceKey(LayoutWidgetElement widget, bool menuTheme)
    {
        var assignment = ComponentSkinCatalog.Normalize(
            widget.TypeId,
            widget.SkinId,
            widget.SkinVersion,
            widget.SkinSettings);
        if (assignment?.SkinId == ComponentSkinCatalog.ExampleSkinId &&
            widget.Settings is not CommandWidgetSettings { Command: MediaCommandKind.PlayPause })
        {
            assignment = null;
        }
        if (assignment is not null &&
            ComponentSkinCatalog.TryGet(assignment.SkinId, out var definition) &&
            !string.IsNullOrWhiteSpace(definition.ResourceKey))
        {
            return definition.ResourceKey;
        }

        return menuTheme ? "LayoutEditorButtonStyle" : "TransportButtonStyle";
    }

    internal static ComponentSkinAssignment? Normalize(LayoutWidgetElement widget) =>
        ComponentSkinCatalog.Normalize(
            widget.TypeId,
            widget.SkinId,
            widget.SkinVersion,
            widget.SkinSettings);
}
