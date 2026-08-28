using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Playback;

public sealed class PlaybackCommandDefinition : ComponentDefinitionBase<PlaybackCommandSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.PlaybackCommand, "Settings.LayoutWidget.CommandTitle", "Settings.LayoutWidget.CommandDescription",
        ComponentCategory.Playback, ComponentCapabilities.Invoke, true, true, true, true, false, 20);

    public override PlaybackCommandSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(PlaybackCommandSettings settings, ComponentMeasureContext context)
    {
        var size = Math.Clamp(settings.ButtonSizeDip, 20, 96);
        var cells = ToCells(size, context.CellSizeDip);
        var minimum = ToCells(20, context.CellSizeDip);
        return Result(cells, cells, minimum, minimum, false, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(PlaybackCommandSettings settings) =>
        settings.ButtonSizeDip is < 20 or > 96
            ? [new("PlaybackCommand.InvalidButtonSize", "Component.Validation.CommandButtonSize")]
            : [];
}
