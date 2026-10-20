// 合并一个资源所有者的重复故障；状态有界、使用单调时钟，不拥有线程或计时器。
using System.Diagnostics;

namespace AFMediaBar.Classes.Services;

internal sealed class RepeatedFailureReporter(TimeProvider? timeProvider = null)
{
    private const int Capacity = 32;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, Failure> _failures = [];
    private readonly object _gate = new();
    private sealed record Failure(string Fingerprint, long ReportedAt, long SeenAt, int Count);

    internal void Report(AppLogService? log, string category, string key, string message,
        Exception? exception = null, AppLogLevel level = AppLogLevel.Warning)
    {
        if (log is null || log.IsDisposed)
        {
            Debug.WriteLine($"[{category}] {message} operation={key}: {exception}");
            return;
        }
        var fingerprint = $"{exception?.GetType().FullName}:{exception?.HResult}";
        int count;
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            _failures.TryGetValue(key, out var previous);
            count = previous is not null && previous.Fingerprint == fingerprint ? (previous.Count < int.MaxValue ? previous.Count + 1 : int.MaxValue) : 1;
            if (previous is not null && previous.Fingerprint == fingerprint &&
                _clock.GetElapsedTime(previous.ReportedAt, now) < TimeSpan.FromSeconds(60))
            {
                _failures[key] = previous with { SeenAt = now, Count = count };
                return;
            }
            if (previous is null && _failures.Count >= Capacity)
                _failures.Remove(_failures.MinBy(pair => pair.Value.SeenAt).Key);
            _failures[key] = new(fingerprint, now, now, 0);
        }
        var text = $"{message} operation={key} occurrences={count}";
        if (level == AppLogLevel.Error) log.Error(category, text, exception);
        else if (exception is not null) log.Warn(category, text, exception);
        else log.Warn(category, text);
    }

    internal bool Recover(AppLogService? log, string category, string key, string message)
    {
        Failure? previous;
        lock (_gate)
        {
            if (!_failures.Remove(key, out previous)) return false;
        }
        log?.Info(category, $"{message} operation={key} suppressed={previous.Count}");
        return true;
    }
}
