using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets;

/// <summary>
/// Compatibility facade for callers that have not yet adopted dependency injection.
/// </summary>
public static class ComponentDefinitionAdapter
{
    public static IComponentSettingsMapper Default { get; } = new Schema5ComponentSettingsMapper();

    internal static bool TryCreateDefaultSettings(string typeId, out WidgetSettings settings) =>
        Default.TryCreateDefaultSettings(typeId, out settings);

    internal static bool TryMeasure(
        LayoutProfile profile,
        LayoutWidgetElement widget,
        out (int Width, int Height) measurement) =>
        Default.TryMeasure(profile, widget, out measurement);

    public static bool TryMapSettings(LayoutWidgetElement widget, out IComponentSettings componentSettings) =>
        Default.TryMapSettings(widget, out componentSettings);
}
