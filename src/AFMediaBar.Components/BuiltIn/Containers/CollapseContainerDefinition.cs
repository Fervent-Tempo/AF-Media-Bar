using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed class CollapseContainerDefinition : ComponentDefinitionBase<CollapseContainerSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.CollapseContainer, "Settings.Layout.ContainerAutoCollapse", "Settings.Layout.ContainerAutoCollapse",
        ComponentCategory.Container, ComponentCapabilities.Display, true, true, true, true, false, 2);
    public override ComponentKind Kind => ComponentKind.Container;
    public override CollapseContainerSettings CreateDefault() => new();
    public override ComponentMeasureResult Measure(CollapseContainerSettings settings, ComponentMeasureContext context) =>
        Result(ToCells(120, context.CellSizeDip), ToCells(80, context.CellSizeDip), 1, 1, true, context);
    public override IReadOnlyList<ComponentValidationIssue> Validate(CollapseContainerSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.TriggerThicknessDip is < 2 or > 24) issues.Add(new("Collapse.InvalidTriggerThickness", "Component.Validation.CollapseTriggerThickness"));
        if (settings.ProximityDip is < 0 or > 512) issues.Add(new("Collapse.InvalidProximity", "Component.Validation.CollapseProximity"));
        if (settings.EffectiveAnimation.DurationMilliseconds is < 0 or > 5000) issues.Add(new("Collapse.InvalidDuration", "Component.Validation.AnimationDuration"));
        return issues;
    }
}
