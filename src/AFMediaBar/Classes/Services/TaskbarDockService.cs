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
/// 1. GetSelectedTaskbarHandle: 根据监视器索引查找对应的任务栏 HWND
///    Find taskbar HWND for specified monitor index
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
    /// <summary>
    /// 获取选定监视器上的任务栏句柄（主任务栏或副任务栏）。
    /// Get taskbar handle on selected monitor (main or secondary taskbar).
    ///
    /// 算法 Algorithm:
    /// 1. 获取监视器列表，限定索引范围
    ///    Get monitor list, clamp index to valid range
    /// 2. 检查主任务栏是否在目标监视器上
    ///    Check if main taskbar is on target monitor
    /// 3. 单监视器：直接返回主任务栏
    ///    Single monitor: return main taskbar directly
    /// 4. 双监视器：查找 Shell_SecondaryTrayWnd
    ///    Dual monitor: find Shell_SecondaryTrayWnd
    /// 5. 多监视器：枚举所有窗口查找匹配的副任务栏
    ///    Multi-monitor: enumerate all windows to find matching secondary taskbar
    /// </summary>
    public IntPtr GetSelectedTaskbarHandle(int selectedMonitorIndex, out bool isMainTaskbarSelected)
    {
        var monitors = MonitorUtil.GetMonitors();
        var selectedMonitor = monitors[Math.Clamp(selectedMonitorIndex, 0, monitors.Count - 1)];
        isMainTaskbarSelected = true;

        // 获取主任务栏并检查是否在选定的监视器上
        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (mainHwnd != IntPtr.Zero && MonitorUtil.GetMonitor(mainHwnd).deviceId == selectedMonitor.deviceId)
            return mainHwnd;

        if (monitors.Count == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (monitors.Count == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (hwnd != IntPtr.Zero && MonitorUtil.GetMonitor(hwnd).deviceId == selectedMonitor.deviceId)
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
                MonitorUtil.GetMonitor(wnd).deviceId == selectedMonitor.deviceId)
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

    public bool TryGetTaskbarRect(IntPtr taskbarHandle, out RECT rect)
    {
        rect = default;
        return taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out rect);
    }

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

    public void SetWindowPosition(IntPtr windowHandle, IntPtr taskbarHandle, RECT taskbarRect, int width, int height)
    {
        if (windowHandle == IntPtr.Zero || taskbarHandle == IntPtr.Zero)
            return;

        // SetWindowPos positions the child relative to its parent, so convert screen coords first.
        POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
        ScreenToClient(taskbarHandle, ref containerPos);

        SetWindowPos(windowHandle, 0,
            containerPos.X, containerPos.Y,
            width, height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
    }

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
