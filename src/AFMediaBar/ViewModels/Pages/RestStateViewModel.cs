// 常驻页组合现有编辑器；详细参数仍由原页面拥有，订阅由 DI 作用域释放。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>协调常驻内容、排序和无媒体保留项。</summary>
public sealed class RestStateViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private bool _disposed;
    public DisplayModesViewModel Layers { get; }
    public TaskbarAppearanceViewModel Layout { get; }
    public BluetoothBatterySettingsViewModel Battery { get; }
    public SettingsPreviewViewModel Preview { get; }
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;
    public bool BatteryAvailable => _configuration.Current.Experiments.BluetoothBattery && !string.IsNullOrEmpty(_configuration.Current.BluetoothBatteryWidget.DeviceId);
    public SettingsPageKey BatteryDestination => _configuration.Current.Experiments.BluetoothBattery ? SettingsPageKey.Widgets : SettingsPageKey.Experimental;

    public RestStateViewModel(DisplayModesViewModel layers, TaskbarAppearanceViewModel layout,
        BluetoothBatterySettingsViewModel battery, SettingsPreviewViewModel preview, ISettingsConfiguration configuration)
    {
        Layers = layers;
        Layout = layout;
        Battery = battery;
        Preview = preview;
        _configuration = configuration;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        configuration.Activated += OnActivated;
    }

    public void Reset()
    {
        if (!_disposed && _configuration.IsActive) _configuration.Reset(SettingsResetScope.RestState);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (!_disposed && _configuration.IsActive) OnPropertyChanged(string.Empty);
    }
    private void OnActivated(object? sender, EventArgs args) => OnSettingsChanged(sender, new());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _configuration.Activated -= OnActivated;
    }
}
