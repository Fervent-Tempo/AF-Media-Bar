// Taskbar docking engine, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Text;
using AFMediaBar.Classes.Utils;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 任务栏停靠服务：将媒体栏窗口嵌入到 Windows Explorer 任务栏中。
/// Taskbar docking service: embeds the media bar window into the Windows Explorer taskbar.
///
/// 职责 Responsibilities:
/// 1. 定位指定监视器的任务栏窗口（主任务栏或副任务栏）
///    Locate taskbar window on specified monitor (main or secondary)
/// 2. 将媒体栏窗口设置为任务栏的子窗口（停靠）
///    Set media bar window as child of taskbar (docking)
/// 3. 计算并应用媒体栏在任务栏中的位置和大小
///    Calculate and apply media bar position and size within taskbar
/// 4. 应用输入区域（点击穿透），使任务栏其他部分仍可交互
///    Apply input region (click-through) so rest of taskbar remains interactive
///
/// 算法 Algorithm:
/// 1. GetSelectedTaskbarHandle: 根据显示器设备标识查找对应的任务栏 HWND
///    Find taskbar HWND for the specified monitor device identifier
/// 2. DockWindow: 修改窗口样式为 WS_CHILD，并设置父窗口为任务栏
///    Change window style to WS_CHILD and set parent to taskbar
/// 3. SetWindowPosition: 计算物理像素位置并调用 SetWindowPos
///    Calculate physical pixel position and call SetWindowPos
/// 4. ApplyInputRegion: 创建 GDI 区域并通过 SetWindowRgn 限制输入区域
///    Create GDI region and restrict input area via SetWindowRgn
///
/// ⚠️ 注意 Note:
/// 此服务移植自 FluentFlyout (GPL-3.0-or-later)，包含复杂的 Win32 API 调用和多监视器处理。
/// This service is ported from FluentFlyout (GPL-3.0-or-later), contains complex Win32 API calls and multi-monitor handling.
/// </summary>
public class TaskbarDockService : ITaskbarDockService
{
    private readonly IDisplayMonitorService _displayMonitorService;

    /// <summary>创建使用共享显示器目录的任务栏停靠服务。 / Creates a taskbar docking service using the shared display catalog.</summary>
    public TaskbarDockService(IDisplayMonitorService displayMonitorService)
    {
        _displayMonitorService = displayMonitorService;
    }

    /// <summary>
    /// 获取选定监视器上的任务栏句柄（主任务栏或副任务栏）。
    /// Get taskbar handle on selected monitor (main or secondary taskbar).
    ///
    /// 算法 Algorithm:
    /// 1. 获取监视器列表，按设备标识匹配并应用回退
    ///    Get the monitor list, match by device identifier, and apply fallback
    /// 2. 检查主任务栏是否在目标监视器上
    ///    Check if main taskbar is on target monitor
    /// 3. 单监视器：直接返回主任务栏
    ///    Single monitor: return main taskbar directly
    /// 4. 双监视器：查找 Shell_SecondaryTrayWnd
    ///    Dual monitor: find Shell_SecondaryTrayWnd
    /// 5. 多监视器：枚举所有窗口查找匹配的副任务栏
    ///    Multi-monitor: enumerate all windows to find matching secondary taskbar
    /// </summary>
    public IntPtr GetSelectedTaskbarHandle(string? selectedMonitorDeviceId, out bool isMainTaskbarSelected)
    {
        var monitors = _displayMonitorService.GetMonitors();
        var selectedMonitor = DisplayTargetPolicy.ResolveFixed(monitors, selectedMonitorDeviceId);
        isMainTaskbarSelected = true;

        // 获取主任务栏并检查是否在选定的监视器上
        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (selectedMonitor is null)
            return mainHwnd;

        if (mainHwnd != IntPtr.Zero && string.Equals(
                MonitorUtil.GetMonitor(mainHwnd).deviceId,
                selectedMonitor.DeviceId,
                StringComparison.OrdinalIgnoreCase))
            return mainHwnd;

        if (monitors.Count == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (monitors.Count == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (hwnd != IntPtr.Zero && string.Equals(
                    MonitorUtil.GetMonitor(hwnd).deviceId,
                    selectedMonitor.DeviceId,
                    StringComparison.OrdinalIgnoreCase))
                return hwnd;

            isMainTaskbarSelected = true;
            return mainHwnd;
        }

        // 多于两个监视器：枚举所有窗口以查找属于选定监视器的 Shell_SecondaryTrayWnd
        // More than two monitors: enumerate all windows to find the Shell_SecondaryTrayWnd
        // that belongs to the selected monitor.

        IntPtr secondHwnd = IntPtr.Zero;
        StringBuilder className = new(256); // 256 是最大类名长度 256 is the maximum class name length
        IntPtr CheckWindowClass(IntPtr wnd)
        {
            GetClassName(wnd, className, className.Capacity);
            if (className.ToString() == "Shell_SecondaryTrayWnd" &&
                string.Equals(
                    MonitorUtil.GetMonitor(wnd).deviceId,
                    selectedMonitor.DeviceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return wnd;
            }
            return IntPtr.Zero;
        }

        // 在主任务栏线程中创建的窗口是常见情况且查找速度很快
        // Windows created in the main taskbar's thread are the common case and very fast to find.
        // 在罕见情况下 Shell_TrayWnd 和 Shell_SecondaryTrayWnd 位于不同线程
        // In rare cases Shell_TrayWnd and Shell_SecondaryTrayWnd live on different threads.
        if (mainHwnd != IntPtr.Zero)
        {
            uint threadId = GetWindowThreadProcessId(mainHwnd, IntPtr.Zero);
            EnumThreadWindows(threadId, (wnd, param) =>
            {
                secondHwnd = CheckWindowClass(wnd);
                return secondHwnd == IntPtr.Zero; // false 停止枚举 false stops the enumeration
            }, IntPtr.Zero);

            if (secondHwnd != IntPtr.Zero)
                return secondHwnd;
        }

        // 回退：搜索所有窗口 Fallback: search all windows.
        EnumWindows((wnd, param) =>
        {
            secondHwnd = CheckWindowClass(wnd);
            return secondHwnd == IntPtr.Zero;
        }, IntPtr.Zero);

        if (secondHwnd != IntPtr.Zero)
            return secondHwnd;

        // 在选定的监视器上未找到任务栏；回退到主任务栏
        // No taskbar found on the selected monitor; fall back to the main taskbar.
        isMainTaskbarSelected = true;
        return mainHwnd;
    }

    /// <summary>
    /// 获取任务栏的 DPI 缩放比例。
    /// Get taskbar DPI scaling factor.
    /// </summary>
    public double GetTaskbarDpiScale(IntPtr taskbarHandle)
    {
        if (taskbarHandle == IntPtr.Zero)
            return 0;
        return GetDpiForWindow(taskbarHandle) / 96.0;
    }

    /// <summary>
    /// 读取任务栏的物理像素矩形；句柄无效或 Win32 查询失败时返回 <see langword="false"/>。
    /// Reads the taskbar rectangle in physical pixels; returns <see langword="false"/> for an invalid handle or failed Win32 query.
    /// </summary>
    public bool TryGetTaskbarRect(IntPtr taskbarHandle, out RECT rect)
    {
        rect = default;
        return taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out rect);
    }

    /// <summary>
    /// 根据任务栏物理矩形判断其是否为左右侧竖向任务栏。
    /// Determines whether the taskbar is docked vertically from its physical rectangle.
    /// </summary>
    public bool IsTaskbarVertical(IntPtr taskbarHandle)
    {
        if (!TryGetTaskbarRect(taskbarHandle, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return height > width;
    }

    /// <summary>
    /// 将顶层媒体窗口转换为 Explorer 任务栏子窗口；调用方仍负责关闭前执行解挂。
    /// Converts the top-level media window into an Explorer taskbar child; the caller remains responsible for undocking before close.
    /// </summary>
    public void DockWindow(IntPtr windowHandle, IntPtr taskbarHandle)
    {
        if (windowHandle == IntPtr.Zero || taskbarHandle == IntPtr.Zero)
            return;

        // This prevents the window from trying to float above the taskbar as a separate entity
        int style = GetWindowLong(windowHandle, GWL_STYLE);
        style = (style & ~WS_POPUP) | WS_CHILD;
        SetWindowLong(windowHandle, GWL_STYLE, style);

        SetParent(windowHandle, taskbarHandle);
    }

    /// <summary>
    /// 隐藏并解除任务栏父子关系，再恢复顶层窗口样式，供安全销毁或重新停靠。
    /// Hides and detaches the taskbar child, then restores top-level styles for safe destruction or redocking.
    /// </summary>
    public void UndockWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        SetWindowPos(windowHandle, 0, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        SetParent(windowHandle, IntPtr.Zero);

        var style = GetWindowLong(windowHandle, GWL_STYLE);
        style = (style & ~WS_CHILD) | WS_POPUP;
        SetWindowLong(windowHandle, GWL_STYLE, style);
    }

    /// <summary>
    /// 将屏幕物理坐标转换为任务栏客户区坐标，并定位已停靠窗口。调用在窗口所属的 UI 线程上同步完成，
    /// 不强制显示窗口；定位与显隐因此可以保持"先落地稳定几何，再恢复显示"的顺序。
    /// Converts physical screen coordinates to taskbar-client coordinates and positions the docked window. The call completes synchronously on the UI
    /// thread that owns the window and never forces it visible, preserving the ordering "land stable geometry first, then restore visibility".
    /// </summary>
    public void SetWindowPosition(IntPtr windowHandle, IntPtr taskbarHandle, RECT taskbarRect, int width, int height)
    {
        if (windowHandle == IntPtr.Zero || taskbarHandle == IntPtr.Zero)
            return;

        // SetWindowPos positions the child relative to its parent, so convert screen coords first.
        POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
        if (!ScreenToClient(taskbarHandle, ref containerPos))
            return;

        SetWindowPos(windowHandle, 0,
            containerPos.X, containerPos.Y,
            width, height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// 合并给定物理矩形并转移 GDI 区域所有权给窗口，以限制任务栏子窗口的可交互范围。
    /// Combines the physical rectangles and transfers the resulting GDI region to the window to constrain its interactive area.
    /// </summary>
    public void ApplyInputRegion(IntPtr windowHandle, IReadOnlyList<RECT> rects)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
        foreach (var r in rects)
        {
            // skip empty rects
            if (r.Right <= r.Left || r.Bottom <= r.Top)
                continue;

            IntPtr newRgn = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            if (newRgn == IntPtr.Zero)
                goto on_error;

            if (CombineRgn(rgn, rgn, newRgn, RGN_OR) == 0)
            {
                DeleteObject(newRgn);
                goto on_error;
            }

            DeleteObject(newRgn);
        }

        if (!SetWindowRgn(windowHandle, rgn, true))
            goto on_error;

        return;

    on_error:
        // Regions not transferred to the window must be destroyed manually
        DeleteObject(rgn);
        SetWindowRgn(windowHandle, IntPtr.Zero, true);
    }
}
