using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed class OutputDeviceDefinition : ComponentDefinitionBase<OutputDeviceSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.OutputDevice, "Settings.LayoutWidget.OutputDeviceTitle", "Settings.LayoutWidget.OutputDeviceDescription",
        ComponentCategory.Audio, ComponentCapabilities.Display | ComponentCapabilities.Invoke | ComponentCapabilities.Popup,
        true, true, true, true, false, 21);

    public override OutputDeviceSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(OutputDeviceSettings settings, ComponentMeasureContext context)
    {
        var size = Math.Clamp(settings.ButtonSizeDip, 20, 96);
        var cells = ToCells(size, context.CellSizeDip);
        var minimum = ToCells(20, context.CellSizeDip);
        return Result(cells, cells, minimum, minimum, false, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(OutputDeviceSettings settings) =>
        settings.ButtonSizeDip is < 20 or > 96
            ? [new("OutputDevice.InvalidButtonSize", "Component.Validation.OutputDeviceButtonSize")]
            : [];
}
