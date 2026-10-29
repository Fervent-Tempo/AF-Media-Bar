using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现快捷控制设置，编辑器与服务由既有作用域拥有。</summary>
public partial class ComponentsSettingsPage : INavigableView<ContentLayoutViewModel>
{
    /// <summary>当前上下文的内容与布局编辑器组合。</summary>
    public ContentLayoutViewModel ViewModel { get; }
    public SettingsPreviewViewModel Preview { get; }

    /// <summary>创建内容与布局页。</summary>
    public ComponentsSettingsPage(ContentLayoutViewModel viewModel, SettingsPreviewViewModel preview)
    {
        ViewModel = viewModel;
        Preview = preview;
        Preview.Mode = SettingsPreviewMode.QuickControls;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as Panel);

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Settings.Page.QuickControls", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetSection(SettingsResetScope.QuickControls);
    }
}
