// 页面只编辑媒体卡片的显示开关；窗口生命周期由应用层按设置同步。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现媒体卡片的开关。</summary>
public partial class MediaCardPage : INavigableView<MediaCardViewModel>
{
    public MediaCardViewModel ViewModel { get; }

    public MediaCardPage(MediaCardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    // 等页面真正呈现完再补字体清单，避开入场动画与首帧布局。
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        try
        {
            if (await SettingsPageActivation.WaitForPresentationAsync(this, ViewModel.ContextCancellationToken))
                await ViewModel.RefreshInstalledFontsAsync(ViewModel.ContextCancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private void ResetFontsButton_OnClick(object sender, RoutedEventArgs e) => ViewModel.ResetFonts();
}
