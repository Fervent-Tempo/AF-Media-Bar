// CPU 累计时间的差分与基线状态；不执行原生调用或使用 UI 资源。
namespace AFMediaBar.Classes.Services;

/// <summary>拒绝计数回退和无效差值，恢复后从新基线计算 CPU 占用。</summary>
public sealed class CpuUsagePolicy
{
    private (ulong Idle, ulong Kernel, ulong User)? _previous;

    public int? Sample(ulong idle, ulong kernel, ulong user)
    {
        var previous = _previous;
        _previous = (idle, kernel, user);
        if (previous is not { } value || idle < value.Idle || kernel < value.Kernel || user < value.User)
            return null;
        var idleDelta = idle - value.Idle;
        var totalDelta = (double)(kernel - value.Kernel) + (user - value.User);
        if (totalDelta <= 0 || idleDelta > totalDelta)
            return null;
        return (int)Math.Clamp(Math.Round((totalDelta - idleDelta) * 100d / totalDelta), 0, 100);
    }

    public void Reset() => _previous = null;
}
