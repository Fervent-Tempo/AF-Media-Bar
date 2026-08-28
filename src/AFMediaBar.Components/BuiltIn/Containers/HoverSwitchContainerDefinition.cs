using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed class HoverSwitchContainerDefinition : ComponentDefinitionBase<HoverSwitchContainerSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.HoverSwitchContainer, "Settings.Layout.ContainerHoverSwitch", "Settings.Layout.ContainerHoverSwitch",
        ComponentCategory.Container, ComponentCapabilities.Display, true, true, true, true, false, 1);
    public override ComponentKind Kind => ComponentKind.Container;
    public override HoverSwitchContainerSettings CreateDefault() => new();
    public override ComponentMeasureResult Measure(HoverSwitchContainerSettings settings, ComponentMeasureContext context)
    {
        var width = ToCells(context.IsVertical ? 48 : 168, context.CellSizeDip);
        var height = ToCells(context.IsVertical ? 168 : 48, context.CellSizeDip);
        return Result(width, height, 1, 1, true, context);
    }
    public override IReadOnlyList<ComponentValidationIssue> Validate(HoverSwitchContainerSettings settings)
    {
        var issues = new List<ComponentValidationIssue>();
        if (settings.ProximityDip is < 0 or > 512) issues.Add(new("HoverSwitch.InvalidProximity", "Component.Validation.HoverSwitchProximity"));
        if (settings.EffectiveAnimation.DurationMilliseconds is < 0 or > 5000) issues.Add(new("HoverSwitch.InvalidDuration", "Component.Validation.AnimationDuration"));
        return issues;
    }
}
