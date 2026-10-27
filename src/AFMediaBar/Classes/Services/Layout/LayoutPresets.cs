using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 布局预设：提供访问所有内置布局配置的入口。
/// Layout presets: provides entry point to access all built-in layout configurations.
///
/// 职责 Responsibilities:
/// 1. 作为布局系统的统一访问入口
///    Serve as unified access point for layout system
/// 2. 根据任务栏方向返回对应的布局
///    Return the layout for the taskbar orientation
/// 3. 确保布局选择的类型安全
///    Ensure type-safe layout selection
///
/// ⚠️ 架构约束 Architecture Constraints:
/// - 具体布局定义在独立的文件中（TaskbarHorizontalLayout.cs 等）
///   Specific layout definitions are in separate files (TaskbarHorizontalLayout.cs, etc.)
/// - 此类只负责路由，不包含布局的实际定义
///   This class only handles routing, not actual layout definitions
/// </summary>
public static class LayoutPresets
{
    /// <summary>
    /// 任务栏主题 - 横向布局（适配任务栏在屏幕顶部或底部）。
    /// Taskbar theme - horizontal layout (for taskbar at top or bottom).
    /// 详细定义见 TaskbarHorizontalLayout.cs / See TaskbarHorizontalLayout.cs for details.
    /// </summary>
    public static LayoutSchema TaskbarHorizontal => TaskbarHorizontalLayout.Create();

    /// <summary>
    /// 任务栏主题 - 竖向布局（适配任务栏在屏幕左侧或右侧）。
    /// Taskbar theme - vertical layout (for taskbar at left or right).
    /// 详细定义见 TaskbarVerticalLayout.cs / See TaskbarVerticalLayout.cs for details.
    /// </summary>
    public static LayoutSchema TaskbarVertical => TaskbarVerticalLayout.Create();

    /// <summary>
    /// 获取指定窗口模式和方向的布局。
    /// Get layout for specified window mode and orientation.
    ///
    /// 算法 Algorithm:
    /// 根据方向（横向/竖向）选择任务栏布局；旧模式值回退到任务栏。
    /// Select the horizontal or vertical taskbar layout; legacy mode values fall back to the taskbar.
    /// </summary>
    /// <param name="mode">窗口模式 / Window mode</param>
    /// <param name="orientation">布局方向 / Layout orientation</param>
    /// <param name="smallArtwork">是否把封面缩到原槽位的八成并居中 / Whether to shrink the artwork to 80% of its slot and center it</param>
    /// <returns>对应的布局配置 / Corresponding layout configuration</returns>
    public static LayoutSchema GetLayout(WindowMode mode, LayoutOrientation orientation, bool smallArtwork = false)
    {
        var layout = (mode, orientation) switch
        {
            (WindowMode.Taskbar, LayoutOrientation.Horizontal) => TaskbarHorizontal,
            (WindowMode.Taskbar, LayoutOrientation.Vertical) => TaskbarVertical,
            (_, LayoutOrientation.Vertical) => TaskbarVertical,
            _ => TaskbarHorizontal
        };
        return smallArtwork ? CompactArtwork(layout) : layout;
    }

    /// <summary>
    /// 封面缩到八成，圆角与占位图标按同一比例缩小；运行时布局按缩小后的尺寸重排相邻组件。
    /// </summary>
    private static LayoutSchema CompactArtwork(LayoutSchema layout)
    {
        const double Scale = 0.8;
        return layout with
        {
            Components = layout.Components.Select(component =>
            {
                if (!string.Equals(component.Id, LayoutComponentIds.Artwork, StringComparison.Ordinal)) return component;
                var bounds = component.Bounds;
                var size = Math.Max(1, Math.Round(bounds.Width * Scale));
                var properties = new Dictionary<string, object>(component.Properties);
                if (properties.TryGetValue("cornerRadius", out var radius) && radius is double cornerRadius)
                    properties["cornerRadius"] = Math.Round(cornerRadius * Scale, 2);
                if (properties.TryGetValue("placeholderIconSize", out var iconSize) && iconSize is double placeholderSize)
                    properties["placeholderIconSize"] = Math.Round(placeholderSize * Scale, 2);
                return component with
                {
                    Bounds = new ComponentBounds(
                        bounds.X + (bounds.Width - size) / 2,
                        bounds.Y + (bounds.Height - size) / 2,
                        size, size),
                    Properties = properties
                };
            }).ToList()
        };
    }
}
