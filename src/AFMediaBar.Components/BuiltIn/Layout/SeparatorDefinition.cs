using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Layout;

public sealed class SeparatorDefinition : IComponentDefinition
{
    public ComponentMetadata Metadata { get; } = new(
        ComponentTypeIds.Separator,
        "Settings.LayoutWidget.SeparatorTitle",
        "Settings.LayoutWidget.SeparatorDescription",
        ComponentCategory.Layout,
        ComponentCapabilities.Display,
        true, true, true, true, true, 40);

    public ComponentKind Kind => ComponentKind.Functional;

    public IComponentSettings CreateDefaultSettings() => new SeparatorSettings();

    public ComponentMeasureResult Measure(IComponentSettings settings, ComponentMeasureContext context)
    {
        var separator = settings as SeparatorSettings ?? new SeparatorSettings();
        var thickness = Math.Clamp(separator.ThicknessDip, 1, 8);
        var length = Math.Clamp(separator.LengthDip, 4, 256);
        var width = ToCells(thickness + 16, context.CellSizeDip);
        var height = ToCells(length, context.CellSizeDip);
        return new ComponentMeasureResult(width, height, 1, 1, true,
            context.Columns < width || context.Rows < height ? "Component.MeasureTooSmall" : null);
    }

    public IReadOnlyList<ComponentValidationIssue> Validate(IComponentSettings settings)
    {
        if (settings is not SeparatorSettings separator)
        {
            return [new ComponentValidationIssue("Component.TypeMismatch", "Component.Validation.TypeMismatch")];
        }

        var issues = new List<ComponentValidationIssue>();
        if (separator.ThicknessDip is < 1 or > 8)
        {
            issues.Add(new ComponentValidationIssue("Separator.InvalidThickness", "Component.Validation.SeparatorThickness"));
        }

        if (separator.LengthDip is < 4 or > 256)
        {
            issues.Add(new ComponentValidationIssue("Separator.InvalidLength", "Component.Validation.SeparatorLength"));
        }

        return issues;
    }

    public bool IsInteractive(IComponentSettings settings) => false;

    private static int ToCells(int dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / (double)Math.Max(1, cellSizeDip)));
}
