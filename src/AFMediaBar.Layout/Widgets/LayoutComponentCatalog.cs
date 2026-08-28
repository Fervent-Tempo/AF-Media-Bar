using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Layout.Widgets;

public static class LayoutComponentCatalog
{
    private static readonly BuiltInComponentRegistry ComponentRegistry = new();
    private static readonly BuiltInWidgetCatalog WidgetCatalog = new(ComponentRegistry);
    private static readonly Schema5ComponentSettingsMapper SettingsMapper = new(ComponentRegistry);

    public static bool TryGet(string typeId, out WidgetDescriptor descriptor)
    {
        return WidgetCatalog.TryGet(typeId, out descriptor);
    }

    public static bool IsInteractive(
        LayoutWidgetElement widget,
        IComponentSettingsMapper? settingsMapper = null)
    {
        var mapper = settingsMapper ?? SettingsMapper;
        if (!widget.Enabled || !TryGet(widget.TypeId, out var definition))
        {
            return false;
        }

        return mapper.TryMapSettings(widget, out var settings) &&
               ComponentRegistry.TryGet(settings.TypeId, out var component) &&
               component.IsInteractive(settings);
    }

    public static WidgetSettings CreateDefaultSettings(string typeId)
    {
        if (SettingsMapper.TryCreateDefaultSettings(typeId, out var settings))
        {
            return settings;
        }

        throw new ArgumentException($"Unknown layout component TypeId: {typeId}", nameof(typeId));
    }
}
