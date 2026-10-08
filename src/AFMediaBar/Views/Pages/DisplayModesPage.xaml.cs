// Presents actual hosting choices and taskbar layers; unsupported families remain informational cards.
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>Runtime display-family and taskbar layer settings.</summary>
public partial class DisplayModesPage : INavigableView<DisplayModesViewModel>
{
    /// <summary>Actual runtime mode and current taskbar settings.</summary>
    public DisplayModesViewModel ViewModel { get; }
    /// <summary>Creates the page in its configuration scope.</summary>
    public DisplayModesPage(DisplayModesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.DisplayModes", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetDisplayModes();
    }
}
