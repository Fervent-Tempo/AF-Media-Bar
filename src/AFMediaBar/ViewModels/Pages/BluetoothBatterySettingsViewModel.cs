// 页面编辑显示设备与提醒目标；枚举经作用域取消，不接管应用级追踪生命周期。
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>所选蓝牙设备、显示策略和提醒的全局编辑器。</summary>
public sealed partial class BluetoothBatterySettingsViewModel : ObservableObject, IDisposable
{
    private readonly BluetoothBatteryService _battery;
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private IDisposable? _overviewLease;
    private bool _pageActive;
    private bool _disposed;
    private bool _refreshing;
    private int _generation;
    private string _status = string.Empty;
    public ObservableCollection<BluetoothBatteryDevice> Devices { get; } = [];
    public ObservableCollection<BatterySelectionItem> DisplayDevices { get; } = [];
    public BatteryVisualStyle VisualStyle { get => Current.VisualStyle; set => Update(Current with { VisualStyle = value }); }
    public int CycleIntervalSeconds { get => Current.CycleIntervalSeconds; set => Update(Current with { CycleIntervalSeconds = value }); }
    public bool IsCycle => PresentationMode == BatteryPresentationMode.Cycle;
    public string? FillColor => Current.FillColor;
    public bool UseAccentColor { get => Current.FillColor is null; set => Update(Current with { FillColor = value ? null : "#0078D4" }); }
    public string FillColorHex
    {
        get => Current.FillColor ?? "#0078D4";
        set { if (ColorHex.TryParse(value, out var color)) Update(Current with { FillColor = ColorHex.Format(color) }); }
    }
    [RelayCommand] private void SetFillColor(string? color) { if (color is not null) FillColorHex = color; }
    public BatteryPresentationMode PresentationMode
    {
        get => Current.PresentationMode;
        set
        {
            if (value == Current.PresentationMode || !_configuration.IsActive) return;
            _configuration.SetTaskbarExperience(AFMediaBar.Classes.Services.ComponentLayoutPolicy.PreserveImplicitLayouts(SettingsManager.Current));
            Update(Current with { PresentationMode = value });
        }
    }
    /// <summary>设备多选行；写入由所属设置作用域验证。</summary>
    public sealed class BatterySelectionItem(BluetoothBatterySettingsViewModel owner, BluetoothBatteryDevice device) : ObservableObject
    {
        public BluetoothBatteryDevice Device { get; private set; } = device;
        public string Id => Device.Id;
        public string Name => Device.DisplayName;
        public BluetoothBatterySnapshot Snapshot => owner._battery.Readings.FirstOrDefault(r => Device.Matches(r.DeviceId)) ??
            new(Id, null, null, false, false, null) { DeviceName = Name, Kind = Device.Kind, Transport = Device.Transport };
        public string State => Translations.Get("Battery.Transport." + Snapshot.Transport) + " · " +
            Translations.Get(Snapshot.IsConnected ? "Battery.Connected" : "Battery.Disconnected");
        public string Label => Snapshot.Percent is { } percent ? percent + "%" : "—";
        public string Details => Name + "\n" + Snapshot.Details;
        public bool IsSelected
        {
            get => owner.Current.GetSelectedDevices().Any(d => Device.Matches(d.Id));
            set
            {
                if (value == IsSelected) return;
                var selected = owner.Current.GetSelectedDevices().Where(d => !Device.Matches(d.Id)).ToList();
                if (value) selected.Add(new(Id, Name));
                owner.Update(owner.Current with { SelectedDevices = selected });
            }
        }
        internal void Refresh(BluetoothBatteryDevice value) { Device = value; OnPropertyChanged(string.Empty); }
    }
    private void RefreshDisplayDevices()
    {
        var selected = Current.GetSelectedDevices();
        var devices = selected.Select(d => Devices.FirstOrDefault(v => v.Matches(d.Id)) ?? new(d.Id, d.Name, null, string.Empty, []))
            .Concat(Devices.Where(d => !selected.Any(v => d.Matches(v.Id)))).ToArray();
        var retained = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in DisplayDevices.Where(d => !retained.Contains(d.Id)).ToArray()) DisplayDevices.Remove(item);
        for (var index = 0; index < devices.Length; index++)
        {
            var device = devices[index];
            var item = DisplayDevices.FirstOrDefault(d => d.Id.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
            if (item is null) DisplayDevices.Insert(index, new(this, device));
            else { item.Refresh(device); DisplayDevices.Move(DisplayDevices.IndexOf(item), index); }
        }
    }
    [RelayCommand] private void ReorderDevice(SettingsReorderRequest? request)
    {
        if (_disposed || !_configuration.IsActive || request is null ||
            !request.TryResolve(DisplayDevices.ToArray(), d => d.Id, out var index, out var target)) return;
        DisplayDevices.Move(index, target);
        Update(Current with { SelectedDevices = DisplayDevices.Where(d => d.IsSelected).Select(d => new BatterySelectedDevice(d.Id, d.Name)).ToArray() });
    }
    private BluetoothBatteryWidgetSettings Current => SettingsManager.Current.BluetoothBatteryWidget;
    public bool IsExperimentEnabled => SettingsManager.Current.Experiments.BluetoothBattery;
    public bool Enabled { get => Current.Enabled; set => Update(Current with { Enabled = value }); }
    public bool Notifications { get => Current.Notifications; set => Update(Current with { Notifications = value }); }
    public bool ConnectionNotifications { get => Current.ConnectionNotifications; set => Update(Current with { ConnectionNotifications = value }); }
    public bool CanRefresh => IsExperimentEnabled && !_battery.IsRefreshing;
    public string Status => _battery.IsRefreshing ? Translations.Get("Battery.Loading") :
        _battery.LastDiscoveryFailed ? Translations.Get("Battery.ReadFailed") : DisplayDevices.Count == 0 ? Translations.Get("Battery.Empty") : string.Empty;
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
    public void Activate()
    {
        if (_disposed || !_configuration.IsActive) return;
        _pageActive = true;
        if (IsExperimentEnabled) _overviewLease ??= _battery.AcquireOverviewSession();
        OnChanged(this, EventArgs.Empty);
    }
    public void Deactivate() { _pageActive = false; Interlocked.Exchange(ref _overviewLease, null)?.Dispose(); }
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
            RefreshDisplayDevices();
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) when (_disposed || _configuration.CancellationToken.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed && generation == _generation) { _status = Translations.Get("Battery.ReadFailed"); OnPropertyChanged(nameof(CurrentDetails)); } }
        finally { _refreshing = false; }
    }
    [RelayCommand] private void ClearSelection() => Update(Current with { DeviceId = null, LastKnownName = string.Empty });
    [RelayCommand] private void Refresh() => _battery.RequestRefresh();
    [RelayCommand] private Task RefreshDevicesListAsync() => RefreshDevicesAsync();
    private void OnChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (_pageActive)
        {
            if (IsExperimentEnabled) _overviewLease ??= _battery.AcquireOverviewSession();
            else Interlocked.Exchange(ref _overviewLease, null)?.Dispose();
            foreach (var reading in _battery.Readings)
                if (!Devices.Any(d => d.Matches(reading.DeviceId)))
                    Devices.Add(new(reading.DeviceId, reading.DeviceName, null, string.Empty, [])
                        { Kind = reading.Kind, Transport = reading.Transport, IsPresent = reading.IsConnected });
        }
        RefreshDisplayDevices(); OnPropertyChanged(string.Empty);
    }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.BluetoothBatteryWidget) or nameof(AppSettings.Experiments)) OnChanged(sender, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++;
        Deactivate();
        _battery.Changed -= OnChanged;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnChanged;
    }
}
