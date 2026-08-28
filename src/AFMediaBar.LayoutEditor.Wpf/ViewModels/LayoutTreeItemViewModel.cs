using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.LayoutEditor.Wpf.ViewModels;

/// <summary>
/// Observable tree projection for containers, slots and functional components.
/// The immutable layout element remains the source of truth; this type owns only
/// editor selection and expansion state.
/// </summary>
public sealed partial class LayoutTreeItemViewModel : ObservableObject
{
    public LayoutTreeItemViewModel(
        string id,
        string labelResourceKey,
        string displayName,
        LayoutEditorNodeKind kind,
        object? model,
        LayoutTreeItemViewModel? parent = null)
    {
        InstanceId = id;
        LabelResourceKey = labelResourceKey;
        DisplayName = displayName;
        Kind = kind;
        Model = model;
        Parent = parent;
    }

    public string InstanceId { get; }
    public string LabelResourceKey { get; }
    public string DisplayName { get; }
    public LayoutEditorNodeKind Kind { get; }
    public object? Model { get; }
    public LayoutTreeItemViewModel? Parent { get; }
    public ObservableCollection<LayoutTreeItemViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isEnabled = true;
}

public enum LayoutEditorNodeKind
{
    Container,
    Slot,
    Widget,
    CollapseContainer
}
