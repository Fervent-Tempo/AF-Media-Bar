using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.Layout.Widgets;

public sealed class BuiltInWidgetCatalog : IWidgetCatalog
{
    private static readonly IReadOnlyList<WidgetDescriptor> Definitions =
    [
        Create(BuiltInWidgetTypeIds.Artwork, LayoutComponentCategory.Media, WidgetCapabilities.Display | WidgetCapabilities.Invoke, 5, 5, true),
        Create(BuiltInWidgetTypeIds.MediaText, LayoutComponentCategory.Media, WidgetCapabilities.Display, 27, 5, true),
        Create(BuiltInWidgetTypeIds.MediaSource, LayoutComponentCategory.Media, WidgetCapabilities.Display | WidgetCapabilities.Invoke, 27, 3, true),
        Create(BuiltInWidgetTypeIds.Command, LayoutComponentCategory.Controls, WidgetCapabilities.Invoke, 3, 3, false),
        Create(BuiltInWidgetTypeIds.Metrics, LayoutComponentCategory.System, WidgetCapabilities.Display | WidgetCapabilities.Invoke, 10, 3, true),
        Create(BuiltInWidgetTypeIds.Spectrum, LayoutComponentCategory.Audio, WidgetCapabilities.Display, 11, 3, true),
        Create(BuiltInWidgetTypeIds.Separator, LayoutComponentCategory.Layout, WidgetCapabilities.Display, 3, 3, true)
    ];

    public IReadOnlyList<WidgetDescriptor> Items => Definitions;

    public bool TryGet(string typeId, out WidgetDescriptor descriptor)
    {
        descriptor = Definitions.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return descriptor is not null;
    }

    private static WidgetDescriptor Create(
        string typeId,
        LayoutComponentCategory category,
        WidgetCapabilities capabilities,
        int width,
        int height,
        bool supportsCollapsedSlot) =>
        new(
            typeId,
            category,
            capabilities,
            new LayoutGridRect(0, 0, width, height),
            LayoutGridRect.Unit(0, 0),
            supportsCollapsedSlot);
}
