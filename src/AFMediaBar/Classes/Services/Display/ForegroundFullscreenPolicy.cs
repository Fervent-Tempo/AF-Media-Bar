// 前台全屏判定有两条入口：纯几何判断供测试，读取前台窗口的版本供宿主直接使用。
using System.Runtime.InteropServices;
using System.Text;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>判断前台窗口是否覆盖其显示器物理边界。 / Determines whether a foreground window covers its display's physical bounds.</summary>
public static class ForegroundFullscreenPolicy
{
    /// <summary>按允许的像素误差判断全屏覆盖。 / Tests fullscreen coverage with a physical-pixel tolerance.</summary>
    public static bool IsFullscreen(Rect windowBounds, Rect monitorBounds, double tolerance = 2)
    {
        if (windowBounds.IsEmpty || monitorBounds.IsEmpty ||
            windowBounds.Width <= 0 || windowBounds.Height <= 0 ||
            monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            return false;
        }

        var safeTolerance = double.IsFinite(tolerance) ? Math.Max(0, tolerance) : 0;
        return windowBounds.Left <= monitorBounds.Left + safeTolerance &&
               windowBounds.Top <= monitorBounds.Top + safeTolerance &&
               windowBounds.Right >= monitorBounds.Right - safeTolerance &&
               windowBounds.Bottom >= monitorBounds.Bottom - safeTolerance;
    }

    /// <summary>
    /// 读取当前前台窗口并判断它是否占满所属显示器。桌面、任务栏与本进程自己的窗口都算「不是全屏应用」，
    /// 否则切换桌面或点自己的菜单时会把媒体栏误藏。
    /// </summary>
    public static bool IsForegroundWindowFullscreen()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || !NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
                return false;

            NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            if (processId == Environment.ProcessId || processId == 0) return false;

            var className = new StringBuilder(128);
            NativeMethods.GetClassName(foreground, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;

            var monitor = MonitorUtil.GetMonitor(foreground);
            if (monitor.monitorArea.Width <= 0 || monitor.monitorArea.Height <= 0) return false;

            // 先用 DWM 的可见边框：无边框全屏窗口的 GetWindowRect 可能比真实可见区域更大。
            if (NativeMethods.DwmGetWindowAttribute(
                    foreground,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out var bounds,
                    Marshal.SizeOf<NativeMethods.RECT>()) != NativeMethods.S_OK &&
                !NativeMethods.GetWindowRect(foreground, out bounds))
            {
                return false;
            }

            var windowBounds = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            return IsFullscreen(windowBounds, monitor.monitorArea);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
