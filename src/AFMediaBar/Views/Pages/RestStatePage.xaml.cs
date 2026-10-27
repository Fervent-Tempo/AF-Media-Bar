// 常驻页只呈现显隐、排序和无媒体保留项，编辑器由页面作用域拥有。
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>常驻状态的唯一设置入口。</summary>
public partial class RestStatePage : INavigableView<RestStateViewModel>
{
    public RestStateViewModel ViewModel { get; }
    public RestStatePage(RestStateViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs args) => SettingsRevealAnimator.Play(sender as Panel);
    private void OnPageUnloaded(object sender, RoutedEventArgs args) => SettingsRevealAnimator.Cancel(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    {
        var cancellation = ViewModel.ContextCancellationToken;
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.RestState", cancellationToken: cancellation) && !cancellation.IsCancellationRequested)
            ViewModel.Reset();
    }
}
