// Reads existing display/docking services on a worker; no handles survive the query and no runtime settings are changed.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Adapts the existing platform services to a read-only settings context.</summary>
public sealed class SettingsEnvironmentReader(IDisplayMonitorService monitors, ITaskbarDockService taskbars) : ISettingsEnvironmentReader
{
    /// <inheritdoc />
    public Task<SettingsEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        // Copy preferences on the caller's UI thread; platform enumeration and HWND queries belong to the worker.
        var configured = SettingsManager.Current.TaskbarTargetMonitorDeviceIds?.ToArray();
        var legacy = SettingsManager.Current.TaskbarTargetMonitorDeviceId;
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            monitors.Refresh();
            var displays = monitors.GetMonitors();
            var targets = TaskbarTargetPolicy.ResolveDeviceIds(displays, configured, legacy);
            var result = new List<SettingsMonitorContext>();
            foreach (var deviceId in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var display = displays.FirstOrDefault(value => string.Equals(value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (display is null) continue;
                var handle = taskbars.GetSelectedTaskbarHandle(deviceId, out _);
                NativeMethods.RECT rect = default;
                var available = handle != IntPtr.Zero && taskbars.TryGetTaskbarRect(handle, out rect);
                LayoutOrientation? orientation = null;
                var compact = false;
                if (available)
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    var dpi = taskbars.GetTaskbarDpiScale(handle);
                    available = width > 0 && height > 0 && dpi > 0d;
                    if (available)
                    {
                        orientation = height > width ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
                        // A presentation hint only, not the persisted information-density setting or a third profile.
                        compact = Math.Min(width, height) / dpi < 40d;
                    }
                }
                result.Add(new(deviceId, display.DeviceName, display.IsPrimary, orientation, compact, available));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Only taskbar runtime exists in this release. Legacy WindowMode preview data must not activate other hosts.
            return new SettingsEnvironmentSnapshot(SettingsMode.Taskbar, result);
        }, cancellationToken);
    }
}
