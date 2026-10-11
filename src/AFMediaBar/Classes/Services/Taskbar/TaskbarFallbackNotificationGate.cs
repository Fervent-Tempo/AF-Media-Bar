// 隐藏宿主拥有空间提示计次；本策略不持有系统通知、窗口或计时器。
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>多任务栏合并两级提醒，充分恢复后才允许下一轮。</summary>
public sealed class TaskbarFallbackNotificationGate
{
    private readonly Dictionary<string, Observation> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private bool _constrainedNotified;
    private bool _hiddenNotified;
    private double? _recoveryStarted;

    public TaskbarSpacePressure? Observe(string host, TaskbarSpacePressure pressure, bool ample,
        double seconds, long generation, long publicationId, long lastFailurePublicationId = 0)
    {
        if (!double.IsFinite(seconds)) return null;
        _hosts.TryGetValue(host, out var previous);
        if (previous is not null && (generation < previous.Generation ||
            generation == previous.Generation && publicationId < previous.Publication)) return null;
        var fresh = previous is null || generation != previous.Generation || publicationId > previous.Publication;
        if (!fresh) return null;
        var changed = previous is null || previous.Pressure != pressure || generation != previous.Generation ||
            seconds < previous.LastSeconds || seconds - previous.LastSeconds > 4 || lastFailurePublicationId > previous.LastFailure;
        if (previous is not null && (seconds < previous.LastSeconds || seconds - previous.LastSeconds > 4 ||
            lastFailurePublicationId > previous.LastFailure || generation != previous.Generation))
            _recoveryStarted = null;
        var current = new Observation(pressure, ample, generation, publicationId, lastFailurePublicationId,
            changed ? seconds : previous!.StartedSeconds, seconds, changed ? 1 : previous!.Count + 1);
        _hosts[host] = current;
        if (_hosts.Values.All(value => value.Pressure == TaskbarSpacePressure.Normal && value.Ample))
        {
            _recoveryStarted ??= seconds;
            if (_hosts.Values.All(value => value.LastSeconds - _recoveryStarted.Value >= 2))
            {
                _constrainedNotified = false;
                _hiddenNotified = false;
            }
        }
        else _recoveryStarted = null;
        if (current.Count < 2 || seconds - current.StartedSeconds < 0.2 - 0.000001) return null;
        if (pressure == TaskbarSpacePressure.Hidden && !_hiddenNotified)
        {
            _hiddenNotified = true;
            _constrainedNotified = true;
            return pressure;
        }
        if (pressure == TaskbarSpacePressure.Constrained && !_constrainedNotified)
        {
            _constrainedNotified = true;
            return pressure;
        }
        return null;
    }

    /// <summary>关闭的宿主不阻止其他屏幕恢复；重建本身不重置已发送等级。</summary>
    public void ForgetHost(string host)
    {
        _hosts.Remove(host);
        _recoveryStarted = null;
    }

    private sealed record Observation(TaskbarSpacePressure Pressure, bool Ample, long Generation,
        long Publication, long LastFailure, double StartedSeconds, double LastSeconds, int Count);
}
