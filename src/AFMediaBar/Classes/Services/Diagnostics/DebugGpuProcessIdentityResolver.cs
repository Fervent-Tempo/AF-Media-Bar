#if DEBUG
// 每个性能记录写入任务独占身份查询与权限失败冷却；不跨记录会话缓存进程身份。
using System.ComponentModel;
using System.Diagnostics;

namespace AFMediaBar.Classes.Services.Diagnostics;

internal sealed class DebugGpuProcessIdentityResolver(
    Func<int, (string Name, DateTimeOffset StartedAt)>? readIdentity = null, TimeProvider? timeProvider = null)
{
    private const int Capacity = 128;
    private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Func<int, (string Name, DateTimeOffset StartedAt)> _readIdentity = readIdentity ?? ReadIdentity;
    private readonly Dictionary<int, long> _denied = [];

    internal GpuProcessIdentityUsage[] DescribeTopGpu(GpuProcessEngineUsage[] entries, DateTimeOffset capturedAt)
    {
        var top = entries.Where(item => item.Percent > 0).OrderByDescending(item => item.Percent).Take(5).ToArray();
        var identities = new Dictionary<int, (string? Name, DateTimeOffset? StartedAt)>();
        foreach (var pid in top.Select(item => item.Pid).Distinct())
            identities[pid] = Resolve(pid, capturedAt);
        return top.Select(item => new GpuProcessIdentityUsage(item.Pid, identities[item.Pid].Name,
            identities[item.Pid].StartedAt, item.Adapter, item.Engine, item.Percent)).ToArray();
    }

    private (string? Name, DateTimeOffset? StartedAt) Resolve(int pid, DateTimeOffset capturedAt)
    {
        var now = _clock.GetTimestamp();
        if (_denied.TryGetValue(pid, out var failedAt) && _clock.GetElapsedTime(failedAt, now) < _retryInterval)
            return (null, null);
        _denied.Remove(pid);
        try
        {
            var identity = _readIdentity(pid);
            // 成功结果也只用于本次采样；PID 复用或采样积压不能继承另一进程的身份。
            return GpuProcessEngineUsage.CanAssociateProcess(identity.StartedAt, capturedAt)
                ? (identity.Name, identity.StartedAt) : (null, null);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            if (_denied.Count >= Capacity) _denied.Remove(_denied.MinBy(item => item.Value).Key);
            _denied[pid] = now;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception) { }
        return (null, null);
    }

    private static (string Name, DateTimeOffset StartedAt) ReadIdentity(int pid)
    {
        using var process = Process.GetProcessById(pid);
        var startedAt = new DateTimeOffset(process.StartTime);
        return (process.ProcessName, startedAt);
    }
}
#endif
