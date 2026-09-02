namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 组件边界配置：定义组件在画布中的位置和尺寸。
/// Component bounds config: defines component position and size on canvas.
///
/// 坐标系 Coordinate System:
/// - 原点 (0,0) 在画布左上角 / Origin (0,0) at canvas top-left
/// - X 轴向右递增 / X-axis increases rightward
/// - Y 轴向下递增 / Y-axis increases downward
/// </summary>
public record ComponentBounds
{
    /// <summary>X 坐标（距离左边缘的像素） / X coordinate (pixels from left edge)</summary>
    public double X { get; init; }

    /// <summary>Y 坐标（距离顶边缘的像素） / Y coordinate (pixels from top edge)</summary>
    public double Y { get; init; }

    /// <summary>宽度（像素） / Width in pixels</summary>
    public double Width { get; init; }

    /// <summary>高度（像素） / Height in pixels</summary>
    public double Height { get; init; }

    public ComponentBounds() { }

    public ComponentBounds(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// 组件配置：定义单个 UI 组件的类型、位置和属性。
/// Component config: defines a single UI component's type, position, and properties.
///
/// 职责 Responsibilities:
/// - 存储组件的静态配置信息
///   Store component's static configuration
/// - 不包含运行时状态或业务逻辑
///   Does not contain runtime state or business logic
/// </summary>
public record ComponentConfig
{
    /// <summary>组件唯一标识符 / Component unique identifier</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 组件类型：Artwork, MediaText, PlaybackControls 等
    /// Component type: Artwork, MediaText, PlaybackControls, etc.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>组件边界（位置和尺寸） / Component bounds (position and size)</summary>
    public ComponentBounds Bounds { get; init; } = new();

    /// <summary>
    /// 组件属性字典：存储特定于组件类型的配置。
    /// Component properties dictionary: stores type-specific configurations.
    ///
    /// 示例 Examples:
    /// - Artwork: { "cornerRadius": 5, "showPlaceholder": true }
    /// - MediaText: { "titleFontSize": 13, "showArtist": true }
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}
