// Named ITaskbarDockService to avoid clashing with Wpf.Ui's ITaskBarService.
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 任务栏停靠服务接口：定义将窗口嵌入 Explorer 任务栏的核心操作。
/// Taskbar docking service interface: defines core operations for embedding windows into Explorer taskbar.
///
/// 职责 Responsibilities:
/// 1. 定位任务栏窗口句柄（支持主任务栏和多监视器副任务栏）
///    Locate taskbar window handle (supports main taskbar and multi-monitor secondary taskbars)
/// 2. 将窗口停靠为任务栏的子窗口
///    Dock window as child of taskbar
/// 3. 计算和应用窗口在任务栏中的位置
///    Calculate and apply window position within taskbar
/// 4. 设置输入区域以实现点击穿透
///    Set input region for click-through behavior
///
/// 实现 Implementation:
/// TaskbarDockService 实现此接口，移植自 FluentFlyout (GPL-3.0-or-later)。
/// Implemented by TaskbarDockService, ported from FluentFlyout (GPL-3.0-or-later).
///
/// 使用场景 Usage:
/// TaskbarWindow 在初始化和位置更新时调用此服务的方法。
/// TaskbarWindow calls methods of this service during initialization and position updates.
/// </summary>
public interface ITaskbarDockService
{
    /// <summary>
    /// 查找指定监视器上的任务栏窗口（Shell_TrayWnd / Shell_SecondaryTrayWnd）。
    /// Finds the taskbar window (Shell_TrayWnd / Shell_SecondaryTrayWnd) on the monitor with the given index.
    ///
    /// 算法 Algorithm:
    /// 1. 单监视器：返回主任务栏 Single monitor: return main taskbar
    /// 2. 双监视器：查找 Shell_SecondaryTrayWnd Dual monitor: find Shell_SecondaryTrayWnd
    /// 3. 多监视器：枚举窗口查找匹配的副任务栏 Multi-monitor: enumerate windows for matching secondary
    ///
    /// ⚠️ 注意 Note:
    /// Explorer 重启期间可能返回 IntPtr.Zero。
    /// May return IntPtr.Zero while Explorer is (re)starting.
    /// </summary>
    /// <param name="selectedMonitorIndex">监视器索引（见 MonitorUtil.GetMonitors 顺序）Monitor index (see MonitorUtil.GetMonitors order)</param>
    /// <param name="isMainTaskbarSelected">输出参数：是否选中主任务栏 Output: whether main taskbar is selected</param>
    /// <returns>任务栏窗口句柄 Taskbar window handle</returns>
    IntPtr GetSelectedTaskbarHandle(int selectedMonitorIndex, out bool isMainTaskbarSelected);

    /// <summary>
    /// 获取任务栏的 DPI 缩放比例（GetDpiForWindow / 96）。
    /// Get DPI scaling factor of the taskbar (GetDpiForWindow / 96).
    /// </summary>
    /// <returns>DPI 缩放比例，句柄无效时返回 0 DPI scaling factor, returns 0 if handle is invalid</returns>
    double GetTaskbarDpiScale(IntPtr taskbarHandle);

    /// <summary>
    /// 获取任务栏窗口的屏幕坐标边界。
    /// Gets the taskbar window bounds in screen coordinates.
    /// </summary>
    bool TryGetTaskbarRect(IntPtr taskbarHandle, out RECT rect);

    /// <summary>
    /// 将窗口转换为任务栏的子窗口（WS_CHILD + SetParent）。
    /// Turns the window into a WS_CHILD of the taskbar (SetParent).
    ///
    /// 实现 Implementation:
    /// 1. 修改窗口样式为 WS_CHILD（移除 WS_POPUP）
    ///    Modify window style to WS_CHILD (remove WS_POPUP)
    /// 2. 调用 SetParent 将窗口设为任务栏的子窗口
    ///    Call SetParent to set window as child of taskbar
    /// </summary>
    void DockWindow(IntPtr windowHandle, IntPtr taskbarHandle);

    /// <summary>
    /// 定位并调整子窗口的大小，覆盖整个任务栏。
    /// Positions and sizes the child window over the taskbar.
    ///
    /// 坐标转换 Coordinate Conversion:
    /// 屏幕坐标 → 任务栏客户端坐标（ScreenToClient）后调用 SetWindowPos。
    /// Coordinates are converted from screen space to taskbar-client space (ScreenToClient) before SetWindowPos.
    /// </summary>
    void SetWindowPosition(IntPtr windowHandle, IntPtr taskbarHandle, RECT taskbarRect, int width, int height);

    /// <summary>
    /// 应用输入区域，使窗口仅在指定矩形区域可见和可交互（SetWindowRgn）。
    /// Clips the window so only the given rects are visible and hit-testable (SetWindowRgn).
    ///
    /// 用途 Purpose:
    /// 媒体栏只占任务栏一部分，其他区域需要点击穿透，让任务栏原有控件可交互。
    /// Media bar occupies only part of taskbar; other areas need click-through so original taskbar controls remain interactive.
    /// </summary>
    void ApplyInputRegion(IntPtr windowHandle, IReadOnlyList<RECT> rects);
}
