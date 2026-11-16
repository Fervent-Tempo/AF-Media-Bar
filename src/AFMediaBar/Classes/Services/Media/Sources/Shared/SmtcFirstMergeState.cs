// 共享的 SMTC 优先合并：内存只补时间轴与歌词，换歌时短暂等待 SMTC 追上。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Sources.Shared;

/// <summary>
/// 酷狗、网易云共用的快照仲裁：SMTC 提供元数据、封面和控制，内存只补位置、时长与歌词。
/// Shared snapshot arbitration for KuGou and NetEase: SMTC supplies metadata, artwork, and controls;
/// memory only supplements position, duration, and lyrics.
/// </summary>
internal sealed class SmtcFirstMergeState
{
    internal static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(1.5);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _hold;
    private readonly Func<MediaSnapshot, bool> _sameSource;
    private readonly Func<MediaSnapshot, MediaSnapshot, bool> _sameTrack;
    private string? _mismatchKey;
    private DateTimeOffset _holdUntil;

    internal SmtcFirstMergeState(
        Func<MediaSnapshot, bool> sameSource,
        Func<MediaSnapshot, MediaSnapshot, bool> sameTrack,
        TimeProvider? timeProvider = null,
        TimeSpan? hold = null)
    {
        _sameSource = sameSource;
        _sameTrack = sameTrack;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hold = hold ?? DefaultHold;
    }

    /// <summary>
    /// 合并一个来源的内存与 SMTC 快照。无 SMTC 时退回内存；内存不可用或 stale 时退回 SMTC。
    /// Merges one source's memory and SMTC snapshots. Falls back to memory without SMTC, and to SMTC when memory is unavailable or stale.
    /// </summary>
    internal MediaSnapshot Merge(MediaSnapshot? memory, MediaSnapshot smtc, string sourceId)
    {
        var now = _timeProvider.GetUtcNow();
        var baseline = smtc.IsConnected && _sameSource(smtc) ? smtc : MediaSnapshot.Disconnected;

        if (memory is not { IsConnected: true })
        {
            _mismatchKey = null;
            return baseline.IsConnected ? baseline with { SourceId = sourceId } : MediaSnapshot.Disconnected;
        }

        if (!baseline.IsConnected)
        {
            _mismatchKey = null;
            return memory with { SourceId = sourceId };
        }

        if (memory.IsStale)
        {
            _mismatchKey = null;
            return baseline with
            {
                SourceId = sourceId,
                SourceName = string.IsNullOrWhiteSpace(baseline.SourceName) ? memory.SourceName : baseline.SourceName
            };
        }

        // SMTC 没有标题和歌手时没有可比较的身份，直接让内存补字段，不进入换歌缓冲。
        var smtcIdentifiable = !string.IsNullOrWhiteSpace(baseline.Title) ||
                               !string.IsNullOrWhiteSpace(baseline.Artist);
        var sameTrack = !smtcIdentifiable || _sameTrack(memory, baseline);
        var overlay = sameTrack;
        if (!sameTrack)
        {
            // 内存与 SMTC 不是同一首时给 SMTC 一个追上的窗口；窗口结束后仍按内存补时间轴。
            // While the two channels disagree, give SMTC a short window to catch up; afterwards still use memory for the timeline.
            var mismatch = $"{TrackKey(memory)}\u001e{TrackKey(baseline)}";
            if (!string.Equals(mismatch, _mismatchKey, StringComparison.Ordinal))
            {
                _mismatchKey = mismatch;
                _holdUntil = now + _hold;
            }

            overlay = now >= _holdUntil;
        }
        else
        {
            _mismatchKey = null;
        }

        if (!overlay)
        {
            return baseline with
            {
                SourceId = sourceId,
                SourceName = string.IsNullOrWhiteSpace(baseline.SourceName) ? memory.SourceName : baseline.SourceName
            };
        }

        var duration = memory.Duration > 0 ? memory.Duration : baseline.Duration;
        var position = duration > 0
            ? Math.Clamp(memory.Position, 0, duration)
            : Math.Max(0, memory.Position);
        var useMemoryLyrics = memory.Lyrics is not null;
        return baseline with
        {
            SourceId = sourceId,
            Title = string.IsNullOrWhiteSpace(baseline.Title) ? memory.Title : baseline.Title,
            Artist = string.IsNullOrWhiteSpace(baseline.Artist) ? memory.Artist : baseline.Artist,
            SourceName = string.IsNullOrWhiteSpace(baseline.SourceName) ? memory.SourceName : baseline.SourceName,
            Artwork = baseline.Artwork,
            Lyrics = useMemoryLyrics ? memory.Lyrics : baseline.Lyrics,
            Position = position,
            Duration = duration,
            CanSeek = baseline.CanSeek && duration > 0,
            TimelineUpdatedAt = memory.TimelineUpdatedAt,
            IsSearchingLyrics = useMemoryLyrics ? memory.IsSearchingLyrics : baseline.IsSearchingLyrics,
            IsStale = false
        };
    }

    private static string TrackKey(MediaSnapshot snapshot) =>
        $"{snapshot.Title.Trim().ToUpperInvariant()}\u001f{snapshot.Artist.Trim().ToUpperInvariant()}";
}
