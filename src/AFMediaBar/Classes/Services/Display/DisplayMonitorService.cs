using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 基于 Win32 的显示器目录和前台窗口目标解析器。
/// Win32-backed display catalog and foreground-window target resolver.
/// </summary>
public sealed class DisplayMonitorService : IDisplayMonitorService
{
    private readonly object _gate = new();
    private IReadOnlyList<DisplayMonitorInfo> _monitors = Array.Empty<DisplayMonitorInfo>();

    /// <inheritdoc />
    public event EventHandler? MonitorsChanged;

    /// <inheritdoc />
    public IReadOnlyList<DisplayMonitorInfo> GetMonitors()
    {
        lock (_gate)
        {
            if (_monitors.Count > 0)
                return _monitors;
        }

        Refresh();
        lock (_gate)
        {
            return _monitors;
        }
    }

    /// <inheritdoc />
    public void Refresh()
    {
        DisplayMonitorInfo[] next;
        try
        {
            next = MonitorUtil.GetMonitors()
                .Where(IsValid)
                .Select(ToSnapshot)
                .ToArray();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[DisplayMonitorService] Monitor enumeration failed: {exception}");
            return;
        }

        var changed = false;
        lock (_gate)
        {
            if (!_monitors.SequenceEqual(next))
            {
                _monitors = next;
                changed = true;
            }
        }

        if (changed)
            MonitorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) =>
        DisplayTargetPolicy.ResolveFixed(GetMonitors(), deviceId);

    /// <inheritdoc />
    public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId)
    {
        string? foregroundDeviceId = null;
        if (mode == NotificationTargetMode.ForegroundWindow)
        {
            try
            {
                var foreground = NativeMethods.GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    var monitor = MonitorUtil.GetMonitor(foreground);
                    if (IsValid(monitor))
                        foregroundDeviceId = monitor.deviceId;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[DisplayMonitorService] Foreground monitor resolution failed: {exception}");
            }
        }

        return DisplayTargetPolicy.ResolveNotification(
            GetMonitors(),
            mode,
            fixedDeviceId,
            foregroundDeviceId);
    }

    /// <inheritdoc />
    public bool IsForegroundWindowFullscreen() => ForegroundFullscreenPolicy.IsForegroundWindowFullscreen();

    private static bool IsValid(MonitorUtil.MonitorInfo monitor) =>
        !string.IsNullOrWhiteSpace(monitor.deviceId) &&
        monitor.monitorArea.Width > 0 && monitor.monitorArea.Height > 0;

    private static DisplayMonitorInfo ToSnapshot(MonitorUtil.MonitorInfo monitor) => new(
        monitor.deviceId,
        string.IsNullOrWhiteSpace(monitor.deviceName) ? monitor.deviceId : monitor.deviceName,
        monitor.isPrimary,
        monitor.monitorArea,
        monitor.workArea,
        monitor.dpiX == 0 ? 96u : monitor.dpiX,
        monitor.dpiY == 0 ? 96u : monitor.dpiY);
}
