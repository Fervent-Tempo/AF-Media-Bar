using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed record CollapseContainerSettings(
    int TriggerThicknessDip = 6,
    int ProximityDip = 72,
    ComponentAnimationSettings? Animation = null) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.CollapseContainer;
    public ComponentAnimationSettings EffectiveAnimation => Animation ?? new();
}
