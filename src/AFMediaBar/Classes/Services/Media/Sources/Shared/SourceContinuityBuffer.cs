// 内存来源短暂读取失败时冻结最后快照；进程不存在或超过期限立即清空。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Sources.Shared;

/// <summary>
/// 有界的短暂失败缓冲：只冻结最近一次已连接快照，并把它标记为 stale，供合并层回退 SMTC。
/// Bounded transient-failure buffer: freezes the last connected snapshot, marks it stale, and lets the merge layer fall back to SMTC.
/// </summary>
internal sealed class SourceContinuityBuffer(TimeSpan hold)
{
    private MediaSnapshot? _last;
    private DateTimeOffset? _missingSince;

    /// <summary>接收一次读取结果；进程不存在时不保留旧快照。/ Receives a read result; no old snapshot survives when the process is absent.</summary>
    internal MediaSnapshot? Update(MediaSnapshot? snapshot, bool processPresent, DateTimeOffset now)
    {
        if (!processPresent)
        {
            Clear();
            return null;
        }

        if (snapshot is { IsConnected: true })
        {
            _last = snapshot;
            _missingSince = null;
            return snapshot;
        }

        _missingSince ??= now;
        if (_last is null || now - _missingSince.Value >= hold)
        {
            _last = null;
            return null;
        }

        return _last with { IsPlaying = false, PlaybackRate = 0, IsStale = true };
    }

    internal void Clear()
    {
        _last = null;
        _missingSince = null;
    }
}
