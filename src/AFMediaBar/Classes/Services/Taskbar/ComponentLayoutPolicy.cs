// 三状态布局的兼容、投影与编辑纯策略；设置页和实际媒体栏共用，不执行 I/O。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>解析组件布局，旧设置只在新状态尚未配置时参与，不维护第二份可写顺序。</summary>
public static class ComponentLayoutPolicy
{
    public const double HandleLength = 12;
    public static IReadOnlyList<TaskbarRestComponent> MetricComponents { get; } =
        [TaskbarRestComponent.SystemMemory, TaskbarRestComponent.SystemCpu, TaskbarRestComponent.SystemGpu, TaskbarRestComponent.ProcessMemory, TaskbarRestComponent.SystemNetwork];
    public static IReadOnlyList<TaskbarRestComponent> HoverComponents { get; } =
        [TaskbarRestComponent.PreviousTrack, TaskbarRestComponent.PlayPause, TaskbarRestComponent.NextTrack,
         TaskbarRestComponent.OutputDevice, TaskbarRestComponent.InputDevice, TaskbarRestComponent.Volume];
    public static bool IsMetric(TaskbarRestComponent component) => MetricComponents.Contains(component);
    public static MetricKind Metric(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.SystemCpu => MetricKind.SystemCpu,
        TaskbarRestComponent.SystemGpu => MetricKind.SystemGpu,
        TaskbarRestComponent.ProcessMemory => MetricKind.ProcessMemory,
        TaskbarRestComponent.SystemNetwork => MetricKind.SystemNetwork,
        _ => MetricKind.SystemMemory
    };
    public static TaskbarRestComponent Component(MetricKind metric) => metric switch
    {
        MetricKind.SystemCpu => TaskbarRestComponent.SystemCpu,
        MetricKind.SystemGpu => TaskbarRestComponent.SystemGpu,
        MetricKind.ProcessMemory => TaskbarRestComponent.ProcessMemory,
        MetricKind.SystemNetwork => TaskbarRestComponent.SystemNetwork,
        _ => TaskbarRestComponent.SystemMemory
    };
    public static bool CanMove(TaskbarRestComponent component) => component is not
        (TaskbarRestComponent.Artwork or TaskbarRestComponent.MediaText or TaskbarRestComponent.DragHandle);

    public static IReadOnlyList<ComponentLayoutEntry> ResolveAll(AppSettings settings, BarLayoutState state)
    {
        var legacy = Legacy(settings, state);
        if (settings.TaskbarExperience.ComponentLayouts?.Get(state) is not { } configured) return legacy;
        var allowed = legacy.Select(entry => entry.Component).ToHashSet();
        var entries = configured.Where(entry => entry is not null && allowed.Contains(entry.Component))
            .DistinctBy(entry => entry.Component).Select(entry => entry with { }).ToList();
        foreach (var entry in legacy)
            if (!entries.Any(existing => existing.Component == entry.Component)) entries.Add(entry with { IsVisible = false });
        var fixedItems = legacy.Where(entry => !CanMove(entry.Component)).Select(entry => entry.Component).ToArray();
        var ordered = fixedItems.Select(component => entries.Single(entry => entry.Component == component))
            .Concat(entries.Where(entry => CanMove(entry.Component))).ToArray();
        return state == BarLayoutState.Resident
            ? ordered.Select(entry => entry.Component == TaskbarRestComponent.MediaText ? entry with { IsVisible = true } : entry).ToArray()
            : ordered;
    }

    public static IReadOnlyList<ComponentLayoutEntry> Resolve(AppSettings settings, BarLayoutState state) => ResolveAll(settings, state)
        .Where(entry => state == BarLayoutState.Hover || settings.PerformanceComponent.DisplayMode == PerformanceDisplayMode.Parallel
            ? state == BarLayoutState.Hover || entry.Component != TaskbarRestComponent.Performance
            : !IsMetric(entry.Component)).ToArray();

    public static TaskbarExperienceSettings SetVisible(AppSettings settings, BarLayoutState state, TaskbarRestComponent component, bool visible)
    {
        var entries = ResolveAll(settings, state).Select(entry => entry.Component == component &&
            !(state == BarLayoutState.Resident && component == TaskbarRestComponent.MediaText) ? entry with { IsVisible = visible } : entry).ToArray();
        return Set(settings.TaskbarExperience, state, entries);
    }

    public static bool TryMove(AppSettings settings, BarLayoutState state, TaskbarRestComponent component, int targetIndex,
        IReadOnlyList<TaskbarRestComponent> expected, out TaskbarExperienceSettings result)
    {
        result = settings.TaskbarExperience;
        var projected = Resolve(settings, state).Select(entry => entry.Component).ToArray();
        if (!CanMove(component) || !projected.SequenceEqual(expected) || targetIndex < 0 || targetIndex >= projected.Length ||
            !CanMove(projected[targetIndex]) || !projected.Contains(component)) return false;
        var sourceIndex = Array.IndexOf(projected, component);
        if (sourceIndex == targetIndex) return false;
        var all = ResolveAll(settings, state).ToList();
        var source = all.Single(entry => entry.Component == component);
        var target = all.Single(entry => entry.Component == projected[targetIndex]);
        all.Remove(source);
        var destination = all.IndexOf(target) + (sourceIndex < targetIndex ? 1 : 0);
        all.Insert(destination, source);
        result = Set(settings.TaskbarExperience, state, all);
        return true;
    }

    public static TaskbarExperienceSettings Set(TaskbarExperienceSettings experience, BarLayoutState state, IReadOnlyList<ComponentLayoutEntry> entries) =>
        experience with { ComponentLayouts = (experience.ComponentLayouts ?? new()).With(state, entries) };

    public static TaskbarHoverControlsSettings HoverVisibility(AppSettings settings)
    {
        bool Visible(TaskbarRestComponent component) => Resolve(settings, BarLayoutState.Hover).Any(entry => entry.Component == component && entry.IsVisible);
        return settings.TaskbarExperience.HoverControls with
        {
            PreviousNextVisible = Visible(TaskbarRestComponent.PreviousTrack) || Visible(TaskbarRestComponent.NextTrack),
            PlayPauseVisible = Visible(TaskbarRestComponent.PlayPause),
            OutputDeviceVisible = Visible(TaskbarRestComponent.OutputDevice),
            InputDeviceVisible = Visible(TaskbarRestComponent.InputDevice),
            AudioControlVisible = Visible(TaskbarRestComponent.Volume)
        };
    }

    private static IReadOnlyList<ComponentLayoutEntry> Legacy(AppSettings settings, BarLayoutState state)
    {
        var experience = settings.TaskbarExperience;
        if (state == BarLayoutState.Hover)
        {
            var controls = experience.HoverControls;
            return HoverComponents.Select(component => new ComponentLayoutEntry(component, component switch
            {
                TaskbarRestComponent.PreviousTrack or TaskbarRestComponent.NextTrack => controls.PreviousNextVisible,
                TaskbarRestComponent.PlayPause => controls.PlayPauseVisible,
                TaskbarRestComponent.OutputDevice => controls.OutputDeviceVisible,
                TaskbarRestComponent.InputDevice => controls.InputDeviceVisible,
                _ => controls.AudioControlVisible
            })).ToArray();
        }
        var idle = TaskbarRestLayoutPolicy.ResolveIdleComponents(experience.IdleComponents);
        var selected = settings.PerformanceComponent.Normalize();
        bool Visible(TaskbarRestComponent component) => state == BarLayoutState.Idle ? idle.Contains(component) : component switch
        {
            TaskbarRestComponent.Artwork => experience.ArtworkVisible,
            TaskbarRestComponent.MediaText => true,
            TaskbarRestComponent.Spectrum => experience.SpectrumVisible,
            TaskbarRestComponent.Performance => experience.PerformanceVisible,
            TaskbarRestComponent.OutputDevice => experience.OutputDeviceVisible,
            TaskbarRestComponent.InputDevice => experience.InputDeviceVisible,
            TaskbarRestComponent.Volume => experience.VolumeVisible,
            TaskbarRestComponent.BluetoothBattery => settings.BluetoothBatteryWidget.Enabled,
            _ => false
        };
        var entries = new List<ComponentLayoutEntry>();
        foreach (var component in TaskbarRestLayoutPolicy.ResolveOrder(experience.RestComponentOrder).Where(component =>
            TaskbarRestLayoutPolicy.DefaultOrder.Contains(component)))
        {
            if (state == BarLayoutState.Idle && component == TaskbarRestComponent.MediaText) continue;
            if (component == TaskbarRestComponent.Performance)
            {
                foreach (var metric in selected.GetMetricOrder()) entries.Add(new(Component(metric), Visible(component) && selected.Metrics!.Contains(metric)));
                if (experience.RestComponentOrder?.Count > 0) entries.Add(new(component, Visible(component)));
            }
            else entries.Add(new(component, Visible(component)));
        }
        if (!entries.Any(entry => entry.Component == TaskbarRestComponent.Performance))
            entries.Add(new(TaskbarRestComponent.Performance, Visible(TaskbarRestComponent.Performance)));
        return entries;
    }
}
