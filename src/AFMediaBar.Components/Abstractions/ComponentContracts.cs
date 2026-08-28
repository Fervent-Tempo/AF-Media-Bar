namespace AFMediaBar.Components.Abstractions;

public enum ComponentKind { Container = 0, Functional = 1 }

public enum ComponentCategory { Container = 0, Media = 1, Playback = 2, Audio = 3, System = 4, Layout = 5 }

[Flags]
public enum ComponentCapabilities
{
    None = 0,
    Display = 1,
    Invoke = 2,
    Adjust = 4,
    Popup = 8,
    Interactive = Invoke | Adjust | Popup
}

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

public static class ComponentTypeIds
{
    public const string StaticContainer = "builtin.container.static";
    public const string HoverSwitchContainer = "builtin.container.hover-switch";
    public const string CollapseContainer = "builtin.container.collapse";
    public const string Artwork = "builtin.artwork";
    public const string MediaText = "builtin.media-text";
    public const string MediaSource = "builtin.media-source";
    public const string PlaybackCommand = "builtin.command";
    public const string OutputDevice = "builtin.output-device";
    public const string Volume = "builtin.volume";
    public const string Spectrum = "builtin.spectrum";
    public const string Metrics = "builtin.metrics";
    public const string Separator = "builtin.separator";
}
