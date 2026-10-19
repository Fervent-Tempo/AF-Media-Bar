// 低电量提醒按物理设备保留回差状态，切换与重连只建立基线，不重新触发同一周期。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>不依赖硬件或通知窗口的提醒状态机。</summary>
public sealed class BluetoothBatteryAlertPolicy
{
    private readonly Dictionary<Guid, bool> _armed = [];
    private Guid? _target;
    private int _threshold;
    private bool _enabled;
    private bool _baseline = true;

    public void ResetBaseline() => _baseline = true;
    public bool Evaluate(Guid? target, BluetoothBatterySnapshot? battery, bool enabled, int threshold)
    {
        if (_threshold != threshold) { _threshold = threshold; _armed.Clear(); _baseline = true; }
        if (_target != target || _enabled != enabled) { _target = target; _enabled = enabled; _baseline = true; }
        if (!enabled || target is null || battery is null || battery.ContainerId != target || !battery.IsRealtime)
        { _baseline = true; return false; }
        var percent = battery.Percent!.Value;
        if (!_armed.TryGetValue(target.Value, out var armed))
        {
            // 仅保留有限的运行期状态；不将提醒历史写入用户配置。
            if (_armed.Count >= 128) _armed.Remove(_armed.Keys.First());
            _armed[target.Value] = percent > threshold;
            _baseline = false;
            return false;
        }
        if (_baseline)
        {
            _baseline = false;
            if (percent >= threshold + 5) _armed[target.Value] = true;
            else if (percent <= threshold) _armed[target.Value] = false;
            return false;
        }
        if (percent >= threshold + 5) _armed[target.Value] = true;
        else if (armed && percent <= threshold) { _armed[target.Value] = false; return true; }
        return false;
    }
}
