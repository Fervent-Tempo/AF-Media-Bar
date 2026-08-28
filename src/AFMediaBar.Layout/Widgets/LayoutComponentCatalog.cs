using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Layout.Widgets;

public static class LayoutComponentCatalog
{
    public static bool TryGet(string typeId, out WidgetDescriptor descriptor)
    {
        descriptor = new BuiltInWidgetCatalog().Items.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return descriptor is not null;
    }

    public static bool IsInteractive(LayoutWidgetElement widget)
    {
        if (!widget.Enabled || !TryGet(widget.TypeId, out var definition))
        {
            return false;
        }

        return widget.Settings switch
        {
            ArtworkWidgetSettings artwork => artwork.OpenSourceOnClick,
            MetricsWidgetSettings metrics => metrics.OpenTaskManagerOnClick,
            _ => (definition.Capabilities & WidgetCapabilities.Interactive) != 0
        };
    }

    public static WidgetSettings CreateDefaultSettings(string typeId)
    {
        if (ComponentDefinitionAdapter.TryCreateDefaultSettings(typeId, out var settings))
        {
            return settings;
        }

        return typeId switch
        {
        BuiltInWidgetTypeIds.Artwork => new ArtworkWidgetSettings(6, false, true),
        BuiltInWidgetTypeIds.MediaText => new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1),
        BuiltInWidgetTypeIds.MediaSource => new MediaTextWidgetSettings(MediaTextKind.Source, false, 11, 1),
        BuiltInWidgetTypeIds.Command => new CommandWidgetSettings(MediaCommandKind.PlayPause, CommandWidgetSettings.DefaultButtonSizeDip),
        BuiltInWidgetTypeIds.Metrics => new MetricsWidgetSettings(MetricKind.SystemMemory, false, 2500, [MetricKind.SystemMemory]),
        _ => new MediaTextWidgetSettings(MediaTextKind.Title, false, 14, 1)
        };
    }
}
