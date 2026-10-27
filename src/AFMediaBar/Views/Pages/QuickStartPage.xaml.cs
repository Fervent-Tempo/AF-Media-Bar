using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>本地入门教程与设置直达入口，不启动媒体或设备服务。</summary>
public partial class QuickStartPage : INavigableView<QuickStartViewModel>
{
    public QuickStartViewModel ViewModel { get; }

    public QuickStartPage(QuickStartViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        await ViewModel.ActivateAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs args) => ViewModel.Deactivate();

    private void OnTableMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs args)
    {
        if (PageLayout.ScrollViewer is not { } scroll) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta);
        args.Handled = true;
    }
}
