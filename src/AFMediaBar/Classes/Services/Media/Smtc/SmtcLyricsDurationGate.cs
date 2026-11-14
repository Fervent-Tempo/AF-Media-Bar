// 新曲目首读的 SMTC 时长可能仍是上一首或 0；先等读数稳定，再发唯一一次取词请求。
namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 判断新曲目的歌词请求是否应暂缓，避免把上一首的时长当成当前曲目的时长。
/// Decides whether a new track's lyric request should wait so the previous track's duration is not scored as the current one.
/// </summary>
internal sealed class SmtcLyricsDurationGate
{
    /// <summary>最长等待；超时后按当前时长取词，不能因为上游一直不给时长而永远不取词。</summary>
    internal static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(1);
    private const double DurationToleranceSeconds = 0.05;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maximumWait;
    private string? _trackKey;
    private double? _lastDuration;
    private int _sameDurationObservations;
    private long _startedAt;
    private bool _released;

    internal SmtcLyricsDurationGate(TimeProvider timeProvider, TimeSpan? maximumWait = null)
    {
        _timeProvider = timeProvider;
        _maximumWait = maximumWait is { } value && value > TimeSpan.Zero ? value : MaximumWait;
    }

    /// <summary>首读总是暂缓；同一曲目的后续读数变化或稳定后才放行。</summary>
    internal bool ShouldDefer(string trackKey, double? durationSeconds)
    {
        var now = _timeProvider.GetTimestamp();
        if (!string.Equals(_trackKey, trackKey, StringComparison.Ordinal))
        {
            _trackKey = trackKey;
            _startedAt = now;
            _lastDuration = durationSeconds;
            _sameDurationObservations = durationSeconds is > 0 ? 1 : 0;
            _released = false;
            return true;
        }

        if (_released) return false;
        if (_timeProvider.GetElapsedTime(_startedAt, now) >= _maximumWait)
        {
            _released = true;
            return false;
        }

        if (durationSeconds is > 0)
        {
            // 0 → 正数是明确的“时长终于可用”，不需要再等第二次相同读数。
            if (_lastDuration is not > 0)
            {
                _lastDuration = durationSeconds;
                _released = true;
                return false;
            }

            if (Math.Abs(durationSeconds.Value - _lastDuration.Value) <= DurationToleranceSeconds)
            {
                _sameDurationObservations++;
                if (_sameDurationObservations >= 2)
                {
                    _released = true;
                    return false;
                }
            }
            else
            {
                // 首读可能是上一首；换成新值后再等一次确认，避免另一个瞬时值继续误导评分。
                _lastDuration = durationSeconds;
                _sameDurationObservations = 1;
            }
        }
        else
        {
            // 时长为 0 时不能拿来评分；继续等下一次读数，超时后仍按无时长处理。
            _lastDuration = null;
            _sameDurationObservations = 0;
        }

        return true;
    }

    /// <summary>换歌、清缓存或释放后丢弃上一首的稳定化状态。/ Drops the previous track's stabilization state.</summary>
    internal void Reset()
    {
        _trackKey = null;
        _lastDuration = null;
        _sameDurationObservations = 0;
        _released = false;
    }
}
