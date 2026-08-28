using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Containers;

public sealed class StaticContainerDefinition : ComponentDefinitionBase<StaticContainerSettings>
{
    public override ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.StaticContainer, "Settings.Layout.ContainerStatic", "Settings.Layout.ContainerStatic",
        ComponentCategory.Container, ComponentCapabilities.Display, true, true, true, true, false, 0);
    public override ComponentKind Kind => ComponentKind.Container;
    public override StaticContainerSettings CreateDefault() => new();
    public override ComponentMeasureResult Measure(StaticContainerSettings settings, ComponentMeasureContext context)
    {
        var width = ToCells(context.IsVertical ? 48 : 168, context.CellSizeDip);
        var height = ToCells(context.IsVertical ? 168 : 48, context.CellSizeDip);
        return Result(width, height, 1, 1, true, context);
    }
}
