using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现静置层组件的开关和参数，状态由专属组件视图模型持有。</summary>
public partial class ComponentsSettingsPage : INavigableView<ComponentsSettingsViewModel>
{
    /// <summary>当前上下文的组件设置状态。</summary>
    public ComponentsSettingsViewModel ViewModel { get; }

    /// <summary>创建组件设置页。</summary>
    public ComponentsSettingsPage(ComponentsSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as Panel);

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Components"))
            ViewModel.ResetComponents();
    }
}
