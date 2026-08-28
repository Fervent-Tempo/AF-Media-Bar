using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed class VolumeDefinition : ComponentDefinitionBase<VolumeSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.Volume, "Settings.LayoutWidget.VolumeTitle", "Settings.LayoutWidget.VolumeDescription",
        ComponentCategory.Audio, ComponentCapabilities.Display | ComponentCapabilities.Adjust | ComponentCapabilities.Popup,
        true, true, true, true, false, 22);

    public override VolumeSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(VolumeSettings settings, ComponentMeasureContext context)
    {
        var size = Math.Clamp(settings.ButtonSizeDip, 20, 96);
        var cells = ToCells(size, context.CellSizeDip);
        var minimum = ToCells(20, context.CellSizeDip);
        return Result(cells, cells, minimum, minimum, false, context);
    }

    public override IReadOnlyList<ComponentValidationIssue> Validate(VolumeSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.ButtonSizeDip is < 20 or > 96) issues.Add(new("Volume.InvalidButtonSize", "Component.Validation.VolumeButtonSize"));
        if (settings.WheelStepPercent is < 1 or > 20) issues.Add(new("Volume.InvalidWheelStep", "Component.Validation.VolumeWheelStep"));
        return issues;
    }
}
