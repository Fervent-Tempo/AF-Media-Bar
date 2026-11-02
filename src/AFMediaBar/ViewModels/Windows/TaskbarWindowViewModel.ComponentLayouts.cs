// 两种宿主共用的组件排序命令；视图仅提供意图，视图模型负责写入设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows;

public partial class TaskbarWindowViewModel
{
    [RelayCommand]
    private void CommitComponentPlacement(ComponentPlacementRequest? request)
    {
        if (request is null || request.State == BarLayoutState.Hover || !ComponentLayoutPolicy.CanMove(request.Component)) return;
        var root = SettingsManager.Current;
        var current = MonitorBarProfilePolicy.Project(root, request.Target, request.MonitorDeviceId);
        if (!ComponentLayoutPolicy.Resolve(current, request.State).SequenceEqual(request.OriginalEntries) || current.TaskbarBarPositionLocked) return;
        var profile = MonitorBarProfilePolicy.Capture(root, request.Target, request.MonitorDeviceId);
        var detached = profile.Detached.Where(position => position.Component != request.Component);
        if (request.Position is { } position) detached = detached.Append(position with { Component = request.Component });
        if (request.Position is null && ComponentLayoutPolicy.TryMove(current, request.State, request.Component, request.TargetIndex,
            request.OriginalEntries.Select(entry => entry.Component).ToArray(), out var reordered))
            profile = request.State == BarLayoutState.Idle ? profile with { Idle = reordered.ComponentLayouts!.Idle! }
                : profile with { Resident = reordered.ComponentLayouts!.Resident! };
        MonitorBarProfilePolicy.Store(root, profile with { Detached = detached.ToArray() });
    }
    [RelayCommand]
    private void CommitComponentReorder(ComponentReorderRequest? request)
    {
        if (request is null) return;
        var root = SettingsManager.Current;
        var settings = request.State != BarLayoutState.Hover && request.Target is { } target && request.MonitorDeviceId is { } monitor
            ? MonitorBarProfilePolicy.Project(root, target, monitor) : root;
        var entries = ComponentLayoutPolicy.Resolve(settings, request.State);
        if (!entries.SequenceEqual(request.OriginalEntries)) return;
        if (ComponentLayoutPolicy.TryMove(settings, request.State, request.Component, request.TargetIndex,
            entries.Select(entry => entry.Component).ToArray(), out var updated))
        {
            if (request.State != BarLayoutState.Hover && request.Target is { } scope && request.MonitorDeviceId is { } monitorId)
                MonitorBarProfilePolicy.SetLayout(root, scope, monitorId, request.State, updated.ComponentLayouts!.Get(request.State)!);
            else root.TaskbarExperience = updated;
        }
    }
}
