// 提供器拥有协议读取资源；协调器在目标不再被追踪时释放其会话。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>蓝牙与 HID 电量的设备目录、读取及会话释放契约。</summary>
public interface IDeviceBatteryProvider
{
    string Id { get; }
    Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token);
    Task<BluetoothBatterySnapshot> ReadAsync(BluetoothBatteryDevice device, string selectedId, CancellationToken token);
    Task ReleaseAsync(string deviceId);
}
