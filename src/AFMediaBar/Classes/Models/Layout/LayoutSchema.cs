namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 布局架构：定义完整的 UI 布局配置（画布 + 组件列表）。
/// Layout schema: defines complete UI layout configuration (canvas + component list).
///
/// 职责 Responsibilities:
/// 1. 存储布局的所有静态配置信息
///    Store all static configuration for a layout
/// 2. 作为布局预设和布局渲染引擎之间的数据契约
///    Serve as data contract between layout presets and layout render engine
/// 3. 支持横向/竖向两种方向的布局
///    Support both horizontal and vertical orientations
///
/// ⚠️ 架构约束 Architecture Constraints:
/// - 此类是纯数据结构（record），不包含业务逻辑
///   This is a pure data structure (record), no business logic
/// - 只定义配置，不负责渲染（渲染由 LayoutRenderEngine 负责）
///   Only defines configuration, not responsible for rendering (LayoutRenderEngine handles that)
/// </summary>
public record LayoutSchema
{
    /// <summary>布局方向（横向或竖向） / Layout orientation (horizontal or vertical)</summary>
    public LayoutOrientation Orientation { get; init; } = LayoutOrientation.Horizontal;

    /// <summary>画布配置（尺寸、背景、边框、效果） / Canvas config (size, background, border, effects)</summary>
    public CanvasConfig Canvas { get; init; } = new();

    /// <summary>
    /// 组件列表：定义布局中所有 UI 组件的配置。
    /// Component list: defines all UI component configurations in the layout.
    ///
    /// 组件按声明顺序渲染（先声明的在底层，后声明的在顶层）。
    /// Components rendered in declaration order (earlier = bottom layer, later = top layer).
    /// </summary>
    public List<ComponentConfig> Components { get; init; } = new();

    /// <summary>
    /// 布局描述（可选）：用于调试和文档。
    /// Layout description (optional): for debugging and documentation.
    /// </summary>
    public string? Description { get; init; }
}
