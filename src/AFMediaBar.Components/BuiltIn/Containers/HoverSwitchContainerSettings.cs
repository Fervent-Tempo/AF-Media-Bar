using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed record HoverSwitchContainerSettings(
    ComponentFlowOrientation Orientation = ComponentFlowOrientation.Automatic,
    ComponentContentAlignment PrimaryContentAlignment = ComponentContentAlignment.Center,
    ComponentContentAlignment SecondaryContentAlignment = ComponentContentAlignment.Center,
    int ProximityDip = 48,
    ComponentAnimationSettings? Animation = null) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.HoverSwitchContainer;
    public ComponentAnimationSettings EffectiveAnimation => Animation ?? new();
}
