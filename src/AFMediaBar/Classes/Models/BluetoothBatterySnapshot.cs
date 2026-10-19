// 物理设备电量快照与呈现字段，不持有 WinRT 或原生句柄。
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Models;

/// <summary>电量读数来源，不参与用户设置序列化。</summary>
public enum BluetoothBatterySource { WindowsProperty, BleGatt }

/// <summary>失败保留最后有效读数，但不能作为实时电量或通知依据。</summary>
public sealed record BluetoothBatterySnapshot(string DeviceId, int? Percent, BluetoothBatterySource? Source,
    bool IsConnected, bool ReadSucceeded, DateTimeOffset? UpdatedAt)
{
    public string DeviceName { get; init; } = string.Empty;
    public bool IsRealtime => IsConnected && ReadSucceeded && Percent is not null && UpdatedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(3);
    public string Label => Percent is { } percent ? $"{percent}%" + (IsRealtime ? string.Empty : " · " + Translations.Get("Battery.Stale")) : Translations.Get("Battery.Unknown");
    public string Details => UpdatedAt is null ? Translations.Get("Battery.NoReading") : Translations.Format("Battery.Details", Label,
        UpdatedAt?.ToLocalTime().ToString("g") ?? Translations.Get("Battery.Unknown"),
        Translations.Get(Source == BluetoothBatterySource.WindowsProperty ? "Battery.WindowsProperty" : Source == BluetoothBatterySource.BleGatt ? "Battery.BleGatt" : "Battery.Unknown"));
}
