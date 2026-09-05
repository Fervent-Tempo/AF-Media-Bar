using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Settings;

/// <summary>
/// 任务栏媒体栏水平位置枚举。
/// Horizontal placement of the media bar on the taskbar.
/// </summary>
public enum TaskbarBarPosition
{
    Start = 0,   // 开始位置（左侧/上侧）Start position (left/top)
    Center = 1,  // 居中位置 Center position
    End = 2      // 结束位置（右侧/下侧）End position (right/bottom)
}

/// <summary>
/// 布局方向模式：自动、横向或纵向。
/// Layout orientation mode: auto, horizontal or vertical.
/// </summary>
public enum LayoutOrientationMode
{
    Auto = 0,       // 自动（根据任务栏位置自动选择）Auto (auto-select based on taskbar position)
    Horizontal = 1, // 横向 Horizontal
    Vertical = 2    // 纵向 Vertical
}

/// <summary>
/// 应用设置模型：定义所有可配置的应用行为。
/// Application settings model: defines all configurable application behaviors.
///
/// 职责 Responsibilities:
/// 1. 存储任务栏媒体栏的位置、监视器、外观等配置
///    Store taskbar media bar position, monitor, appearance configurations
/// 2. 提供默认值（通过属性初始化器）
///    Provide default values (via property initializers)
///
/// ⚠️ 注意 Note:
/// 当前为内存存储，未实现持久化。后续需添加注册表或 JSON 持久化逻辑。
/// Currently in-memory only; persistence (registry or JSON) to be added later.
/// </summary>
public class AppSettings
{
    /// <summary>是否启用任务栏媒体栏（停靠到任务栏）Whether the media bar is docked into the taskbar.</summary>
    public bool TaskbarBarEnabled { get; set; } = true;

    /// <summary>
    /// 选中的监视器索引，其任务栏将承载媒体栏（参见 <see cref="Utils.MonitorUtil.GetMonitors"/> 顺序）
    /// Index of the monitor whose taskbar hosts the bar (see <see cref="Utils.MonitorUtil.GetMonitors"/> order).
    /// </summary>
    public int TaskbarBarSelectedMonitor { get; set; }

    /// <summary>媒体栏在任务栏上的放置位置 Where on the taskbar the bar is placed.</summary>
    public TaskbarBarPosition Position { get; set; } = TaskbarBarPosition.Start;

    /// <summary>
    /// 显示模糊的专辑封面背景（类似 FluentFlyout 的 TaskbarWidgetBackgroundBlur，默认关闭）
    /// Show the blurred album-cover background (like FluentFlyout's TaskbarWidgetBackgroundBlur, off by default).
    /// </summary>
    public bool TaskbarBarBackgroundBlur { get; set; }

    /// <summary>
    /// 沿任务栏轴应用的额外手动偏移（物理像素）
    /// Extra manual offset (physical px) applied along the taskbar axis.
    /// </summary>
    public int TaskbarBarManualPadding { get; set; }

    /// <summary>
    /// 窗口模式：任务栏模式或灵动岛模式
    /// Window mode: taskbar mode or dynamic island mode
    /// </summary>
    public WindowMode WindowMode { get; set; } = WindowMode.Taskbar;

    /// <summary>
    /// 布局方向模式：自动、横向或纵向
    /// Layout orientation mode: auto, horizontal or vertical
    /// </summary>
    public LayoutOrientationMode LayoutOrientationMode { get; set; } = LayoutOrientationMode.Auto;

    /// <summary>灵动岛上次拖动位置。/ Last dragged position of the dynamic island.</summary>
    public double? DynamicIslandLeft { get; set; }
    public double? DynamicIslandTop { get; set; }

    /// <summary>暂停时隐藏到的边缘。/ Edge used for the paused retracted state.</summary>
    public DynamicIslandEdge DynamicIslandEdge { get; set; } = DynamicIslandEdge.Top;

    /// <summary>灵动岛是否已拖到桌面边缘并启用自动隐藏。/ Whether the dynamic island is docked to an edge for auto-hide.</summary>
    public bool DynamicIslandEdgeDocked { get; set; } = true;
}

/// <summary>
/// 设置管理器：提供全局访问当前设置实例。
/// Settings manager: provides global access to the current settings instance.
///
/// ⚠️ 架构注意 Architecture Note:
/// 使用静态属性提供全局访问，适合小型应用快速开发。
/// 大型应用建议通过 DI 注入 IOptions&lt;AppSettings&gt;。
/// Uses static property for global access, suitable for small apps and rapid development.
/// For larger apps, consider injecting IOptions&lt;AppSettings&gt; via DI.
/// </summary>
public static class SettingsManager
{
    public static AppSettings Current { get; set; } = new();

    /// <summary>
    /// 布局设置变更事件：当窗口模式或布局方向发生变化时触发。
    /// Layout settings changed event: fired when window mode or layout orientation changes.
    /// </summary>
    public static event EventHandler<LayoutSettingsChangedEventArgs>? LayoutSettingsChanged;

    /// <summary>
    /// 触发布局设置变更事件。
    /// Raise layout settings changed event.
    /// </summary>
    /// <param name="windowMode">新的窗口模式 / New window mode</param>
    /// <param name="orientationMode">新的布局方向模式 / New layout orientation mode</param>
    public static void RaiseLayoutSettingsChanged(WindowMode windowMode, LayoutOrientationMode orientationMode)
    {
        LayoutSettingsChanged?.Invoke(null, new LayoutSettingsChangedEventArgs(windowMode, orientationMode));
    }
}

/// <summary>
/// 布局设置变更事件参数。
/// Layout settings changed event arguments.
/// </summary>
public class LayoutSettingsChangedEventArgs : EventArgs
{
    /// <summary>新的窗口模式 / New window mode</summary>
    public WindowMode WindowMode { get; }

    /// <summary>新的布局方向模式 / New layout orientation mode</summary>
    public LayoutOrientationMode OrientationMode { get; }

    public LayoutSettingsChangedEventArgs(WindowMode windowMode, LayoutOrientationMode orientationMode)
    {
        WindowMode = windowMode;
        OrientationMode = orientationMode;
    }
}
