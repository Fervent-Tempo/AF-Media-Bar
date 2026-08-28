namespace AFMediaBar.Components.Abstractions;

public interface IComponentSettings
{
    string TypeId { get; }
}

public interface IComponentDefinition
{
    ComponentMetadata Metadata { get; }
    ComponentKind Kind { get; }
    IComponentSettings CreateDefaultSettings();
    ComponentMeasureResult Measure(IComponentSettings settings, ComponentMeasureContext context);
    IReadOnlyList<ComponentValidationIssue> Validate(IComponentSettings settings);
    bool IsInteractive(IComponentSettings settings);
}

public interface IComponentRegistry
{
    IReadOnlyList<IComponentDefinition> Items { get; }
    bool TryGet(string typeId, out IComponentDefinition definition);
}
