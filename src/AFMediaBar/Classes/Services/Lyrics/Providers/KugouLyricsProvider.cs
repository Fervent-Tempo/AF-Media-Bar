using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Kugou;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 酷狗歌词源：按曲名歌手搜索曲目拿到文件 hash，再按 hash 找到歌词候选并取解密后的 KRC 逐字歌词。
/// The Kugou source: searches a track by title and artist to obtain its file hash, looks up the lyric candidates by that
/// hash, and retrieves the decrypted KRC syllable lyrics.
///
/// 候选按曲目时长挑最接近的一个：同一个 hash 下可能同时存在原词、译词与不同版本。
/// The candidate closest in duration is chosen, because one hash can carry the original lyrics, a translation, and several
/// versions at the same time.
///
/// 已知限制：KRC 自带的译文行本轮不解析（提供译文的是网易云、QQ 音乐与汽水音乐）。
/// Known limitation: the translation lines a KRC file may carry are not parsed in this round (NetEase, QQ Music and
/// SodaMusic are the sources that supply translations); .
/// </summary>
public sealed class KugouLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.Kugou;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        var match = await LyricsSearch.MatchAsync(request, Searchers.Kugou, cancellationToken,
            retainBelowMinimum: LyricsSourcePolicy.IsCurrentPlaybackSource(request.PlaybackSourceId, SourceName));
        if (match?.Candidate is not KugouSearchResult kugou || string.IsNullOrWhiteSpace(kugou.Hash))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (candidate, confirmedNoLyrics) = await FindCandidateAsync(kugou.Hash, request, cancellationToken);
        if (confirmedNoLyrics) return LyricsResult.NoLyrics(SourceName, match.Score);
        if (candidate is null ||
            string.IsNullOrWhiteSpace(candidate.Id) ||
            string.IsNullOrWhiteSpace(candidate.AccessKey))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? krc;
        try
        {
            krc = await Lyricify.Lyrics.Decrypter.Krc.Helper.GetLyricsAsync(candidate.Id, candidate.AccessKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(krc))
        {
            return null;
        }

        var document = LyricsTextParser.Parse(
            krc,
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(SourceName, document) { MatchScore = match!.Score } : null;
    }

    private async Task<(SearchLyricsResponse.Candidate? Candidate, bool ConfirmedNoLyrics)> FindCandidateAsync(
        string hash,
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        SearchLyricsResponse? response;
        try
        {
            response = await _api.GetSearchLyrics(
                $"{request.Title} {request.Artist}",
                request.DurationSeconds is { } duration && duration > 0 ? (int)Math.Round(duration) : null,
                hash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = response?.Candidates;
        if (response is not { Status: 200, ErrorCode: 0 } || candidates is null) return (null, false);
        if (candidates is not { Count: > 0 })
        {
            return (null, true);
        }

        var usable = candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Id) && !string.IsNullOrWhiteSpace(candidate.AccessKey))
            .ToList();
        if (usable.Count == 0)
        {
            return (null, false);
        }

        var targetSeconds = request.DurationSeconds;
        if (targetSeconds is not { } target || !double.IsFinite(target) || target <= 0)
        {
            return (usable[0], false);
        }

        return (usable
            .OrderBy(candidate => Math.Abs(candidate.Duration - target))
            .First(), false);
    }
}
