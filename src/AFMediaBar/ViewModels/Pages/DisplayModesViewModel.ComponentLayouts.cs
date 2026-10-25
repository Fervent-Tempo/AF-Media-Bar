// 三状态列表只表达设置意图；顺序和显隐均由共用布局策略解析。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

public partial class DisplayModesViewModel
{
    private readonly Dictionary<(BarLayoutState, TaskbarRestComponent), ComponentLayoutSettingItem> _layoutRows = new();
    public IReadOnlyList<ComponentLayoutSettingItem> ResidentLayoutEntries { get; private set; } = [];
    public IReadOnlyList<ComponentLayoutSettingItem> IdleLayoutEntries { get; private set; } = [];
    public IReadOnlyList<ComponentLayoutSettingItem> HoverLayoutEntries { get; private set; } = [];

    private IReadOnlyList<ComponentLayoutSettingItem> LayoutRows(BarLayoutState state)
    {
        return ComponentLayoutPolicy.Resolve(_configuration.Current, state).Select(entry =>
        {
            var key = (state, entry.Component);
            if (!_layoutRows.TryGetValue(key, out var row))
                _layoutRows[key] = row = new(this, state, entry.Component);
            row.Refresh();
            return row;
        }).ToArray();
    }

    private void RefreshComponentLayouts()
    {
        var resident = LayoutRows(BarLayoutState.Resident);
        var idle = LayoutRows(BarLayoutState.Idle);
        var hover = LayoutRows(BarLayoutState.Hover);
        if (!ResidentLayoutEntries.SequenceEqual(resident)) { ResidentLayoutEntries = resident; OnPropertyChanged(nameof(ResidentLayoutEntries)); }
        if (!IdleLayoutEntries.SequenceEqual(idle)) { IdleLayoutEntries = idle; OnPropertyChanged(nameof(IdleLayoutEntries)); }
        if (!HoverLayoutEntries.SequenceEqual(hover)) { HoverLayoutEntries = hover; OnPropertyChanged(nameof(HoverLayoutEntries)); }
    }

    [RelayCommand]
    private void ReorderLayout(SettingsReorderRequest? request)
    {
        if (_disposed || _isRefreshing || !_configuration.IsActive || request?.Item is not ComponentLayoutSettingItem row) return;
        var current = LayoutRows(row.State);
        if (request.TryResolve(current, item => item.Component, out _, out var target) &&
            ComponentLayoutPolicy.TryMove(_configuration.Current, row.State, row.Component, target,
                current.Select(item => item.Component).ToArray(), out var result))
            _configuration.SetTaskbarExperience(result);
    }

    public sealed class ComponentLayoutSettingItem(DisplayModesViewModel owner, BarLayoutState state, TaskbarRestComponent component) : ObservableObject
    {
        public BarLayoutState State { get; } = state;
        public TaskbarRestComponent Component { get; } = component;
        public bool CanMove => ComponentLayoutPolicy.CanMove(Component);
        public bool CanToggle => !(State == BarLayoutState.Resident && Component == TaskbarRestComponent.MediaText);
        public string Description => CanMove ? string.Empty : Translations.Get("ComponentLayout.FixedHead");
        public string DisplayName => Component switch
        {
            TaskbarRestComponent.SystemCpu => "CPU",
            TaskbarRestComponent.SystemGpu => "GPU",
            TaskbarRestComponent.SystemMemory => Translations.Get("Media.Metric.SystemMemory.Title"),
            TaskbarRestComponent.ProcessMemory => Translations.Get("Media.Metric.ProcessMemory.Title"),
            TaskbarRestComponent.SystemNetwork => Translations.Get("Media.Metric.SystemNetwork.Title"),
            TaskbarRestComponent.PreviousTrack => Translations.Get("Common.PreviousTrack"),
            TaskbarRestComponent.PlayPause => Translations.Get("Common.PlayPause"),
            TaskbarRestComponent.NextTrack => Translations.Get("Common.NextTrack"),
            _ => Translations.Get(State == BarLayoutState.Idle && Component == TaskbarRestComponent.Artwork
                ? TaskbarRestComponentSettingItem.QuickLaunchNoteNameKey : TaskbarRestComponentSettingItem.ResolveNameKey(Component))
        };
        public bool IsVisible
        {
            get => ComponentLayoutPolicy.ResolveAll(owner._configuration.Current, State).Any(item => item.Component == Component && item.IsVisible);
            set
            {
                if (!CanToggle || owner._disposed || owner._isRefreshing || !owner._configuration.IsActive || value == IsVisible) return;
                owner._configuration.SetTaskbarExperience(ComponentLayoutPolicy.SetVisible(owner._configuration.Current, State, Component, value));
            }
        }
        internal void Refresh() => OnPropertyChanged(string.Empty);
    }
}
