// 单设备不可变快照，服务在 UI 线程发布；呈现订阅不触发额外平台查询。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>设置、组件与提醒共用的所选设备快照。</summary>
public sealed class BluetoothBatteryCache
{
    public BluetoothBatterySnapshot? Snapshot { get; private set; }
    public event EventHandler? Changed;
    public void Publish(BluetoothBatterySnapshot? snapshot)
    {
        if (Snapshot == snapshot) return;
        Snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear() => Publish(null);
}
