using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>显示模式设置页面。 / Display-mode settings page.</summary>
public partial class DisplayModesPage : INavigableView<DisplayModesViewModel>
{
    public DisplayModesViewModel ViewModel { get; }
    public DisplayModesPage(DisplayModesViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    { if (await SettingsResetDialog.ConfirmAsync("显示模式页")) ViewModel.ResetDisplayModes(); }
}
