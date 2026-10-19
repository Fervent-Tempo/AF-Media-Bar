// 配对目录快照不持有 WinRT 设备对象；物理关联只使用容器或有效地址，不按名称合并。
namespace AFMediaBar.Classes.Models;

/// <summary>同一物理蓝牙设备的一条系统关联端点。</summary>
public sealed record BluetoothBatteryPeer(string Id, bool IsLowEnergy, bool IsConnected);

/// <summary>可由用户选择的配对设备，音频端点不是追踪身份。</summary>
public sealed record BluetoothBatteryDevice(string Id, string DisplayName, Guid? ContainerId, string Address,
    IReadOnlyList<BluetoothBatteryPeer> Peers)
{
    public bool IsConnected => Peers.Any(peer => peer.IsConnected);
    public bool Matches(string? id) => id is not null && (Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
        Peers.Any(peer => peer.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
}
