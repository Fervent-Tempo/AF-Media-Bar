// 只保存一个用户明确选择的蓝牙设备；不由默认音频设备或名称推断目标。
namespace AFMediaBar.Classes.Settings;

/// <summary>电量组件的自动显隐方式。</summary>
public enum BluetoothBatteryDisplayMode { WhenConnected, WhenLow }

/// <summary>组件和低电量提醒共用所选设备及阈值，缺字段保持关闭。</summary>
public sealed record BluetoothBatteryWidgetSettings
{
    public bool Enabled { get; init; }
    public string? DeviceId { get; init; }
    public string LastKnownName { get; init; } = string.Empty;
    public BluetoothBatteryDisplayMode DisplayMode { get; init; }
    public bool Notifications { get; init; }
    public int LowThreshold { get; init; } = 20;
    public BluetoothBatteryWidgetSettings Normalize() => this with
    {
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? null : DeviceId.Trim(),
        LastKnownName = LastKnownName ?? string.Empty,
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : BluetoothBatteryDisplayMode.WhenConnected,
        LowThreshold = Math.Clamp(LowThreshold, 5, 90)
    };
}
