using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>全局交互设置页面。 / Global interaction settings page.</summary>
public partial class InteractionPage : INavigableView<InteractionViewModel>
{
    public InteractionViewModel ViewModel { get; }
    public InteractionPage(InteractionViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("Common.Page.Interaction", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetInteraction(); }
}
