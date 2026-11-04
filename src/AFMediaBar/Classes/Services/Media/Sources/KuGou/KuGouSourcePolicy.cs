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
            !string.IsNullOrWhiteSpace(memory.Artist) && !string.IsNullOrWhiteSpace(baseline.Artist) &&
            (string.Equals(memory.Artist.Trim(), baseline.Artist.Trim(), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(memory.Artist.Split(',')[0].Trim(), baseline.Artist.Trim(), StringComparison.OrdinalIgnoreCase));
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
}
