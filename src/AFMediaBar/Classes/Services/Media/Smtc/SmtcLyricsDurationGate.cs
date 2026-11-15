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
    private static readonly TimeSpan StableDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PreviousTrackDurationConfirmation = TimeSpan.FromMilliseconds(500);
    private const double DurationToleranceSeconds = 0.05;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maximumWait;
    private string? _trackKey;
    private double? _lastDuration;
    private double? _lastPositiveDuration;
    private double? _previousTrackDuration;
    private long _startedAt;
    private long _stableSince;
    private bool _matchesPreviousTrack;
    private bool _released;

    internal SmtcLyricsDurationGate(TimeProvider timeProvider, TimeSpan? maximumWait = null)
    {
        _timeProvider = timeProvider;
        _maximumWait = maximumWait is { } value && value > TimeSpan.Zero ? value : MaximumWait;
    }

    /// <summary>当前跟踪的曲目键，供调用方判断是否需要重排到期刷新。</summary>
    internal string? CurrentTrackKey => _trackKey;

    /// <summary>首读总是暂缓；同一曲目的时长保持足够久或超时后才放行。</summary>
    internal bool ShouldDefer(string trackKey, double? durationSeconds)
    {
        var now = _timeProvider.GetTimestamp();
        if (!string.Equals(_trackKey, trackKey, StringComparison.Ordinal))
        {
            _previousTrackDuration = _released ? _lastPositiveDuration : null;
            _trackKey = trackKey;
            _startedAt = now;
            _lastDuration = durationSeconds;
            _lastPositiveDuration = durationSeconds is > 0 ? durationSeconds : null;
            _stableSince = now;
            _matchesPreviousTrack = MatchesPreviousTrack(durationSeconds);
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
            var previous = _lastDuration;
            var wasUnavailable = previous is not > 0;
            if (wasUnavailable || Math.Abs(durationSeconds.Value - (previous ?? 0)) > DurationToleranceSeconds)
            {
                _lastDuration = durationSeconds;
                _lastPositiveDuration = durationSeconds;
                _stableSince = now;
                _matchesPreviousTrack = MatchesPreviousTrack(durationSeconds);
                // 0 -> 非旧值正数是明确的可用时长；但如果恰好等于上一首，仍要等确认。
                if (wasUnavailable && !_matchesPreviousTrack)
                {
                    _released = true;
                    return false;
                }
            }

            var stableFor = _timeProvider.GetElapsedTime(_stableSince, now);
            var required = _matchesPreviousTrack ? PreviousTrackDurationConfirmation : StableDuration;
            if (stableFor >= required)
            {
                _released = true;
                return false;
            }
        }
        else
        {
            // 时长为 0 时不能拿来评分；继续等下一次读数，超时后仍按无时长处理。
            _lastDuration = null;
            _stableSince = now;
            _matchesPreviousTrack = false;
        }

        return true;
    }

    /// <summary>换歌、清缓存或释放后丢弃上一首的稳定化状态。/ Drops the previous track's stabilization state.</summary>
    internal void Reset()
    {
        _trackKey = null;
        _lastDuration = null;
        _lastPositiveDuration = null;
        _previousTrackDuration = null;
        _stableSince = 0;
        _matchesPreviousTrack = false;
        _released = false;
    }

    private bool MatchesPreviousTrack(double? durationSeconds) =>
        _previousTrackDuration is > 0 && durationSeconds is > 0 &&
        Math.Abs(durationSeconds.Value - _previousTrackDuration.Value) <= DurationToleranceSeconds;
}
