using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public sealed class MediaSourceDefinition : ComponentDefinitionBase<MediaSourceSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.MediaSource, "Settings.LayoutWidget.MediaSourceTitle", "Settings.LayoutWidget.MediaSourceDescription",
        ComponentCategory.Media, ComponentCapabilities.Display | ComponentCapabilities.Invoke,
        true, true, true, true, true, 12);

    public override MediaSourceSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(MediaSourceSettings settings, ComponentMeasureContext context)
    {
        var fontSize = Math.Clamp(settings.FontSizeDip, 6, 72);
        var width = context.IsVertical ? 68 : 210;
        var height = Math.Max(40, Math.Max(12, Math.Ceiling(fontSize * 1.25)) * Math.Clamp(settings.MaxLines, 1, 2));
        return Result(ToCells(width, context.CellSizeDip), ToCells(height, context.CellSizeDip), 1, 1, true, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(MediaSourceSettings settings) =>
        settings.FontSizeDip is < 6 or > 72 || settings.MaxLines is < 1 or > 2
            ? [new("MediaSource.InvalidTextLayout", "Component.Validation.MediaSourceTextLayout")]
            : [];
}
