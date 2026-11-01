// 配对目录快照不持有 WinRT 设备对象；物理关联只使用容器或有效地址，不按名称合并。
namespace AFMediaBar.Classes.Models;

/// <summary>实际承载电量查询的连接方式。</summary>
public enum BatteryTransport { Bluetooth, Usb, Receiver }

/// <summary>来自系统分类或已确认协议的设备类型；接收器本身不代表子设备类型。</summary>
public enum BatteryDeviceKind { Unknown, Mouse, Keyboard, Headset, Controller }

/// <summary>同一物理蓝牙设备的一条系统关联端点。</summary>
public sealed record BluetoothBatteryPeer(string Id, bool IsLowEnergy, bool IsConnected);

/// <summary>可由用户选择的配对设备，音频端点不是追踪身份。</summary>
public sealed record BluetoothBatteryDevice(string Id, string DisplayName, Guid? ContainerId, string Address,
    IReadOnlyList<BluetoothBatteryPeer> Peers)
{
    public string ProviderId { get; init; } = "bluetooth";
    public BatteryDeviceKind Kind { get; init; }
    public BatteryTransport Transport { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public bool IsPresent { get; init; }
    public bool IsPassive { get; init; }
    public bool IsConnected => IsPresent || Peers.Any(peer => peer.IsConnected);
    public bool Matches(string? id) => id is not null && (Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
        Peers.Any(peer => peer.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
}
