using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 读取 Explorer 主任务栏所在屏幕边缘，并提供纯几何回退；短暂不可用时返回空值而不阻断编辑。
/// Reads the Explorer primary-taskbar edge with a geometry-only fallback, returning null during transient unavailability without blocking editing.
/// </summary>
public static class TaskbarEdgeService
{
    /// <summary>
    /// 使用任务栏实际窗口矩形判断自动排布方向，和主窗口定位使用同一几何规则。
    /// Resolves automatic layout from the taskbar window rectangle, matching the main-window placement rule.
    /// </summary>
    public static bool? TryResolveCurrentVerticalLayout()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect) ||
            taskbarRect.Width <= 0 || taskbarRect.Height <= 0)
        {
            return null;
        }

        return taskbarRect.Height > taskbarRect.Width;
    }

    public static LayoutEdge? TryResolveCurrent()
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

    public static LayoutEdge Resolve(NativeMethods.Rect taskbar, NativeMethods.Rect monitor)
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

    public static bool IsAvailable(
        WindowHostMode hostMode,
        LayoutEdge edge,
        LayoutEdge? taskbarEdge)
    {
        return hostMode == WindowHostMode.Floating || taskbarEdge != edge;
    }
}
