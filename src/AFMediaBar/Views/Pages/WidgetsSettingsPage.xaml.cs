// 页面只呈现参数与导航意图；设备枚举和取消由作用域内的编辑器协调。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现小音符与快速启动设置，编辑器与服务由既有作用域拥有。</summary>
public partial class WidgetsSettingsPage : INavigableView<WidgetsSettingsViewModel>
{
    public WidgetsSettingsViewModel ViewModel { get; }
    public TaskbarAppearanceViewModel Taskbar => ViewModel.Layout;
    public bool HasTaskbarSettings => true;

    public WidgetsSettingsPage(WidgetsSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        SettingsRevealAnimator.Play(sender as Panel);

    }

    private async void BrowseQuickLaunch_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = AFMediaBar.Resources.Translations.Get("Media.Dialog.Browse.Title"),
            Filter = AFMediaBar.Resources.Translations.Get("Media.Dialog.Browse.Filter"),
            Multiselect = false, CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) await ViewModel.Note.AddFileAsync(dialog.FileName);
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    {
        if (await SettingsResetDialog.ConfirmAsync("Settings.Page.NoteAndLaunch", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetSection(SettingsResetScope.QuickLaunch);
    }
}
