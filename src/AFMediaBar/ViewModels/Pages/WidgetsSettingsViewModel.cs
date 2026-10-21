// 组合小组件参数编辑器；订阅和外部服务由 DI 作用域释放。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>小音符、频谱、性能与蓝牙电量的全局配置。</summary>
public sealed class WidgetsSettingsViewModel(
    ComponentsSettingsViewModel components,
    BluetoothBatterySettingsViewModel battery,
    QuickLaunchSettingsViewModel note,
    ISettingsConfiguration configuration)
{
    public QuickLaunchSettingsViewModel Note { get; } = note;
    public ComponentsSettingsViewModel Components { get; } = components;
    public BluetoothBatterySettingsViewModel Battery { get; } = battery;
    public CancellationToken ContextCancellationToken => configuration.CancellationToken;

    public async Task RefreshAsync()
    {
        await Components.RefreshCaptureDevicesAsync();
        if (configuration.IsActive && !ContextCancellationToken.IsCancellationRequested && Battery.IsExperimentEnabled)
            await Battery.RefreshDevicesAsync();
    }

    public void Reset()
    {
        if (configuration.IsActive) configuration.Reset(SettingsResetScope.Components);
    }
}
