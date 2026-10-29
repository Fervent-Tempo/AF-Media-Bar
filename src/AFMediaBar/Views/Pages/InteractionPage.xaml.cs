using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>呈现交互与快捷键设置，编辑器与服务由既有作用域拥有。</summary>
public partial class InteractionPage : INavigableView<InteractionViewModel>
{
    public InteractionViewModel ViewModel { get; }
    public InteractionPage(InteractionViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    { SettingsRevealAnimator.Play(sender as Panel); }
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("Common.Page.Interaction", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetInteraction(); }
}
