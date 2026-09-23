using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 枚举显示器并解析固定或前台窗口目标，不拥有任何 WPF 窗口。
/// Enumerates displays and resolves fixed or foreground-window targets without owning WPF windows.
/// </summary>
public interface IDisplayMonitorService
{
    /// <summary>显示器拓扑快照发生变化时触发。 / Raised when the display-topology snapshot changes.</summary>
    event EventHandler? MonitorsChanged;

    /// <summary>返回最近一次显示器快照。 / Returns the latest display snapshot.</summary>
    IReadOnlyList<DisplayMonitorInfo> GetMonitors();

    /// <summary>重新枚举显示器并在拓扑变化时发布事件。 / Re-enumerates displays and publishes an event when topology changes.</summary>
    void Refresh();

    /// <summary>解析固定显示器，缺失时回退到主屏或首屏。 / Resolves a fixed display, falling back to the primary or first display.</summary>
    DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId);

    /// <summary>按通知目标模式解析显示器。 / Resolves a display using the notification target mode.</summary>
    DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId);

    /// <summary>
    /// 判断当前前台窗口是否在指定显示器上全屏；未指定显示器时检查前台窗口所在屏幕。
    /// Determines whether the foreground window is fullscreen on the specified display; when omitted, checks the foreground window's display.
    /// </summary>
    bool IsForegroundWindowFullscreen(string? monitorDeviceId = null);
}
