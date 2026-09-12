using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>全局交互设置页面。 / Global interaction settings page.</summary>
public partial class InteractionPage : INavigableView<InteractionViewModel>
{
    public InteractionViewModel ViewModel { get; }
    public InteractionPage(InteractionViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("交互页")) ViewModel.ResetInteraction(); }
}
