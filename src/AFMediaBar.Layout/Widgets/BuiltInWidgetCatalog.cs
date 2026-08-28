using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;

namespace AFMediaBar.Layout.Widgets;

public sealed class BuiltInWidgetCatalog : IWidgetCatalog
{
    private readonly IReadOnlyList<WidgetDescriptor> _definitions;

    public BuiltInWidgetCatalog(IComponentRegistry? registry = null)
    {
        _definitions = CreateDefinitions(registry ?? new BuiltInComponentRegistry());
    }

    public IReadOnlyList<WidgetDescriptor> Items => _definitions;

    public bool TryGet(string typeId, out WidgetDescriptor descriptor)
    {
        descriptor = _definitions.FirstOrDefault(item =>
            string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return descriptor is not null;
    }

    private static IReadOnlyList<WidgetDescriptor> CreateDefinitions(IComponentRegistry registry)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            ComponentTypeIds.Artwork,
            ComponentTypeIds.MediaText,
            ComponentTypeIds.MediaSource,
            ComponentTypeIds.PlaybackCommand,
            ComponentTypeIds.Metrics,
            ComponentTypeIds.Spectrum,
            ComponentTypeIds.Separator
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
