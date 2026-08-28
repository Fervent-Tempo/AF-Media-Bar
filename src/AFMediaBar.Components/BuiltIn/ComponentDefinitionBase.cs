using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn;

public abstract class ComponentDefinitionBase<TSettings> : IComponentDefinition
    where TSettings : class, IComponentSettings
{
    public abstract ComponentMetadata Metadata { get; }
    public virtual ComponentKind Kind => ComponentKind.Functional;
    public abstract TSettings CreateDefault();
    public abstract ComponentMeasureResult Measure(TSettings settings, ComponentMeasureContext context);
    public virtual IReadOnlyList<ComponentValidationIssue> Validate(TSettings settings) => [];
    public virtual bool IsInteractive(TSettings settings) =>
        (Metadata.Capabilities & ComponentCapabilities.Interactive) != 0;

    IComponentSettings IComponentDefinition.CreateDefaultSettings() => CreateDefault();

    ComponentMeasureResult IComponentDefinition.Measure(IComponentSettings settings, ComponentMeasureContext context) =>
        settings is TSettings typed
            ? Measure(typed, context)
            : Measure(CreateDefault(), context) with { WarningCode = "Component.TypeMismatch" };

    IReadOnlyList<ComponentValidationIssue> IComponentDefinition.Validate(IComponentSettings settings) =>
        settings is TSettings typed
            ? Validate(typed)
            : [new ComponentValidationIssue("Component.TypeMismatch", "Component.Validation.TypeMismatch")];

    bool IComponentDefinition.IsInteractive(IComponentSettings settings) =>
        settings is TSettings typed && IsInteractive(typed);

    protected static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / Math.Max(1, cellSizeDip)));

    protected static ComponentMeasureResult Result(
        int preferredWidth,
        int preferredHeight,
        int minimumWidth,
        int minimumHeight,
        bool isCompressible,
        ComponentMeasureContext context) =>
        new(
            preferredWidth,
            preferredHeight,
            minimumWidth,
            minimumHeight,
            isCompressible,
            context.Columns < preferredWidth || context.Rows < preferredHeight
                ? "Component.MeasureTooSmall"
                : null);
}
