using System.Text.Json.Serialization;

namespace AFMediaBar.Models;

/// <summary>
/// 仅用于反序列化 schema 1/2/3 的 JSON；确定性迁移到 schema 4 后不会再用这些类型写盘。
/// These types exist only to deserialize schema 1/2/3 JSON; after the deterministic migration they are never persisted.
/// </summary>

/// <summary>
/// schema 3 的自动折叠容器（旧边缘容器）：只保存展开内容，折叠状态只保留触发区域。
/// Schema-3 auto-collapse container (legacy edge container): only expanded content is persisted; collapsed state keeps a trigger region.
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Schema3WidgetElement), "widget")]
[JsonDerivedType(typeof(Schema3ContainerElement), "container")]
public abstract record Schema3Element(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry);

public sealed record Schema3WidgetElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    string TypeId,
    WidgetSettings Settings,
    string? SkinId = null,
    int? SkinVersion = null,
    IReadOnlyDictionary<string, string>? SkinSettings = null) : Schema3Element(InstanceId, Enabled, Geometry);

public sealed record Schema3ContainerElement(
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
    Schema3Slot PrimarySlot,
    Schema3Slot SecondarySlot,
    Schema3Slot CollapsedSlot) : Schema3Element(InstanceId, Enabled, Geometry);

public sealed record Schema3Slot(
    string SlotId,
    IReadOnlyList<Schema3Element> Children)
{
    public static Schema3Slot Empty(string slotId) => new(slotId, []);
}

public sealed record Schema3EdgeContainer(
    string InstanceId,
    bool Enabled,
    LayoutEdge Edge,
    int OffsetDip,
    int TriggerThicknessDip,
    int ProximityDip,
    LayoutAnimationSettings Animation,
    Schema3Slot ExpandedSlot);

public sealed record Schema3Profile(
    LayoutProfileKey Key,
    PlayerLayoutMode LayoutMode,
    LayoutSurfaceSettings Surface,
    IReadOnlyList<Schema3ContainerElement> InlineContainers,
    IReadOnlyList<Schema3EdgeContainer> EdgeContainers,
    Schema3ContainerElement? Root = null);

/// <summary>
/// schema 3 的两档案外壳；JSON 外壳结构与当前 LayoutDocument 相同，但内部仍是 schema 3 形状。
/// Schema-3 two-profile envelope; the JSON envelope matches LayoutDocument but the inner profiles are schema-3 shaped.
/// </summary>
public sealed record Schema3LayoutDocument(
    int SchemaVersion,
    Schema3Profile Horizontal,
    Schema3Profile Vertical);

/// <summary>
/// 仅描述 schema 1/2 的四档案外壳；内部档案是 schema 3 形状，未知旧字段由 JSON 读取器忽略。
/// Describes only the four-profile schema-1/2 envelope; inner profiles are schema-3 shaped while unknown legacy fields are ignored.
/// </summary>
public sealed record LegacyLayoutDocument(
    int SchemaVersion,
    Schema3Profile TaskbarHorizontal,
    Schema3Profile TaskbarVertical,
    Schema3Profile FloatingHorizontal,
    Schema3Profile FloatingVertical);