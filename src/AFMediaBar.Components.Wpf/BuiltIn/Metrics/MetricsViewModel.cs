using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.System;

namespace AFMediaBar.Components.Wpf.BuiltIn.Metrics;

public partial class MetricsViewModel : ComponentViewModelBase
{
    private readonly Action? _metricsRequested;

    public MetricsViewModel(string instanceId, MetricsSettings settings, Action? metricsRequested = null) : base(instanceId)
    {
        Settings = settings;
        _metricsRequested = metricsRequested;
    }

    public MetricsSettings Settings { get; }
    [ObservableProperty] private string text = string.Empty;

    [RelayCommand]
    private void OpenTaskManager()
    {
        if (Settings.OpenTaskManagerOnClick) _metricsRequested?.Invoke();
    }
}
