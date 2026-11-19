// LRCLIB 只负责候选和正文获取；使用统一评分，不把试听时长作为服务器检索条件。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.LRCLIB;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>从 LRCLIB 按最高匹配分采纳歌词或确认无歌词的状态。</summary>
public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();
    public string SourceName => LyricsSourceCatalog.Lrclib;

    public async Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return null;
        // 先按标题原写法搜完所有歌手组合，全部没命中再换成归一化写法重试：
        // 只有半角中点这类写法差异才会产生第二个标题，正常情况仍然只搜一轮。
        // Try every artist combination with the original title first, then repeat with the normalized one: only a spelling
        // difference such as a halfwidth middle dot yields a second title, so the usual case stays a single round.
        foreach (var title in LyricsSearchQueryPolicy.BuildTitleVariants(request))
        {
            foreach (var artist in LyricsSearchQueryPolicy.BuildArtists(request))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = await _api.Search(title, artist).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var result = Resolve(request, candidates ?? [], cancellationToken);
                if (result is not null) return result;
            }
        }
        return null;
    }

    internal static LyricsResult? Resolve(LyricsRequest request, IEnumerable<SearchResultItem> candidates, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 先按歌曲身份排序，不能先排除纯音乐或空歌词，从而让低分候选胜出。
        var ranked = candidates
            .Select(candidate => (Candidate: candidate, Score: LyricsMetadataScore.Calculate(request,
                candidate.TrackName, candidate.ArtistName, candidate.AlbumName, candidate.Duration)))
            .Where(item => item.Score >= LyricsMetadataScore.MinimumScore).OrderByDescending(item => item.Score);
        var item = ranked.FirstOrDefault();
        if (item.Candidate is null) return null;
        if (item.Candidate.Instrumental ||
            (string.IsNullOrWhiteSpace(item.Candidate.SyncedLyrics) && string.IsNullOrWhiteSpace(item.Candidate.PlainLyrics)))
            return LyricsResult.NoLyrics(LyricsSourceCatalog.Lrclib, item.Score);
        cancellationToken.ThrowIfCancellationRequested();
        var main = string.IsNullOrWhiteSpace(item.Candidate.SyncedLyrics) ? item.Candidate.PlainLyrics : item.Candidate.SyncedLyrics;
        var document = LyricsTextParser.Parse(main, request: request,
            durationSeconds: request.DurationSeconds, filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(LyricsSourceCatalog.Lrclib, document) { MatchScore = item.Score } : null;
    }
}
