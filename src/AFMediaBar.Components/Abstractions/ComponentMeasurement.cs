namespace AFMediaBar.Components.Abstractions;

public sealed record ComponentMetadata(
    string TypeId,
    string NameResourceKey,
    string DescriptionResourceKey,
    ComponentCategory Category,
    ComponentCapabilities Capabilities,
    bool SupportsTaskbar,
    bool SupportsFloating,
    bool SupportsHorizontal,
    bool SupportsVertical,
    bool SupportsCollapsedSlot,
    int SortOrder = 0);

public sealed record ComponentMeasureContext(
    int Columns,
    int Rows,
    int CellSizeDip,
    bool IsVertical,
    int? AvailableWidth = null,
    int? AvailableHeight = null);

public sealed record ComponentMeasureResult(
    int PreferredWidth,
    int PreferredHeight,
    int MinimumWidth,
    int MinimumHeight,
    bool IsCompressible,
    string? WarningCode = null)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningCode);
}

public sealed record ComponentValidationIssue(string Code, string MessageResourceKey, bool IsWarning = false);
