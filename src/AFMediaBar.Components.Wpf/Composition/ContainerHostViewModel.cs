using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.Components.Wpf.Composition;

public sealed partial class ContainerHostViewModel : ObservableObject
{
    public ContainerHostViewModel(
        string instanceId,
        IComponentSettings settings,
        object model,
        IReadOnlyList<ComponentViewModelBase> primary,
        IReadOnlyList<ComponentViewModelBase> secondary)
    {
        InstanceId = instanceId;
        Settings = settings;
        Model = model;
        Primary = primary;
        Secondary = secondary;
    }

    public string InstanceId { get; }
    public IComponentSettings Settings { get; }
    public object Model { get; }
    public IReadOnlyList<ComponentViewModelBase> Primary { get; }
    public IReadOnlyList<ComponentViewModelBase> Secondary { get; }

    [ObservableProperty]
    private bool isPointerNear;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private int activeSlotIndex = -1;

    [ObservableProperty]
    private int transitionVersion;
}

public sealed record LayoutCompositionViewModel(
    LayoutProfile Profile,
    IReadOnlyList<ContainerHostViewModel> Containers,
    IReadOnlyList<ContainerHostViewModel> CollapseContainers,
    IReadOnlyDictionary<string, ComponentViewModelBase> Components);
