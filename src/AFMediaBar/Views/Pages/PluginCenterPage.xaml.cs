// 页面只呈现插件目录并管理可见期，业务下载不随页面卸载停止。
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>第三方插件下载与手动安装说明。</summary>
public partial class PluginCenterPage : INavigableView<PluginCenterViewModel>
{
    public PluginCenterViewModel ViewModel { get; }

    public PluginCenterPage(PluginCenterViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.Activate();
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Grid { Children.Count: 2 } grid ||
            grid.Children[1] is not FrameworkElement actions) return;
        var compact = e.NewSize.Width < 600;
        System.Windows.Controls.Grid.SetColumn(actions, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetRow(actions, compact ? 1 : 0);
        actions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        actions.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }
}
