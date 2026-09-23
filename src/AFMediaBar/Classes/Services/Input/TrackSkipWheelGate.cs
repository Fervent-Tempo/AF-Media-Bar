using System.Diagnostics;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将连续滚轮事件合并为一次切歌，并阻止尚未完成的切歌命令重入。
/// Collapses a wheel burst into one track skip and prevents a new skip while the previous command is pending.
/// </summary>
internal sealed class TrackSkipWheelGate
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
    private readonly object _gate = new();
    private long _lastWheelTimestamp;
    private bool _hasWheelEvent;
    private bool _inFlight;

    /// <summary>
    /// 每个滚轮事件都更新静默期；只有上一条命令完成且滚轮已停顿时才允许切歌。
    /// Every wheel event extends the quiet period; a skip starts only after the prior command completes and scrolling pauses.
    /// </summary>
    internal bool TryBegin(long timestamp)
    {
        lock (_gate)
        {
            var afterQuietPeriod = !_hasWheelEvent ||
                Stopwatch.GetElapsedTime(_lastWheelTimestamp, timestamp) >= QuietPeriod;
            _lastWheelTimestamp = timestamp;
            _hasWheelEvent = true;

            if (_inFlight || !afterQuietPeriod)
                return false;

            _inFlight = true;
            return true;
        }
    }

    /// <summary>切歌命令结束后解除重入限制。/ Releases the in-flight guard when the skip command finishes.</summary>
    internal void Complete()
    {
        lock (_gate)
            _inFlight = false;
    }
}
