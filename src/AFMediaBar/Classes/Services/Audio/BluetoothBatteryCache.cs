// 电量服务写入不可变字典，设备目录只读；事件不触发新的原生查询。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>物理设备电量共享缓存，打断读取服务与音频目录之间的依赖环。</summary>
public sealed class BluetoothBatteryCache
{
    private IReadOnlyDictionary<Guid, BluetoothBatterySnapshot> _snapshots = new Dictionary<Guid, BluetoothBatterySnapshot>();
    public event EventHandler? Changed;
    public BluetoothBatterySnapshot? Get(Guid? containerId) => containerId is { } id ? Volatile.Read(ref _snapshots).GetValueOrDefault(id) : null;
    public void Replace(IReadOnlyDictionary<Guid, BluetoothBatterySnapshot> snapshots)
    {
        var old = Volatile.Read(ref _snapshots);
        if (old.Count == snapshots.Count && snapshots.All(pair => old.TryGetValue(pair.Key, out var value) && value == pair.Value)) return;
        Volatile.Write(ref _snapshots, snapshots);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear() => Replace(new Dictionary<Guid, BluetoothBatterySnapshot>());
}
