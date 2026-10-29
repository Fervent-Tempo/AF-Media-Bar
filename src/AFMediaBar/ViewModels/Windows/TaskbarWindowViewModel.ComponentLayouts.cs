// 两种宿主共用的组件排序命令；视图仅提供意图，视图模型负责写入设置。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows;

public partial class TaskbarWindowViewModel
{
    [RelayCommand]
    private void CommitComponentReorder(ComponentReorderRequest? request)
    {
        if (request is null) return;
        var settings = SettingsManager.Current;
        var entries = ComponentLayoutPolicy.Resolve(settings, request.State);
        if (!entries.SequenceEqual(request.OriginalEntries)) return;
        if (ComponentLayoutPolicy.TryMove(settings, request.State, request.Component, request.TargetIndex,
            entries.Select(entry => entry.Component).ToArray(), out var updated))
            settings.TaskbarExperience = updated;
    }
}
