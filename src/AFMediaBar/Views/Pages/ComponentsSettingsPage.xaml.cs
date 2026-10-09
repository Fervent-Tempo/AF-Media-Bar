using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现常驻内容、文字布局、组件参数和控制面板，各编辑器共用当前上下文。</summary>
public partial class ComponentsSettingsPage : INavigableView<ContentLayoutViewModel>
{
    /// <summary>当前上下文的内容与布局编辑器组合。</summary>
    public ContentLayoutViewModel ViewModel { get; }

    /// <summary>创建内容与布局页。</summary>
    public ComponentsSettingsPage(ContentLayoutViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as Panel);

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.ContentLayout", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetContentLayout();
    }
}
