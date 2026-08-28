using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public sealed class MediaTextDefinition : ComponentDefinitionBase<MediaTextSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.MediaText, "Settings.LayoutWidget.MediaTextTitle", "Settings.LayoutWidget.MediaTextDescription",
        ComponentCategory.Media, ComponentCapabilities.Display, true, true, true, true, true, 11);

    public override MediaTextSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(MediaTextSettings settings, ComponentMeasureContext context)
    {
        var fontSize = Math.Clamp(settings.FontSizeDip, 6, 72);
        var combined = settings.TextKind == MediaTextContentKind.TitleAndArtist;
        var width = context.IsVertical ? 68 : combined ? 150 : 210;
        var height = combined
            ? Math.Max(22, Math.Ceiling(fontSize * 1.25)) + Math.Max(18, Math.Ceiling(Math.Max(6, fontSize - 3) * 1.25))
            : Math.Max(40, Math.Max(12, Math.Ceiling(fontSize * 1.25)) * Math.Clamp(settings.MaxLines, 1, 2));
        return Result(ToCells(width, context.CellSizeDip), ToCells(height, context.CellSizeDip), 1, 1, true, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(MediaTextSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.FontSizeDip is < 6 or > 72) issues.Add(new("MediaText.InvalidFontSize", "Component.Validation.MediaTextFontSize"));
        if (settings.MaxLines is < 1 or > 2) issues.Add(new("MediaText.InvalidMaxLines", "Component.Validation.MediaTextMaxLines"));
        if (settings.EnableMarquee && settings.MaxLines > 1) issues.Add(new("MediaText.MarqueeIgnored", "Component.Validation.MediaTextMarqueeIgnored", true));
        return issues;
    }
}
