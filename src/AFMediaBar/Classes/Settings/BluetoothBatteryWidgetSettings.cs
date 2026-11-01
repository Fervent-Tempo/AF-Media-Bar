// 保存用户勾选的设备和独立提醒主设备；不由默认音频设备或名称推断目标。
namespace AFMediaBar.Classes.Settings;

/// <summary>电量组件的自动显隐方式。</summary>
public enum BluetoothBatteryDisplayMode { WhenConnected, WhenLow }

/// <summary>电量数值或设备轮廓填充。</summary>
public enum BatteryVisualStyle { Numeric, Graphic }
/// <summary>多设备在栏上的排列方式。</summary>
public enum BatteryPresentationMode { Cycle, Parallel }
/// <summary>用户勾选的设备及离线时保留的名称。</summary>
public sealed record BatterySelectedDevice(string Id, string Name);

/// <summary>组件和低电量提醒共用所选设备及阈值，缺字段保持关闭。</summary>
public sealed record BluetoothBatteryWidgetSettings
{
    public const int MinimumThreshold = 5;
    public const int MaximumThreshold = 90;
    public const int DefaultThreshold = 20;
    public bool Enabled { get; init; }
    public string? DeviceId { get; init; }
    public string LastKnownName { get; init; } = string.Empty;
    public BluetoothBatteryDisplayMode DisplayMode { get; init; }
    public bool Notifications { get; init; }
    public BatteryVisualStyle VisualStyle { get; init; }
    public BatteryPresentationMode PresentationMode { get; init; }
    public IReadOnlyList<BatterySelectedDevice>? SelectedDevices { get; init; }
    public IReadOnlyList<BatterySelectedDevice> GetSelectedDevices() => SelectedDevices ??
        (DeviceId is { Length: > 0 } id ? [new(id, LastKnownName)] : []);
    public int LowThreshold { get; init; } = DefaultThreshold;
    public BluetoothBatteryWidgetSettings Normalize() => this with
    {
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? null : DeviceId.Trim(),
        LastKnownName = LastKnownName ?? string.Empty,
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : BluetoothBatteryDisplayMode.WhenConnected,
        VisualStyle = Enum.IsDefined(VisualStyle) ? VisualStyle : BatteryVisualStyle.Numeric,
        PresentationMode = Enum.IsDefined(PresentationMode) ? PresentationMode : BatteryPresentationMode.Cycle,
        SelectedDevices = SelectedDevices?.Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => new BatterySelectedDevice(d.Id.Trim(), d.Name ?? string.Empty)).DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
        LowThreshold = Math.Clamp(LowThreshold, MinimumThreshold, MaximumThreshold)
    };
}
