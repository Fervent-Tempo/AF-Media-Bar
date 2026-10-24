// 快速开始只呈现教程与资料；页面离开时释放可见期订阅。
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>设置首页的基础教程、版本亮点入口及播放器支持参考。</summary>
public partial class QuickStartPage : INavigableView<QuickStartViewModel>
{
    public QuickStartViewModel ViewModel { get; }

    public QuickStartPage(QuickStartViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();
}
