// 页面只表达重命名、重置和导航意图；排序预览使用共用行为，设备与持久化由视图模型协调。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>输入输出设备、别名、顺序与通信角色的全局配置。</summary>
public partial class DeviceManagementPage : INavigableView<AudioDeviceSettingsViewModel>
{
    public AudioDeviceSettingsViewModel ViewModel { get; }
    public DeviceManagementPage(AudioDeviceSettingsViewModel viewModel)
    { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    { SettingsRevealAnimator.Play(sender as Panel); await ViewModel.RefreshAsync(); }
    private void DeviceName_MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: AudioDevicePermissionItemViewModel row } element || row.IsRenaming) return;
        element.Focus();
        if (args.ClickCount != 2) return;
        row.BeginRenameCommand.Execute(null);
        args.Handled = true;
    }
    private void DeviceName_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.F2 || sender is not FrameworkElement { DataContext: AudioDevicePermissionItemViewModel row }) return;
        row.BeginRenameCommand.Execute(null);
        args.Handled = true;
    }
    private void AliasEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || sender is not TextBox editor) return;
        editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (editor.IsVisible && ViewModel.CanEdit && editor.DataContext is AudioDevicePermissionItemViewModel { IsRenaming: true })
            { editor.Focus(); editor.SelectAll(); }
        }));
    }
    private void AliasEditor_KeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: AudioDevicePermissionItemViewModel row } editor || args.Key is not (Key.Enter or Key.Escape)) return;
        if (args.Key == Key.Escape) row.CancelRenameCommand.Execute(null);
        else row.CommitRenameCommand.Execute(null);
        args.Handled = true;
        if (editor.Parent is Grid { Parent: Border nameSurface }) nameSurface.Focus();
    }
    private void AliasEditor_LostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: AudioDevicePermissionItemViewModel row }) row.CommitRenameCommand.Execute(null);
    }
    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    { if (await SettingsResetDialog.ConfirmAsync("Device.Management.Title", cancellationToken: ViewModel.CancellationToken)) ViewModel.Reset(); }
}
