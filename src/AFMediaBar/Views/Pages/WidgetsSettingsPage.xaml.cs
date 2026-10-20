// 页面只呈现参数与导航意图；设备枚举和取消由作用域内的编辑器协调。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现小组件参数与返回布局的入口。</summary>
public partial class WidgetsSettingsPage : INavigableView<WidgetsSettingsViewModel>
{
    public WidgetsSettingsViewModel ViewModel { get; }

    public WidgetsSettingsPage(WidgetsSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        await ViewModel.RefreshAsync();
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Widgets", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.Reset();
    }
}
