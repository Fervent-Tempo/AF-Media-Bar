// 视图模型只借用应用级概览会话并提交主设备选择，不拥有硬件服务。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>多设备概览的一行，保留对象身份以维持键盘焦点。</summary>
public sealed class DeviceBatteryOverviewItem(BluetoothBatterySnapshot snapshot) : ObservableObject
{
    public BluetoothBatterySnapshot Snapshot { get; private set; } = snapshot;
    public string Id => Snapshot.DeviceId;
    public string Name => Snapshot.DeviceName;
    public string Label => Snapshot.Percent is { } percent ? percent + "%" : "—";
    public string Details => Name + "\n" + Snapshot.Details + "\n" + Translations.Get(Snapshot.IsCharging switch
        { true => "Battery.Charging", false => "Battery.Discharging", null => "Battery.ChargingUnknown" });
    public bool IsPrimary => Id.Equals(SettingsManager.Current.BluetoothBatteryWidget.DeviceId, StringComparison.OrdinalIgnoreCase);
    public string? FillColor => SettingsManager.Current.BluetoothBatteryWidget.FillColor;
    public string PrimaryLabel => IsPrimary ? Translations.Get("Battery.Primary") : string.Empty;
    public string State => Translations.Get("Battery.Transport." + Snapshot.Transport) + " · " +
        Translations.Get(Snapshot.IsConnected ? "Battery.Connected" : "Battery.Disconnected");
    internal void Update(BluetoothBatterySnapshot value) { Snapshot = value; OnPropertyChanged(string.Empty); }
}

/// <summary>面板打开时请求概览，关闭后立即交还额外设备追踪。</summary>
public sealed partial class DeviceBatteryOverviewViewModel : ObservableObject, IDisposable
{
    private readonly BluetoothBatteryService _battery;
    private readonly LocalizationService _localization;
    private IDisposable? _lease;
    private bool _disposed;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    [RelayCommand] private void OpenSettings() { if (!_disposed) SettingsRequested?.Invoke(); }
    public ObservableCollection<DeviceBatteryOverviewItem> Devices { get; } = [];
    public bool IsEnabled => !_disposed && SettingsManager.Current.Experiments.BluetoothBattery;
    public bool CanRefresh => IsEnabled && !_battery.IsRefreshing;
    public string Status => !IsEnabled ? Translations.Get("Settings.Experiment.Required") :
        _battery.LastDiscoveryFailed ? Translations.Get("Battery.ReadFailed") :
        _battery.IsRefreshing ? Translations.Get("Battery.Loading") : Devices.Count == 0 ? Translations.Get("Battery.Empty") : string.Empty;
    public DeviceBatteryOverviewViewModel(BluetoothBatteryService battery, LocalizationService localization)
    {
        _battery = battery; _localization = localization;
        battery.Changed += OnChanged;
        localization.LanguageChanged += OnChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }
    public void RequestOpen() { if (IsEnabled) OpenRequested?.Invoke(); }
    public void Activate()
    {
        if (!IsEnabled || _lease is not null) return;
        _lease = _battery.AcquireOverviewSession();
        Update();
    }
    public void Deactivate() => Interlocked.Exchange(ref _lease, null)?.Dispose();
    [RelayCommand] private void Refresh() { if (CanRefresh) _battery.RequestRefresh(); }
    [RelayCommand] private void SelectDevice(DeviceBatteryOverviewItem? item)
    {
        if (!IsEnabled || _lease is null || item is null) return;
        SettingsManager.Current.BluetoothBatteryWidget = SettingsManager.Current.BluetoothBatteryWidget with { DeviceId = item.Id, LastKnownName = item.Name };
    }
    private void OnChanged(object? sender, EventArgs args) { if (!_disposed && _lease is not null) Update(); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is not (null or nameof(AppSettings.BluetoothBatteryWidget) or nameof(AppSettings.Experiments))) return;
        if (!IsEnabled) Deactivate();
        if (!_disposed) Update();
    }
    private void Update()
    {
        var readings = IsEnabled ? _battery.Readings : [];
        var ids = readings.Select(r => r.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Devices.Where(d => !ids.Contains(d.Id)).ToArray()) Devices.Remove(item);
        foreach (var reading in readings)
        {
            var item = Devices.FirstOrDefault(d => d.Id.Equals(reading.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (item is null) Devices.Add(new(reading)); else item.Update(reading);
        }
        OnPropertyChanged(string.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Deactivate(); OpenRequested = null; SettingsRequested = null;
        _battery.Changed -= OnChanged; _localization.LanguageChanged -= OnChanged;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
    }
}
