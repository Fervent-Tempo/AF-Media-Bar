using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 读取 Explorer 主任务栏所在屏幕边缘，并提供纯几何回退；短暂不可用时返回空值而不阻断编辑。
/// Reads the Explorer primary-taskbar edge with a geometry-only fallback, returning null during transient unavailability without blocking editing.
/// </summary>
internal static class TaskbarEdgeService
{
    internal static LayoutEdge? TryResolveCurrent()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        return monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)
            ? null
            : Resolve(taskbarRect, monitorInfo.Monitor);
    }

    internal static LayoutEdge Resolve(NativeMethods.Rect taskbar, NativeMethods.Rect monitor)
    {
        var distances = new (LayoutEdge Edge, int Distance)[]
        {
            (LayoutEdge.Top, Math.Abs(taskbar.Top - monitor.Top)),
            (LayoutEdge.Right, Math.Abs(taskbar.Right - monitor.Right)),
            (LayoutEdge.Bottom, Math.Abs(taskbar.Bottom - monitor.Bottom)),
            (LayoutEdge.Left, Math.Abs(taskbar.Left - monitor.Left))
        };
        return distances.OrderBy(item => item.Distance).First().Edge;
    }

    internal static bool IsAvailable(
        WindowHostMode hostMode,
        LayoutEdge edge,
        LayoutEdge? taskbarEdge)
    {
        return hostMode == WindowHostMode.Floating || taskbarEdge != edge;
    }
}
