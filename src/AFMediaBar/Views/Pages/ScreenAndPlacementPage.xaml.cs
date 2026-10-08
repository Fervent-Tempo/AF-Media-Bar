// Presents screen and placement controls; the existing shared view model owns settings, monitors, and commands.
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>Independent settings page for monitor selection and taskbar placement.</summary>
public partial class ScreenAndPlacementPage : INavigableView<DisplayModesViewModel>
{
    /// <summary>Shared display settings state; this page creates no monitor service or settings owner.</summary>
    public DisplayModesViewModel ViewModel { get; }

    /// <summary>Creates the page using the display view model registered at the composition root.</summary>
    public ScreenAndPlacementPage(DisplayModesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs args) => SettingsRevealAnimator.Play(sender as Panel);
}
