// 酷狗独立来源的身份与双通道仲裁，不读取进程、窗口或文件。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>合并酷狗内存快照及同来源 SMTC 控制，保持独立来源选择键稳定。</summary>
public sealed class KuGouSourcePolicy : IMediaSourcePolicy
{
    public const string SourceId = "kugou";
    public const string SelectionKey = "source:kugou";
    string IMediaSourcePolicy.SourceId => SourceId;
    string IMediaSourcePolicy.SelectionKey => SelectionKey;

    public bool Matches(string? sourceId) => sourceId is not null &&
        (sourceId.Contains("kugou", StringComparison.OrdinalIgnoreCase) || sourceId.Contains("kgmusic", StringComparison.OrdinalIgnoreCase));

    internal string NormalizeSourceId(string sourceId) => Matches(sourceId) ? SourceId : sourceId;

    public IReadOnlyList<MediaSourceCandidate> Combine(IReadOnlyList<MediaSourceCandidate> sessions, MediaSnapshot? memory)
    {
        var result = sessions.Where(session => !Matches(session.SourceId)).ToList();
        var smtc = sessions.Where(session => Matches(session.SourceId)).OrderByDescending(session => session.IsPlaying).FirstOrDefault();
        if (smtc is not null || memory is { IsConnected: true })
            result.Add(new(SelectionKey, SourceId, memory is { IsConnected: true } ? memory.IsPlaying : smtc!.IsPlaying, smtc?.SessionKey));
        return result;
    }

    public MediaSnapshot Merge(MediaSnapshot? memory, MediaSnapshot smtc)
    {
        var baseline = smtc.IsConnected && Matches(smtc.SourceId) ? smtc : MediaSnapshot.Disconnected;
        if (memory is not { IsConnected: true })
            return baseline.IsConnected ? baseline with { SourceId = SourceId } : MediaSnapshot.Disconnected;
        var sameTrack = baseline.IsConnected && !string.IsNullOrWhiteSpace(memory.Title) &&
            string.Equals(memory.Title.Trim(), baseline.Title.Trim(), StringComparison.OrdinalIgnoreCase) &&
            ArtistsDescribeTheSameTrack(memory.Artist, baseline.Artist);
        return memory with
        {
            SourceId = SourceId,
            Artwork = sameTrack ? baseline.Artwork : null,
            Lyrics = memory.Lyrics ?? (sameTrack ? baseline.Lyrics : null),
            CanPlayPause = baseline.CanPlayPause,
            CanSkipPrevious = baseline.CanSkipPrevious,
            CanSkipNext = baseline.CanSkipNext,
            CanSeek = baseline.CanSeek && memory.Duration > 0,
            CanChangeRepeat = baseline.CanChangeRepeat,
            RepeatMode = baseline.RepeatMode
        };
    }

    private static bool ArtistsDescribeTheSameTrack(string memoryArtist, string smtcArtist)
    {
        var artists = memoryArtist.Split([',', '、'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var reported = smtcArtist.Split([',', '、'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (artists.Length == 0 || reported.Length == 0) return false;
        // 实时标题使用逗号，SMTC 使用顿号；仍要求完整名单一致，或 SMTC 只报告第一位歌手。
        return artists.SequenceEqual(reported, StringComparer.OrdinalIgnoreCase) ||
            (reported.Length == 1 && string.Equals(artists[0], reported[0], StringComparison.OrdinalIgnoreCase));
    }
}
