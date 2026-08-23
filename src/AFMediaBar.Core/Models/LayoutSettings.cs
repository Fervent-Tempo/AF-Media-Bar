using System.Text.Json.Serialization;

namespace AFMediaBar.Models;

/// <summary>
/// 标识横向和竖向两套共享布局；宿主模式属于窗口状态，不再复制布局档案。
/// Identifies the shared horizontal and vertical layouts; host mode remains window state and no longer duplicates profiles.
/// </summary>
public enum LayoutProfileKey
{
    Horizontal = 0,
    Vertical = 1,

    // 仅供 schema 1/2 的字符串枚举反序列化；迁移后不会写回这些值。
    // These names exist only to deserialize schema-1/2 string enums and are never persisted after migration.
    TaskbarHorizontal = 10,
    TaskbarVertical = 11,
    FloatingHorizontal = 12,
    FloatingVertical = 13
}

public enum LayoutContainerKind
{
    Static = 0,
    HoverSwitch = 1,
    AutoCollapse = 2
}

public enum LayoutEdge
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3
}

public enum LayoutFlowOrientation
{
    Automatic = 0,
    Horizontal = 1,
    Vertical = 2
}

/// <summary>
/// 控制容器内容在档案主轴交叉方向的对齐；默认居中可避免悬停内容贴在窗口边缘而浪费空间。
/// Controls cross-axis alignment inside a container; centered by default so hover content does not waste space at an edge.
/// </summary>
public enum LayoutContentAlignment
{
    Center = 0,
    Start = 1,
    End = 2,
    Stretch = 3
}

public enum LayoutTriggerMode
{
    Always = 0,
    PointerNear = 1,
    EdgeNear = 2
}

public enum LayoutEasingKind
{
    Linear = 0,
    EaseOut = 1,
    EaseInOut = 2
}

[Flags]
public enum WidgetCapabilities
{
    None = 0,
    Display = 1,
    Invoke = 2,
    Adjust = 4,
    Popup = 8,
    Interactive = Invoke | Adjust | Popup
}

public enum MediaTextKind
{
    Title = 0,
    Artist = 1,
    Source = 2,
    TitleAndArtist = 3
}

public enum MetricKind
{
    SystemMemory = 0,
    SystemCpu = 1,
    SystemGpu = 2,
    ProcessMemory = 3
}

public enum MediaCommandKind
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
public static class BuiltInWidgetTypeIds
{
    public const string Artwork = "builtin.artwork";
    public const string MediaText = "builtin.media-text";
    public const string MediaSource = "builtin.media-source";
    public const string Command = "builtin.command";
    public const string Metrics = "builtin.metrics";
    public const string Spectrum = "builtin.spectrum";
    public const string Separator = "builtin.separator";
}

public sealed record LayoutThickness(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public static LayoutThickness Zero { get; } = new(0, 0, 0, 0);
}

public sealed record LayoutGeometry(
    int? WidthDip,
    int? HeightDip,
    int? MinWidthDip,
    int? MaxWidthDip,
    int? MinHeightDip,
    int? MaxHeightDip,
    LayoutThickness Margin)
{
    public static LayoutGeometry Auto { get; } = new(
        null,
        null,
        null,
        null,
        null,
        null,
        LayoutThickness.Zero);
}

public sealed record LayoutAnimationSettings(
    bool Enabled,
    int DurationMilliseconds,
    int DelayMilliseconds,
    LayoutEasingKind Easing)
{
    public static LayoutAnimationSettings Default { get; } = new(
        true,
        220,
        0,
        LayoutEasingKind.EaseOut);
}

public sealed record LayoutSurfaceSettings(
    int LengthScalePercent,
    int ThicknessScalePercent,
    int GapDip,
    int CornerRadiusDip,
    int? WidthDip,
    int? HeightDip,
    bool SizeToContent,
    bool EdgeCollapseEnabled)
{
    public static LayoutSurfaceSettings Default { get; } = new(
        100,
        100,
        4,
        6,
        null,
        null,
        true,
        false);
}

public sealed record LayoutSlot(
    string SlotId,
    IReadOnlyList<LayoutElement> Children)
{
    public static LayoutSlot Empty(string slotId) => new(slotId, []);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LayoutWidgetElement), "widget")]
[JsonDerivedType(typeof(LayoutContainerElement), "container")]
public abstract record LayoutElement(
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
public abstract record WidgetSettings;

public sealed record ArtworkWidgetSettings(
    int CornerRadiusDip,
    bool UseMediaPrimaryColor,
    bool OpenSourceOnClick) : WidgetSettings;

public sealed record MediaTextWidgetSettings(
    MediaTextKind TextKind,
    bool EnableMarquee,
    int FontSizeDip,
    int MaxLines) : WidgetSettings;

public sealed record CommandWidgetSettings(
    MediaCommandKind Command,
    int ButtonSizeDip) : WidgetSettings;

public sealed record MetricsWidgetSettings(
    MetricKind Metric,
    bool OpenTaskManagerOnClick,
    int RefreshIntervalMilliseconds,
    IReadOnlyList<MetricKind> CycleMetrics) : WidgetSettings;

public sealed record SpectrumWidgetSettings(
    int BandCount,
    int RefreshRateHz,
    int SensitivityPercent) : WidgetSettings
{
    public const int MaximumBandCount = 9;
}

public sealed record SeparatorWidgetSettings(
    int ThicknessDip,
    int LengthDip) : WidgetSettings;

public sealed record LayoutWidgetElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    string TypeId,
    WidgetSettings Settings) : LayoutElement(InstanceId, Enabled, Geometry);

public sealed record LayoutContainerElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    LayoutContainerKind ContainerKind,
    LayoutFlowOrientation Orientation,
    LayoutContentAlignment ContentAlignment,
    LayoutContentAlignment SecondaryContentAlignment,
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
public sealed record LayoutEdgeContainer(
    string InstanceId,
    bool Enabled,
    LayoutEdge Edge,
    int OffsetDip,
    int TriggerThicknessDip,
    int ProximityDip,
    LayoutAnimationSettings Animation,
    LayoutSlot ExpandedSlot);

public sealed record LayoutProfile(
    LayoutProfileKey Key,
    PlayerLayoutMode LayoutMode,
    LayoutSurfaceSettings Surface,
    IReadOnlyList<LayoutContainerElement> InlineContainers,
    IReadOnlyList<LayoutEdgeContainer> EdgeContainers,
    // 仅用于读取 schema 1；规范化后始终清空，避免新编辑器重新暴露旧三槽位根节点。
    // Read-only schema-1 compatibility; normalization always clears it so the new editor cannot expose the legacy three-slot root.
    LayoutContainerElement? Root = null);

public sealed record LayoutDocument(
    int SchemaVersion,
    LayoutProfile Horizontal,
    LayoutProfile Vertical)
{
    public const int CurrentSchemaVersion = 3;

    public LayoutProfile Get(LayoutProfileKey key) => key switch
    {
        LayoutProfileKey.Vertical => Vertical,
        _ => Horizontal
    };

    public LayoutDocument WithProfile(LayoutProfile profile) => profile.Key switch
    {
        LayoutProfileKey.Horizontal => this with { Horizontal = profile },
        LayoutProfileKey.Vertical => this with { Vertical = profile },
        _ => this
    };
}

/// <summary>
/// 仅描述 schema 1/2 的四档案外壳；内部元素复用当前数据契约，未知旧字段由 JSON 读取器忽略。
/// Describes only the four-profile schema-1/2 envelope; inner elements reuse current contracts while unknown legacy fields are ignored.
/// </summary>
public sealed record LegacyLayoutDocument(
    int SchemaVersion,
    LayoutProfile TaskbarHorizontal,
    LayoutProfile TaskbarVertical,
    LayoutProfile FloatingHorizontal,
    LayoutProfile FloatingVertical);
