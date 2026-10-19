// 页面只将拖动、重排与重置意图交给视图模型；设备枚举和持久化由应用服务处理。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>输入输出设备、循环快捷键与所选蓝牙设备的全局配置。</summary>
public partial class DeviceManagementPage : INavigableView<AudioDeviceSettingsViewModel>
{
    private Point? _dragStart;
    public AudioDeviceSettingsViewModel ViewModel { get; }
    public DeviceManagementPage(AudioDeviceSettingsViewModel viewModel)
    { ViewModel = viewModel; DataContext = this; InitializeComponent(); Unloaded += (_, _) => _dragStart = null; }
    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    { SettingsRevealAnimator.Play(sender as Panel); await ViewModel.RefreshAsync(); }
    private void DragHandle_Down(object sender, MouseButtonEventArgs args) => _dragStart = args.GetPosition(this);
    private void DragHandle_Move(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed) { _dragStart = null; return; }
        if (!ViewModel.CanEdit || !ViewModel.IsOrderingEnabled || _dragStart is not { } start ||
            sender is not FrameworkElement { DataContext: AudioDevicePermissionItemViewModel row } element || !row.IsAllowed || row.IsOffline) return;
        var point = args.GetPosition(this);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        args.Handled = true;
        DragDrop.DoDragDrop(element, new DataObject(typeof(AudioDevicePermissionItemViewModel), row), DragDropEffects.Move);
    }
    private void DeviceRow_DragOver(object sender, DragEventArgs args)
    {
        args.Handled = true; args.Effects = DragDropEffects.None;
        if (sender is FrameworkElement { DataContext: AudioDevicePermissionItemViewModel target } && target.IsAllowed && !target.IsOffline &&
            args.Data.GetData(typeof(AudioDevicePermissionItemViewModel)) is AudioDevicePermissionItemViewModel source && source.Direction == target.Direction)
            args.Effects = DragDropEffects.Move;
    }
    private void DeviceRow_Drop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        if (sender is FrameworkElement { DataContext: AudioDevicePermissionItemViewModel target } &&
            args.Data.GetData(typeof(AudioDevicePermissionItemViewModel)) is AudioDevicePermissionItemViewModel source) ViewModel.Reorder(source, target);
    }
    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    { if (await SettingsResetDialog.ConfirmAsync("Device.Management.Title", cancellationToken: ViewModel.CancellationToken)) ViewModel.Reset(); }
}
