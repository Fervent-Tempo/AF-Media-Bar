using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;

namespace AFMediaBar.Layout.Widgets;

public sealed class BuiltInWidgetCatalog : IWidgetCatalog
{
    private static readonly IReadOnlyList<WidgetDescriptor> Definitions = CreateDefinitions();

    public IReadOnlyList<WidgetDescriptor> Items => Definitions;

    public bool TryGet(string typeId, out WidgetDescriptor descriptor)
    {
        descriptor = Definitions.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return descriptor is not null;
    }

    private static IReadOnlyList<WidgetDescriptor> CreateDefinitions()
    {
        var registry = new BuiltInComponentRegistry();
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            BuiltInWidgetTypeIds.Artwork,
            BuiltInWidgetTypeIds.MediaText,
            BuiltInWidgetTypeIds.MediaSource,
            BuiltInWidgetTypeIds.Command,
            BuiltInWidgetTypeIds.Metrics,
            BuiltInWidgetTypeIds.Spectrum,
            BuiltInWidgetTypeIds.Separator
        };

        return registry.Items
            .Where(definition => definition.Kind == ComponentKind.Functional && supported.Contains(definition.Metadata.TypeId))
            .Select(definition =>
            {
                var result = definition.Measure(
                    definition.CreateDefaultSettings(),
                    new ComponentMeasureContext(48, 24, 8, false));
                return new WidgetDescriptor(
                    definition.Metadata.TypeId,
                    ToLayoutCategory(definition.Metadata.Category),
                    (WidgetCapabilities)(int)definition.Metadata.Capabilities,
                    new LayoutGridRect(0, 0, result.PreferredWidth, result.PreferredHeight),
                    LayoutGridRect.Unit(0, 0),
                    definition.Metadata.SupportsCollapsedSlot);
            })
            .ToArray();
    }

    private static LayoutComponentCategory ToLayoutCategory(ComponentCategory category) => category switch
    {
        ComponentCategory.Media => LayoutComponentCategory.Media,
        ComponentCategory.Playback => LayoutComponentCategory.Controls,
        ComponentCategory.Audio => LayoutComponentCategory.Audio,
        ComponentCategory.System => LayoutComponentCategory.System,
        _ => LayoutComponentCategory.Layout
    };
}
