using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Win32;

namespace AFMediaBar.Views.Pages;

/// <summary>额外功能设置页。/ Settings page for auxiliary features.</summary>
public partial class ExtraFeaturesPage : INavigableView<ExtraFeaturesViewModel>
{
    public ExtraFeaturesViewModel ViewModel { get; }

    public ExtraFeaturesPage(ExtraFeaturesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as System.Windows.Controls.Panel);

    private async void AddDetectedSource_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement { DataContext: MediaSourceSettingItem item })
            return;
        if (item.Descriptor.CanQuickLaunch)
            ViewModel.AddDetectedSourceCommand.Execute(item);
        else
            await BrowseQuickLaunchAsync(item.Descriptor);
    }

    private async Task BrowseQuickLaunchAsync(MediaSourceDescriptor? source)
    {
        // 对话框在打开的这一刻取文案：它是系统窗口，不会跟随 XAML 的动态资源刷新，因此每次点击都重新取一次。
        // The dialog reads its text at the moment it opens: it is a system window that does not follow the XAML dynamic
        // resources, so the text is fetched again on every click.
        var dialog = new OpenFileDialog
        {
            Title = Translations.Get("Media.Dialog.Browse.Title"),
            Filter = Translations.Get("Media.Dialog.Browse.Filter"),
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
            await ViewModel.AddQuickLaunchFileAsync(dialog.FileName, source);
    }

    private async void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.MediaAndNotifications"))
            ViewModel.ResetExtraFeatures();
    }

}
