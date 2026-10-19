// 页面仅编辑一个追踪目标；枚举经作用域取消，关闭页面不接管应用级追踪生命周期。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>所选蓝牙设备、显示策略和提醒的全局编辑器。</summary>
public sealed partial class BluetoothBatterySettingsViewModel : ObservableObject, IDisposable
{
    private readonly BluetoothBatteryService _battery;
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private bool _disposed;
    private bool _refreshing;
    private int _generation;
    private string _status = string.Empty;
    public ObservableCollection<BluetoothBatteryDevice> Devices { get; } = [];
    private BluetoothBatteryWidgetSettings Current => SettingsManager.Current.BluetoothBatteryWidget;
    public bool IsExperimentEnabled => SettingsManager.Current.Experiments.BluetoothBattery;
    public bool Enabled { get => Current.Enabled; set => Update(Current with { Enabled = value }); }
    public bool Notifications { get => Current.Notifications; set => Update(Current with { Notifications = value }); }
    public double MinimumLowThreshold => BluetoothBatteryWidgetSettings.MinimumThreshold;
    public double MaximumLowThreshold => BluetoothBatteryWidgetSettings.MaximumThreshold;
    public int LowThreshold { get => Current.LowThreshold; set => Update(Current with { LowThreshold = value }); }
    public BluetoothBatteryDisplayMode DisplayMode { get => Current.DisplayMode; set => Update(Current with { DisplayMode = value }); }
    public BluetoothBatteryDevice? SelectedDevice
    {
        get => Devices.FirstOrDefault(device => device.Matches(Current.DeviceId));
        set { if (!_refreshing) Update(Current with { DeviceId = value?.Id, LastKnownName = value?.DisplayName ?? string.Empty }); }
    }
    public string CurrentDetails => _battery.Snapshot is { } snapshot ? snapshot.DeviceName + " · " +
        Translations.Get(snapshot.IsConnected ? "Battery.Connected" : "Battery.Disconnected") + "\n" + snapshot.Details :
        !string.IsNullOrEmpty(_status) ? _status : Translations.Get("Battery.SelectHint");

    public BluetoothBatterySettingsViewModel(BluetoothBatteryService battery, ISettingsConfiguration configuration, LocalizationService localization)
    {
        _battery = battery; _configuration = configuration; _localization = localization;
        battery.Changed += OnChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        localization.LanguageChanged += OnChanged;
    }
    private void Update(BluetoothBatteryWidgetSettings value)
    {
        if (!_disposed && _configuration.IsActive) SettingsManager.Current.BluetoothBatteryWidget = value.Normalize();
    }
    public async Task RefreshDevicesAsync()
    {
        if (_disposed || !_configuration.IsActive) return;
        var generation = ++_generation;
        try
        {
            var devices = await _battery.GetDevicesAsync(_configuration.CancellationToken);
            if (_disposed || !_configuration.IsActive || generation != _generation) return;
            _refreshing = true;
            Devices.Clear();
            foreach (var device in devices) Devices.Add(device);
            if (Current.DeviceId is { } id && !Devices.Any(device => device.Matches(id)))
                Devices.Add(new(id, Current.LastKnownName, null, string.Empty, []));
            _status = string.Empty;
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) when (_disposed || _configuration.CancellationToken.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed && generation == _generation) { _status = Translations.Get("Battery.ReadFailed"); OnPropertyChanged(nameof(CurrentDetails)); } }
        finally { _refreshing = false; }
    }
    [RelayCommand] private void ClearSelection() => Update(Current with { DeviceId = null, LastKnownName = string.Empty });
    [RelayCommand] private void Refresh() => _battery.RequestRefresh();
    [RelayCommand] private Task RefreshDevicesListAsync() => RefreshDevicesAsync();
    private void OnChanged(object? sender, EventArgs args) { if (!_disposed) OnPropertyChanged(string.Empty); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.BluetoothBatteryWidget) or nameof(AppSettings.Experiments)) OnChanged(sender, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++;
        _battery.Changed -= OnChanged;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnChanged;
    }
}
