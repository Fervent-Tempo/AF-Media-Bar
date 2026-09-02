using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 布局预设：提供访问所有内置布局配置的入口。
/// Layout presets: provides entry point to access all built-in layout configurations.
///
/// 职责 Responsibilities:
/// 1. 作为布局系统的统一访问入口
///    Serve as unified access point for layout system
/// 2. 根据窗口模式和方向返回对应的布局
///    Return corresponding layout based on window mode and orientation
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
    /// 悬浮主题 - 横向布局（独立桌面窗口，横向显示）。
    /// Floating theme - horizontal layout (independent desktop window, horizontal display).
    /// 详细定义见 FloatingHorizontalLayout.cs / See FloatingHorizontalLayout.cs for details.
    /// </summary>
    public static LayoutSchema FloatingHorizontal => FloatingHorizontalLayout.Create();

    /// <summary>
    /// 悬浮主题 - 竖向布局（独立桌面窗口，竖向显示）。
    /// Floating theme - vertical layout (independent desktop window, vertical display).
    /// 详细定义见 FloatingVerticalLayout.cs / See FloatingVerticalLayout.cs for details.
    /// </summary>
    public static LayoutSchema FloatingVertical => FloatingVerticalLayout.Create();

    /// <summary>
    /// 获取指定窗口模式和方向的布局。
    /// Get layout for specified window mode and orientation.
    ///
    /// 算法 Algorithm:
    /// 1. 根据窗口模式（任务栏/悬浮）选择主题
    ///    Select theme based on window mode (taskbar/floating)
    /// 2. 根据方向（横向/竖向）选择布局
    ///    Select layout based on orientation (horizontal/vertical)
    /// </summary>
    /// <param name="mode">窗口模式 / Window mode</param>
    /// <param name="orientation">布局方向 / Layout orientation</param>
    /// <returns>对应的布局配置 / Corresponding layout configuration</returns>
    public static LayoutSchema GetLayout(WindowMode mode, LayoutOrientation orientation)
    {
        return (mode, orientation) switch
        {
            (WindowMode.Taskbar, LayoutOrientation.Horizontal) => TaskbarHorizontal,
            (WindowMode.Taskbar, LayoutOrientation.Vertical) => TaskbarVertical,
            (WindowMode.Floating, LayoutOrientation.Horizontal) => FloatingHorizontal,
            (WindowMode.Floating, LayoutOrientation.Vertical) => FloatingVertical,
            _ => TaskbarHorizontal // 默认使用任务栏横向布局
        };
    }
}
