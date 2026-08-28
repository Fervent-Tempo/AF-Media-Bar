using System.Text.Json.Serialization;

namespace AFMediaBar.Layout.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LayoutWidgetElement), "widget")]
[JsonDerivedType(typeof(LayoutContainerElement), "container")]
public abstract record LayoutElement(
    string InstanceId,
    bool Enabled,
    LayoutGeometry Geometry,
    // Top-level containers use profile-grid coordinates; widgets in slots use container-local grid coordinates.
    LayoutGridRect? GridBounds = null);

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
