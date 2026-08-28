using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.ViewModels;

namespace AFMediaBar.Settings;

/// <summary>Presentation state owned by the settings window shell.</summary>
public partial class SettingsWindowViewModel : ViewModelBase
{
    public const double ExpandedNavigationPaneWidth = 220;
    public const double CollapsedNavigationPaneWidth = 56;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationPaneWidth))]
    private bool isNavigationPaneExpanded = true;

    public double NavigationPaneWidth =>
        IsNavigationPaneExpanded ? ExpandedNavigationPaneWidth : CollapsedNavigationPaneWidth;

    [RelayCommand]
    private void ToggleNavigationPane() =>
        IsNavigationPaneExpanded = !IsNavigationPaneExpanded;
}
