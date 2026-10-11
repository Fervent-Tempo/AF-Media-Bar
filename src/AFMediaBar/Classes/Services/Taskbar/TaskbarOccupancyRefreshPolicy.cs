// 合并图标变化并限制短时刷新频率；宿主持有实例和计时器，本策略不访问系统。
namespace AFMediaBar.Classes.Services;

/// <summary>50ms 合并、100ms 刷新，最后一次相关事件后一秒恢复普通采样。</summary>
public sealed class TaskbarOccupancyRefreshPolicy
{
    private double _deadline = double.NegativeInfinity;
    private double _next = double.PositiveInfinity;

    public void Changed(double seconds)
    {
        if (!double.IsFinite(seconds)) return;
        if (seconds > _deadline) _next = seconds + 0.05;
        _deadline = seconds + 1;
    }

    public bool IsActive(double seconds) => double.IsFinite(seconds) && seconds <= _deadline;

    public bool TryBegin(double seconds)
    {
        if (!IsActive(seconds) || seconds + 0.000001 < _next) return false;
        _next = seconds + 0.1;
        return true;
    }

    public void Reset()
    {
        _deadline = double.NegativeInfinity;
        _next = double.PositiveInfinity;
    }
}
