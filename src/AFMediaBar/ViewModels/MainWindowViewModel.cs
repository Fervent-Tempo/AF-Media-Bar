using AFMediaBar.Models;

namespace AFMediaBar.ViewModels;

/// <summary>Root presentation state for the media bar window.</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(WindowSettings settings)
    {
        Placement = new WindowPlacementViewModel(settings);
        TaskbarHost = new TaskbarHostViewModel();
    }

    public WindowPlacementViewModel Placement { get; }

    public TaskbarHostViewModel TaskbarHost { get; }

    public void ApplyWindowSettings(WindowSettings settings) =>
        Placement.ApplySettings(settings);

    public void ApplyPresentation(bool visible, bool expanded) =>
        Placement.SetPresentation(visible, expanded);

    public void ApplyRecovery(string? reason)
    {
        Placement.SetRecovery(reason);
        TaskbarHost.SetRecovery(reason);
    }
}
