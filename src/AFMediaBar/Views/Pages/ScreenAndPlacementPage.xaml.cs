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

    private void OnModeCardClick(object sender, RoutedEventArgs args)
    {
        if (sender is not RadioButton { Tag: string selection } || !int.TryParse(selection, out var mode)) return;
        ViewModel.RuntimeModeSelection = mode;
        // 选择表示实际运行状态，等待宿主切换成功后再由新页面更新。
        FloatingModeCard.GetBindingExpression(RadioButton.IsCheckedProperty)?.UpdateTarget();
        TaskbarModeCard.GetBindingExpression(RadioButton.IsCheckedProperty)?.UpdateTarget();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs args) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.ScreenAndPlacement", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetScreenAndPlacement();
    }
}
