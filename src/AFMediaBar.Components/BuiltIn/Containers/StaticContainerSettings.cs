using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed record StaticContainerSettings(
    ComponentFlowOrientation Orientation = ComponentFlowOrientation.Automatic,
    ComponentContentAlignment ContentAlignment = ComponentContentAlignment.Center) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.StaticContainer;
}
