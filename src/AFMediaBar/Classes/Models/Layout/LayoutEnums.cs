namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 布局方向：横向或竖向。
/// Layout orientation: horizontal or vertical.
///
/// 用途 Usage:
/// - 任务栏模式：根据任务栏位置（上下=横向，左右=竖向）自动选择
///   Taskbar mode: auto-select based on taskbar position (top/bottom=horizontal, left/right=vertical)
/// - 悬浮模式：用户手动选择
///   Floating mode: user manually selects
/// </summary>
public enum LayoutOrientation
{
    /// <summary>横向布局：宽度 > 高度，组件横向排列 / Horizontal: width > height, components arranged horizontally</summary>
    Horizontal,

    /// <summary>竖向布局：高度 > 宽度，组件竖向排列 / Vertical: height > width, components arranged vertically</summary>
    Vertical
}

/// <summary>
/// 窗口模式：任务栏模式或悬浮模式。
/// Window mode: taskbar mode or floating mode.
/// </summary>
public enum WindowMode
{
    /// <summary>任务栏模式：窗口嵌入系统任务栏 / Taskbar mode: window embedded in system taskbar</summary>
    Taskbar,

    /// <summary>悬浮模式：独立的桌面悬浮窗口 / Floating mode: independent desktop floating window</summary>
    Floating
}
