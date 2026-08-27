using System.Text;
using System.Text.Json.Serialization;
namespace AFMediaBar.Layout.Models;

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

    // 仅供 schema 3 读取；schema 4 的折叠容器是 LayoutCollapseContainer，不再出现在 Containers 中。
    // Read-only for schema 3; schema 4 models collapse containers as LayoutCollapseContainer outside Containers.
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

/// <summary>
/// 描述档案的整数逻辑网格；单格尺寸用于把网格坐标转换为 DIP。
/// Describes the integer logical grid of a profile; the cell size converts grid coordinates to DIPs.
/// </summary>
public sealed record LayoutGridSettings(
    int Columns,
    int Rows,
    int CellSizeDip)
{
    public const int MinimumCells = 1;
    public const int MaximumColumns = 256;
    public const int MaximumRows = 256;
    public const int MinimumCellSizeDip = 2;
    public const int MaximumCellSizeDip = 32;

    public static LayoutGridSettings Default { get; } = new(48, 24, 8);

    public static LayoutGridSettings Normalize(LayoutGridSettings? grid)
    {
        grid ??= Default;
        return new LayoutGridSettings(
            Math.Clamp(grid.Columns, MinimumCells, MaximumColumns),
            Math.Clamp(grid.Rows, MinimumCells, MaximumRows),
            Math.Clamp(grid.CellSizeDip, MinimumCellSizeDip, MaximumCellSizeDip));
    }
}

/// <summary>
/// 不可变的整数网格矩形；容器使用档案全局坐标，槽位中的组件使用容器局部坐标。
/// Immutable integer grid rectangle; containers use profile-global coordinates while widgets in slots use container-local coordinates.
/// </summary>
public sealed record LayoutGridRect(
    int X,
    int Y,
    int Width,
    int Height)
{
    [JsonIgnore]
    public int Right => X + Width;

    [JsonIgnore]
    public int Bottom => Y + Height;

    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// 把负增量矩形规范化为非负矩形；编辑器拖动时请使用 FromDrag，这里只作为读入数据的安全网。
    /// Normalizes negative-width/height rectangles; editors should use FromDrag instead of relying on this safety net.
    /// </summary>
    [JsonIgnore]
    public LayoutGridRect Normalized
    {
        get
        {
            if (Width >= 0 && Height >= 0)
            {
                return this;
            }

            return new LayoutGridRect(
                Math.Min(X, X + Width),
                Math.Min(Y, Y + Height),
                Math.Abs(Width),
                Math.Abs(Height));
        }
    }

    /// <summary>
    /// record 默认的 ToString 会遍历计算属性 Normalized，导致无限递归；只输出原始字段。
    /// The default record ToString walks the computed Normalized property and recurses forever; emit raw fields only.
    /// </summary>
    public sealed override string ToString() =>
        $"{nameof(LayoutGridRect)}({X}, {Y}, {Width}x{Height})";

    public static LayoutGridRect Unit(int x, int y) => new(x, y, 1, 1);

    /// <summary>
    /// 从拖动起止两格（含起止格）构造规范矩形；可从任意方向拖动。
    /// Builds the canonical rectangle covering both drag corners inclusive, regardless of drag direction.
    /// </summary>
    public static LayoutGridRect FromDrag(int startX, int startY, int currentX, int currentY) =>
        new(
            Math.Min(startX, currentX),
            Math.Min(startY, currentY),
            Math.Abs(currentX - startX) + 1,
            Math.Abs(currentY - startY) + 1);

    /// <summary>
    /// 两个矩形是否在内部重叠；共享边或只在角上接触不算重叠。
    /// Whether two rectangles overlap in their interiors; shared edges and corner contact are not overlaps.
    /// </summary>
    public bool Overlaps(LayoutGridRect other) =>
        X < other.Right &&
        Right > other.X &&
        Y < other.Bottom &&
        Bottom > other.Y;

    /// <summary>
    /// 是否完全包含另一个矩形（含贴边）。
    /// Whether this rectangle fully contains the other, including flush edges.
    /// </summary>
    public bool Contains(LayoutGridRect other) =>
        other.X >= X &&
        other.Y >= Y &&
        other.Right <= Right &&
        other.Bottom <= Bottom;
}

/// <summary>
/// 折叠容器对非折叠锚点容器的依附；AttachmentSide 表示锚点被依附的边。
/// Describes how a collapse container attaches to a non-collapse anchor; AttachmentSide is the anchored side.
/// </summary>
public sealed record LayoutAttachment(
    string AnchorContainerId,
    LayoutEdge AttachmentSide);

/// <summary>
/// schema 4 的自动折叠容器：保存网格矩形、锚点依附、触发厚度、接近距离和动画。
/// Schema-4 auto-collapse container that persists grid bounds, anchor attachment, trigger thickness, proximity, and animation.
/// </summary>
public sealed record LayoutCollapseContainer(
    string InstanceId,
    bool Enabled,
    LayoutGridRect GridBounds,
    LayoutAttachment Attachment,
    int TriggerThicknessDip,
    int ProximityDip,
    LayoutAnimationSettings Animation,
    LayoutSlot ExpandedSlot);

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
    LayoutGeometry Geometry,
    // 顶层容器使用档案网格坐标；槽位中的组件使用容器局部网格坐标。schema 4 起由约束服务负责赋值。
    // Top-level containers use profile-grid coordinates; widgets in slots use container-local grid coordinates. Assigned by the constraint service since schema 4.
    LayoutGridRect? GridBounds = null);

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
    int ButtonSizeDip) : WidgetSettings
{
    /// <summary>
    /// 新建命令组件的默认交互尺寸；在默认 8 DIP 网格下对应 3x3 格。
    /// Default interaction size for new command widgets; this is 3x3 cells on the default 8 DIP grid.
    /// </summary>
    public const int DefaultButtonSizeDip = 24;
}

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
    WidgetSettings Settings,
    string? SkinId = null,
    int? SkinVersion = null,
    IReadOnlyDictionary<string, string>? SkinSettings = null,
    LayoutGridRect? GridBounds = null) : LayoutElement(InstanceId, Enabled, Geometry, GridBounds);

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
    LayoutGridRect? GridBounds = null) : LayoutElement(InstanceId, Enabled, Geometry, GridBounds);

/// <summary>
/// 单个横向或纵向档案：schema 4 起保存整数网格、顶层非折叠容器和折叠容器。
/// A single horizontal or vertical profile; schema 4 stores the integer grid, top-level non-collapse containers, and collapse containers.
/// </summary>
public sealed record LayoutProfile(
    LayoutProfileKey Key,
    PlayerLayoutMode LayoutMode,
    LayoutSurfaceSettings Surface,
    LayoutGridSettings Grid,
    IReadOnlyList<LayoutContainerElement> Containers,
    IReadOnlyList<LayoutCollapseContainer> CollapseContainers);

public sealed record LayoutDocument(
    int SchemaVersion,
    LayoutProfile Horizontal,
    LayoutProfile Vertical)
{
    public const int CurrentSchemaVersion = 5;

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
