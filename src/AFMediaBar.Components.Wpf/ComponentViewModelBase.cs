using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.Components.Wpf;

public abstract partial class ComponentViewModelBase(string instanceId) : ObservableObject
{
    public string InstanceId { get; } = instanceId;

    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string? warningCode;
}
