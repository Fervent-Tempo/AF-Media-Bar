using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets.Schema5;

namespace AFMediaBar.Layout.Widgets;

/// <summary>
/// Conversion boundary between schema-5 persistence DTOs and component settings.
/// The schema DTOs remain authoritative for JSON compatibility; component settings
/// are used by composition, measurement and WPF presentation.
/// </summary>
public interface IComponentSettingsMapper
{
    bool TryCreateDefaultSettings(string typeId, out WidgetSettings settings);
    bool TryMapSettings(LayoutWidgetElement widget, out IComponentSettings componentSettings);
    bool TryMapToSchema5(IComponentSettings componentSettings, out string typeId, out WidgetSettings settings);
    bool TryMeasure(LayoutProfile profile, LayoutWidgetElement widget, out (int Width, int Height) measurement);
}

public sealed class Schema5ComponentSettingsMapper : IComponentSettingsMapper
{
    private readonly IComponentRegistry _registry;
    private readonly IReadOnlyDictionary<string, ISchema5ComponentCodec> _codecs;

    public Schema5ComponentSettingsMapper(IComponentRegistry? registry = null)
    {
        _registry = registry ?? new BuiltInComponentRegistry();
        _codecs = CreateCodecs();
        ValidateCodecCoverage(_registry, _codecs);
    }

    public bool TryCreateDefaultSettings(string typeId, out WidgetSettings settings)
    {
        settings = null!;
        if (!_registry.TryGet(typeId, out var definition))
        {
            return false;
        }

        return _codecs.TryGetValue(definition.Metadata.TypeId, out var codec) &&
            codec.TryToSchema5(definition.CreateDefaultSettings(), out settings);
    }

    public bool TryMeasure(
        LayoutProfile profile,
        LayoutWidgetElement widget,
        out (int Width, int Height) measurement)
    {
        measurement = default;
        var resolvedTypeId = Schema5ComponentTypeResolver.Resolve(widget.TypeId, widget.Settings);
        if (!TryMapSettings(resolvedTypeId, widget.Settings, out var settings) ||
            !_registry.TryGet(settings.TypeId, out var definition))
        {
            return false;
        }

        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var result = definition.Measure(
            settings,
            new ComponentMeasureContext(
                grid.Columns,
                grid.Rows,
                grid.CellSizeDip,
                profile.LayoutMode == PlayerLayoutMode.Vertical));
        var cell = Math.Max(grid.CellSizeDip, 1);
        var width = result.PreferredWidth;
        var height = result.PreferredHeight;
        if (widget.Settings is MediaTextWidgetSettings &&
            widget.Geometry is { WidthDip: not null, HeightDip: not null } geometry)
        {
            width = ToCells(geometry.WidthDip ?? 0, cell);
            height = ToCells(geometry.HeightDip ?? 0, cell);
        }

        measurement = (width, height);
        return true;
    }

    public bool TryMapSettings(LayoutWidgetElement widget, out IComponentSettings componentSettings) =>
        TryMapSettings(
            Schema5ComponentTypeResolver.Resolve(widget.TypeId, widget.Settings),
            widget.Settings,
            out componentSettings);

    public bool TryMapToSchema5(IComponentSettings componentSettings, out string typeId, out WidgetSettings settings)
    {
        typeId = componentSettings.TypeId;
        if (!_registry.TryGet(typeId, out _) ||
            !_codecs.TryGetValue(typeId, out var codec))
        {
            settings = null!;
            return false;
        }

        return codec.TryToSchema5(componentSettings, out settings);
    }

    private bool TryMapSettings(string typeId, WidgetSettings settings, out IComponentSettings componentSettings)
    {
        if (!_codecs.TryGetValue(typeId, out var codec))
        {
            componentSettings = null!;
            return false;
        }

        return codec.TryFromSchema5(settings, out componentSettings);
    }

    private static IReadOnlyDictionary<string, ISchema5ComponentCodec> CreateCodecs() =>
        Schema5ComponentCodecs.All.ToDictionary(codec => codec.TypeId, StringComparer.Ordinal);

    private static void ValidateCodecCoverage(
        IComponentRegistry registry,
        IReadOnlyDictionary<string, ISchema5ComponentCodec> codecs)
    {
        var missing = registry.Items
            .Where(definition => definition.Kind == ComponentKind.Functional)
            .Select(definition => definition.Metadata.TypeId)
            .Where(typeId => !codecs.ContainsKey(typeId))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing schema 5 component codec(s): {string.Join(", ", missing)}");
        }
    }

    private static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / cellSizeDip));

}
