using System.Text.Json.Serialization;

namespace AFMediaBar.Models;

/// <summary>
/// 描述四种窗口上下文的独立布局档案；档案只保存可迁移的数据，不保存 WPF 控件实例。
/// Describes four independent window-context layouts; profiles persist portable data, never WPF control instances.
/// </summary>
internal enum LayoutProfileKey
{
    TaskbarHorizontal = 0,
    TaskbarVertical = 1,
    FloatingHorizontal = 2,
    FloatingVertical = 3
}

internal enum LayoutContainerKind
{
    Static = 0,
    HoverSwitch = 1,
    AutoCollapse = 2
}

internal enum LayoutEdge
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3
}

internal enum LayoutFlowOrientation
{
    Automatic = 0,
    Horizontal = 1,
    Vertical = 2
}

internal enum LayoutTriggerMode
{
    Always = 0,
    PointerNear = 1,
    EdgeNear = 2
}

internal enum LayoutEasingKind
{
    Linear = 0,
    EaseOut = 1,
    EaseInOut = 2
}

[Flags]
internal enum WidgetCapabilities
{
    None = 0,
    Display = 1,
    Invoke = 2,
    Adjust = 4,
    Popup = 8,
    Interactive = Invoke | Adjust | Popup
}

internal enum MediaTextKind
{
    Title = 0,
    Artist = 1,
    Source = 2
}

internal enum MetricKind
{
    SystemMemory = 0,
    SystemCpu = 1,
    SystemGpu = 2,
    ProcessMemory = 3
}

internal enum MediaCommandKind
{
    Previous = 0,
    PlayPause = 1,
    Next = 2,
    SelectSource = 3,
    AdjustVolume = 4,
    SelectOutputDevice = 5
}

/// <summary>
/// 内置组件的稳定标识；显示名称和说明由组件目录映射到三语言资源。
/// Stable identifiers for built-in widgets; localized names and descriptions come from the component catalog.
/// </summary>
internal static class BuiltInWidgetTypeIds
{
    internal const string Artwork = "builtin.artwork";
    internal const string MediaText = "builtin.media-text";
    internal const string MediaSource = "builtin.media-source";
    internal const string Command = "builtin.command";
    internal const string Metrics = "builtin.metrics";
    internal const string Spectrum = "builtin.spectrum";
    internal const string Separator = "builtin.separator";
}

internal sealed record LayoutThickness(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    internal static LayoutThickness Zero { get; } = new(0, 0, 0, 0);
}

internal sealed record LayoutGeometry(
    int? WidthDip,
    int? HeightDip,
    int? MinWidthDip,
    int? MaxWidthDip,
    int? MinHeightDip,
    int? MaxHeightDip,
    LayoutThickness Margin)
{
    internal static LayoutGeometry Auto { get; } = new(
        null,
        null,
        null,
        null,
        null,
        null,
        LayoutThickness.Zero);
}

internal sealed record LayoutAnimationSettings(
    bool Enabled,
    int DurationMilliseconds,
    int DelayMilliseconds,
    LayoutEasingKind Easing)
{
    internal static LayoutAnimationSettings Default { get; } = new(
        true,
        220,
        0,
        LayoutEasingKind.EaseOut);
}

internal sealed record LayoutSurfaceSettings(
    int LengthScalePercent,
    int ThicknessScalePercent,
    int GapDip,
    int CornerRadiusDip,
    int? WidthDip,
    int? HeightDip,
    bool SizeToContent,
    bool EdgeCollapseEnabled)
{
    internal static LayoutSurfaceSettings Default { get; } = new(
        100,
        100,
        4,
        6,
        null,
        null,
        true,
        false);
}

internal sealed record LayoutSlot(
    string SlotId,
    IReadOnlyList<LayoutElement> Children)
{
    internal static LayoutSlot Empty(string slotId) => new(slotId, []);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LayoutWidgetElement), "widget")]
[JsonDerivedType(typeof(LayoutContainerElement), "container")]
internal abstract record LayoutElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ArtworkWidgetSettings), "artwork")]
[JsonDerivedType(typeof(MediaTextWidgetSettings), "media-text")]
[JsonDerivedType(typeof(CommandWidgetSettings), "command")]
[JsonDerivedType(typeof(MetricsWidgetSettings), "metrics")]
[JsonDerivedType(typeof(SpectrumWidgetSettings), "spectrum")]
[JsonDerivedType(typeof(SeparatorWidgetSettings), "separator")]
internal abstract record WidgetSettings;

internal sealed record ArtworkWidgetSettings(
    int CornerRadiusDip,
    bool UseMediaPrimaryColor) : WidgetSettings;

internal sealed record MediaTextWidgetSettings(
    MediaTextKind TextKind,
    bool EnableMarquee,
    int FontSizeDip,
    int MaxLines) : WidgetSettings;

internal sealed record CommandWidgetSettings(
    MediaCommandKind Command,
    int ButtonSizeDip) : WidgetSettings;

internal sealed record MetricsWidgetSettings(
    MetricKind Metric,
    bool OpenTaskManagerOnClick,
    int RefreshIntervalMilliseconds,
    IReadOnlyList<MetricKind> CycleMetrics) : WidgetSettings;

internal sealed record SpectrumWidgetSettings(
    int BandCount,
    int RefreshRateHz,
    int SensitivityPercent) : WidgetSettings;

internal sealed record SeparatorWidgetSettings(
    int ThicknessDip,
    int LengthDip) : WidgetSettings;

internal sealed record LayoutWidgetElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    string TypeId,
    WidgetSettings Settings) : LayoutElement(InstanceId, Enabled, Geometry);

internal sealed record LayoutContainerElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    LayoutContainerKind ContainerKind,
    LayoutFlowOrientation Orientation,
    LayoutTriggerMode Trigger,
    int ProximityDip,
    LayoutAnimationSettings Animation,
    LayoutSlot PrimarySlot,
    LayoutSlot SecondarySlot,
    LayoutSlot CollapsedSlot) : LayoutElement(InstanceId, Enabled, Geometry);

/// <summary>
/// 描述长条外侧的自动折叠容器；折叠状态只保留触发区域，因此模型只保存展开内容。
/// Describes an auto-collapsing container outside the strip; the collapsed state is trigger-only, so only expanded content is persisted.
/// </summary>
internal sealed record LayoutEdgeContainer(
    string InstanceId,
    bool Enabled,
    LayoutEdge Edge,
    int OffsetDip,
    int TriggerThicknessDip,
    int ProximityDip,
    LayoutAnimationSettings Animation,
    LayoutSlot ExpandedSlot);

internal sealed record LayoutProfile(
    LayoutProfileKey Key,
    WindowHostMode HostMode,
    PlayerLayoutMode LayoutMode,
    LayoutSurfaceSettings Surface,
    IReadOnlyList<LayoutContainerElement> InlineContainers,
    IReadOnlyList<LayoutEdgeContainer> EdgeContainers,
    // 仅用于读取 schema 1；规范化后始终清空，避免新编辑器重新暴露旧三槽位根节点。
    // Read-only schema-1 compatibility; normalization always clears it so the new editor cannot expose the legacy three-slot root.
    LayoutContainerElement? Root = null);

internal sealed record LayoutDocument(
    int SchemaVersion,
    LayoutProfile TaskbarHorizontal,
    LayoutProfile TaskbarVertical,
    LayoutProfile FloatingHorizontal,
    LayoutProfile FloatingVertical)
{
    internal const int CurrentSchemaVersion = 2;

    internal LayoutProfile Get(LayoutProfileKey key) => key switch
    {
        LayoutProfileKey.TaskbarHorizontal => TaskbarHorizontal,
        LayoutProfileKey.TaskbarVertical => TaskbarVertical,
        LayoutProfileKey.FloatingHorizontal => FloatingHorizontal,
        LayoutProfileKey.FloatingVertical => FloatingVertical,
        _ => TaskbarHorizontal
    };

    internal LayoutDocument WithProfile(LayoutProfile profile) => profile.Key switch
    {
        LayoutProfileKey.TaskbarHorizontal => this with { TaskbarHorizontal = profile },
        LayoutProfileKey.TaskbarVertical => this with { TaskbarVertical = profile },
        LayoutProfileKey.FloatingHorizontal => this with { FloatingHorizontal = profile },
        LayoutProfileKey.FloatingVertical => this with { FloatingVertical = profile },
        _ => this
    };
}
