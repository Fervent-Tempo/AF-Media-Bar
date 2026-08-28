using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed class SpectrumDefinition : ComponentDefinitionBase<SpectrumSettings>
{
    public const int MaximumBandCount = 9;

    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.Spectrum, "Settings.LayoutWidget.SpectrumTitle", "Settings.LayoutWidget.SpectrumDescription",
        ComponentCategory.Audio, ComponentCapabilities.Display, true, true, true, true, true, 23);

    public override SpectrumSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(SpectrumSettings settings, ComponentMeasureContext context) =>
        Result(
            ToCells(88, context.CellSizeDip), ToCells(24, context.CellSizeDip),
            ToCells(24, context.CellSizeDip), ToCells(12, context.CellSizeDip), true, context);

    public override IReadOnlyList<ComponentValidationIssue> Validate(SpectrumSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.BandCount is < 1 or > MaximumBandCount) issues.Add(new("Spectrum.InvalidBandCount", "Component.Validation.SpectrumBandCount"));
        if (settings.RefreshRateHz is < 5 or > 30) issues.Add(new("Spectrum.InvalidRefreshRate", "Component.Validation.SpectrumRefreshRate"));
        if (settings.SensitivityPercent is < 1 or > 400) issues.Add(new("Spectrum.InvalidSensitivity", "Component.Validation.SpectrumSensitivity"));
        return issues;
    }
}
