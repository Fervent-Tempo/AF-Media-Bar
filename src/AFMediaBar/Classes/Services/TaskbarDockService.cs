// Taskbar docking engine, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Text;
using AFMediaBar.Classes.Utils;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

public class TaskbarDockService : ITaskbarDockService
{
    public IntPtr GetSelectedTaskbarHandle(int selectedMonitorIndex, out bool isMainTaskbarSelected)
    {
        var monitors = MonitorUtil.GetMonitors();
        var selectedMonitor = monitors[Math.Clamp(selectedMonitorIndex, 0, monitors.Count - 1)];
        isMainTaskbarSelected = true;

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

        // More than two monitors: enumerate all windows to find the Shell_SecondaryTrayWnd
        // that belongs to the selected monitor.

        IntPtr secondHwnd = IntPtr.Zero;
        StringBuilder className = new(256); // 256 is the maximum class name length
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

        // Windows created in the main taskbar's thread are the common case and very fast to find.
        // In rare cases Shell_TrayWnd and Shell_SecondaryTrayWnd live on different threads.
        if (mainHwnd != IntPtr.Zero)
        {
            uint threadId = GetWindowThreadProcessId(mainHwnd, IntPtr.Zero);
            EnumThreadWindows(threadId, (wnd, param) =>
            {
                secondHwnd = CheckWindowClass(wnd);
                return secondHwnd == IntPtr.Zero; // false stops the enumeration
            }, IntPtr.Zero);

            if (secondHwnd != IntPtr.Zero)
                return secondHwnd;
        }

        // Fallback: search all windows.
        EnumWindows((wnd, param) =>
        {
            secondHwnd = CheckWindowClass(wnd);
            return secondHwnd == IntPtr.Zero;
        }, IntPtr.Zero);

        if (secondHwnd != IntPtr.Zero)
            return secondHwnd;

        // No taskbar found on the selected monitor; fall back to the main taskbar.
        isMainTaskbarSelected = true;
        return mainHwnd;
    }

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
