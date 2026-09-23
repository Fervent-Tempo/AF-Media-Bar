using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>解析任务栏媒体栏要承载在哪些显示器上。/ Resolves which monitor taskbars should host the media bar.</summary>
public static class TaskbarTargetPolicy
{
    /// <summary>旧设置文件里表示“所有任务栏”的标识，只用于兼容迁移，不再作为可选项写入。/ Legacy “all taskbars” identifier, read only for migration and never offered or written again.</summary>
    public const string LegacyAllTaskbarsDeviceId = "{AFMediaBar.AllTaskbars}";

    /// <summary>判断旧设置值是否要求在所有任务栏上显示。/ Determines whether a legacy setting requested every taskbar.</summary>
    public static bool IsLegacyAllTaskbars(string? deviceId) =>
        string.Equals(deviceId, LegacyAllTaskbarsDeviceId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 返回当前可用的显式目标；多选按主屏优先稳定排序。只有全部所选目标都断开时才临时回退到主屏，设置中的重连偏好不会被丢弃。
    /// Returns currently available explicit targets, stably ordered with the primary first. Only when every selected target is disconnected does it
    /// temporarily fall back to the primary monitor, without discarding reconnect preferences from settings.
    /// </summary>
    public static IReadOnlyList<string> ResolveDeviceIds(
        IReadOnlyList<DisplayMonitorInfo> monitors,
        IReadOnlyCollection<string>? configuredDeviceIds,
        string? legacyConfiguredDeviceId = null)
    {
        if (monitors.Count == 0)
            return [];

        var selected = (configuredDeviceIds ?? [])
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selected.Count == 0 && IsLegacyAllTaskbars(legacyConfiguredDeviceId))
            selected.UnionWith(monitors.Select(monitor => monitor.DeviceId));
        else if (selected.Count == 0 && !string.IsNullOrWhiteSpace(legacyConfiguredDeviceId))
            selected.Add(legacyConfiguredDeviceId);

        if (selected.Count > 0)
        {
            var resolved = monitors
                .Where(monitor => selected.Contains(monitor.DeviceId))
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenBy(monitor => monitor.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(monitor => monitor.DeviceId)
                .ToArray();
            if (resolved.Length > 0)
                return resolved;
        }

        var fixedMonitor = DisplayTargetPolicy.ResolveFixed(monitors, null);
        return fixedMonitor is null ? [] : [fixedMonitor.DeviceId];
    }
}
