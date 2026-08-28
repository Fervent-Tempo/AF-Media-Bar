using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Media;

public sealed class ArtworkDefinition : ComponentDefinitionBase<ArtworkSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.Artwork, "Settings.LayoutWidget.ArtworkTitle", "Settings.LayoutWidget.ArtworkDescription",
        ComponentCategory.Media, ComponentCapabilities.Display | ComponentCapabilities.Invoke,
        true, true, true, true, true, 10);

    public override ArtworkSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(ArtworkSettings settings, ComponentMeasureContext context)
    {
        var size = ToCells(40, context.CellSizeDip);
        return Result(size, size, 1, 1, false, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(ArtworkSettings settings) =>
        settings.CornerRadiusDip is < 0 or > 32
            ? [new("Artwork.InvalidCornerRadius", "Component.Validation.ArtworkCornerRadius")]
            : [];

    public override bool IsInteractive(ArtworkSettings settings) => settings.OpenSourceOnClick;
}
