// 按逻辑显示器投影布局；读取不创建配置，写入只替换目标记录，不发布平台状态。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>每屏布局的继承、投影及原子替换。</summary>
public static class MonitorBarProfilePolicy
{
    /// <summary>共享设备采样按当前模式的已选屏幕汇总需求，已覆盖的列表不再读取旧全局开关。</summary>
    public static bool IsRequested(AppSettings settings, TaskbarRestComponent component, IReadOnlyList<string>? defaultMonitorIds = null)
    {
        var target = settings.WindowMode == AFMediaBar.Classes.Models.Layout.WindowMode.FloatingBar
            ? BarConfigurationTarget.FloatingBar : BarConfigurationTarget.Taskbar;
        var ids = target == BarConfigurationTarget.FloatingBar ? settings.FloatingBar.MonitorDeviceIds : settings.TaskbarTargetMonitorDeviceIds ?? [];
        if (ids.Count == 0 && defaultMonitorIds is { Count: > 0 }) ids = defaultMonitorIds;
        bool Requested(MonitorBarProfile? profile) => new[] { BarLayoutState.Resident, BarLayoutState.Idle }.Any(state =>
            profile is null ? ComponentLayoutPolicy.Resolve(settings, state).Any(entry => entry.Component == component && entry.IsVisible) :
                (state == BarLayoutState.Idle ? profile.Idle : profile.Resident).Any(entry => entry.Component == component && entry.IsVisible));
        if (ids.Count > 0) return ids.Any(id => Requested(Find(settings, target, id)));
        return Requested(null) || settings.MonitorBarProfiles.Where(profile => profile.Target == target).Any(Requested);
    }
    public static bool Same(MonitorBarProfile? left, MonitorBarProfile? right) => ReferenceEquals(left, right) ||
        left is not null && right is not null && left.Target == right.Target &&
        string.Equals(left.MonitorDeviceId, right.MonitorDeviceId, StringComparison.OrdinalIgnoreCase) &&
        left.BodyPrimaryDip == right.BodyPrimaryDip && left.BodyAlignment == right.BodyAlignment &&
        left.Resident.SequenceEqual(right.Resident) && left.Idle.SequenceEqual(right.Idle) && left.Detached.SequenceEqual(right.Detached);
    public static MonitorBarProfile? Find(AppSettings settings, BarConfigurationTarget target, string? monitorId) =>
        settings.MonitorBarProfiles.FirstOrDefault(profile => profile.Target == target &&
            string.Equals(profile.MonitorDeviceId, monitorId, StringComparison.OrdinalIgnoreCase));

    public static MonitorBarProfile Capture(AppSettings settings, BarConfigurationTarget target, string monitorId) =>
        Find(settings, target, monitorId) ?? new(target, monitorId)
        {
            Resident = ComponentLayoutPolicy.ResolveAll(settings, BarLayoutState.Resident).Select(entry => entry with { }).ToArray(),
            Idle = ComponentLayoutPolicy.ResolveAll(settings, BarLayoutState.Idle).Select(entry => entry with { }).ToArray()
        };

    public static AppSettings Project(AppSettings settings, BarConfigurationTarget target, string? monitorId)
    {
        var result = target == BarConfigurationTarget.FloatingBar
            ? (settings.FloatingBar.Profile ?? BarDisplaySettings.Capture(settings)).ApplyTo(settings) : settings.Clone();
        if (Find(settings, target, monitorId) is not { } profile) return result;
        result.TaskbarExperience = result.TaskbarExperience with
        {
            ComponentLayouts = (result.TaskbarExperience.ComponentLayouts ?? new()) with
            { Resident = profile.Resident, Idle = profile.Idle }
        };
        if (profile.BodyAlignment is { } alignment)
        {
            result.Position = alignment;
            result.TaskbarBarManualPadding = 0;
        }
        return result;
    }

    public static void Store(AppSettings settings, MonitorBarProfile profile) =>
        settings.MonitorBarProfiles = settings.MonitorBarProfiles.Where(existing => existing.Target != profile.Target ||
            !string.Equals(existing.MonitorDeviceId, profile.MonitorDeviceId, StringComparison.OrdinalIgnoreCase))
            .Append(profile.Normalize()).ToArray();

    public static void SetLayout(AppSettings settings, BarConfigurationTarget target, string monitorId,
        BarLayoutState state, IReadOnlyList<ComponentLayoutEntry> entries)
    {
        if (state == BarLayoutState.Hover) throw new ArgumentOutOfRangeException(nameof(state));
        var profile = Capture(settings, target, monitorId);
        Store(settings, state == BarLayoutState.Idle ? profile with { Idle = entries } : profile with { Resident = entries });
    }

    public static void SetDetached(AppSettings settings, BarConfigurationTarget target, string monitorId,
        TaskbarRestComponent component, DetachedComponentPosition? position)
    {
        var profile = Capture(settings, target, monitorId);
        var remaining = profile.Detached.Where(item => item.Component != component);
        Store(settings, profile with { Detached = position is null ? remaining.ToArray() : remaining.Append(position with { Component = component }).ToArray() });
    }

    public static void ResetPlacement(AppSettings settings, AppSettings defaults, BarConfigurationTarget target, string monitorId)
    {
        var profile = Capture(settings, target, monitorId);
        var saved = Find(defaults, target, monitorId);
        Store(settings, profile with
        {
            Detached = saved?.Detached ?? [],
            BodyPrimaryDip = saved?.BodyPrimaryDip,
            BodyAlignment = saved?.BodyAlignment
        });
    }
}
