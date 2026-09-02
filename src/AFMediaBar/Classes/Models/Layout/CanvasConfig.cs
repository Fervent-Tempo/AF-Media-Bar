namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 画布配置：定义布局的整体尺寸、背景、边框等视觉属性。
/// Canvas config: defines layout's overall size, background, border, and visual properties.
///
/// 职责 Responsibilities:
/// - 定义窗口的外观（尺寸、圆角、颜色）
///   Define window appearance (size, corner radius, colors)
/// - 不包含组件信息（组件由 ComponentConfig 列表定义）
///   Does not contain component info (components defined by ComponentConfig list)
/// </summary>
public record CanvasConfig
{
    /// <summary>画布宽度（像素） / Canvas width in pixels</summary>
    public double Width { get; init; } = 280;

    /// <summary>画布高度（像素） / Canvas height in pixels</summary>
    public double Height { get; init; } = 40;

    /// <summary>
    /// 背景颜色（ARGB 格式，如 "#80000000"）
    /// Background color (ARGB format, e.g., "#80000000")
    /// </summary>
    public string Background { get; init; } = "#80000000";

    /// <summary>圆角半径（像素） / Corner radius in pixels</summary>
    public double CornerRadius { get; init; } = 6;

    /// <summary>边框配置 / Border configuration</summary>
    public BorderConfig? Border { get; init; }

    /// <summary>视觉效果配置（模糊、透明度） / Visual effects config (blur, opacity)</summary>
    public EffectsConfig? Effects { get; init; }
}

/// <summary>
/// 边框配置：定义边框的粗细和颜色。
/// Border config: defines border thickness and color.
/// </summary>
public record BorderConfig
{
    /// <summary>边框粗细（像素） / Border thickness in pixels</summary>
    public double Thickness { get; init; } = 1.25;

    /// <summary>
    /// 边框颜色（ARGB 格式）
    /// Border color (ARGB format)
    /// </summary>
    public string Color { get; init; } = "#40FFFFFF";

    /// <summary>
    /// 是否仅应用顶部边框（适配任务栏模式）
    /// Whether to apply border only on top (for taskbar mode)
    /// </summary>
    public bool TopOnly { get; init; } = false;
}

/// <summary>
/// 视觉效果配置：定义模糊和透明度效果。
/// Visual effects config: defines blur and opacity effects.
/// </summary>
public record EffectsConfig
{
    /// <summary>背景模糊半径（像素） / Background blur radius in pixels</summary>
    public double Blur { get; init; } = 80;

    /// <summary>背景不透明度（0.0 - 1.0） / Background opacity (0.0 - 1.0)</summary>
    public double BackgroundOpacity { get; init; } = 0.4;
}
