// 页面只呈现参数与导航意图；设备枚举和取消由作用域内的编辑器协调。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现蓝牙电量设置，编辑器与服务由既有作用域拥有。</summary>
public partial class BluetoothBatteryPage : INavigableView<WidgetsSettingsViewModel>
{
    public WidgetsSettingsViewModel ViewModel { get; }

    public BluetoothBatteryPage(WidgetsSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        if (await SettingsPageActivation.WaitForPresentationAsync(this, ViewModel.ContextCancellationToken))
            await ViewModel.Battery.RefreshDevicesAsync();
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    {
        if (await SettingsResetDialog.ConfirmAsync("Settings.Page.BluetoothBattery", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetSection(SettingsResetScope.BluetoothBattery);
    }
}
