using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.System;

public sealed class MetricsDefinition : ComponentDefinitionBase<MetricsSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.Metrics, "Settings.LayoutWidget.MetricsTitle", "Settings.LayoutWidget.MetricsDescription",
        ComponentCategory.System, ComponentCapabilities.Display | ComponentCapabilities.Invoke,
        true, true, true, true, true, 30);

    public override MetricsSettings CreateDefault() => new();

    public override ComponentMeasureResult Measure(MetricsSettings settings, ComponentMeasureContext context) =>
        Result(ToCells(74, context.CellSizeDip), ToCells(24, context.CellSizeDip), 1, 1, true, context);

    public override IReadOnlyList<ComponentValidationIssue> Validate(MetricsSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.RefreshIntervalMilliseconds is < 250 or > 60000)
            issues.Add(new("Metrics.InvalidRefreshInterval", "Component.Validation.MetricsRefreshInterval"));
        if (settings.EffectiveCycleMetrics.Distinct().Count() != settings.EffectiveCycleMetrics.Count)
            issues.Add(new("Metrics.DuplicateCycleMetric", "Component.Validation.MetricsDuplicateCycleMetric", true));
        return issues;
    }

    public override bool IsInteractive(MetricsSettings settings) => settings.OpenTaskManagerOnClick;
}
