using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.SodaMusic;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 汽水音乐歌词源：按曲名与歌手搜索曲目，再从曲目详情里取歌词正文与中文译文。
/// The Soda Music source: searches a track by title and artist and reads the lyric body plus the Chinese translation from
/// the track detail.
/// </summary>
public sealed class SodaMusicLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.SodaMusic;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        var track = LyricsSearch.ToTrackMetadata(request);
        var minimumMatch = LyricsMatchPolicy.ToMinimumMatch(request.MatchStrictness);
        var match = await LyricsSearch.MatchAsync(track, Searchers.SodaMusic, minimumMatch, cancellationToken);
        if (match is not SodaMusicSearchResult soda || string.IsNullOrWhiteSpace(soda.Id))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        TrackDetailResult? detail;
        try
        {
            detail = await _api.GetDetail(soda.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        var main = detail?.Lyric?.Content;
        if (string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        var document = LyricsTextParser.Parse(
            main,
            ResolveTranslation(detail!.Lyric),
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(SourceName, document) : null;
    }

    /// <summary>
    /// 取中文译文：优先逐语言翻译表里的中文，其次曲目详情自带的译文对象。
    /// Resolves the Chinese translation: the per-language translation table first, then the translation object carried by the
    /// track detail.
    /// </summary>
    private static string? ResolveTranslation(LyricInfo? lyric)
    {
        if (lyric?.LangTranslations is { Count: > 0 } translations &&
            translations.TryGetValue("zh", out var chinese) &&
            !string.IsNullOrWhiteSpace(chinese?.Content))
        {
            return chinese!.Content;
        }

        var fallback = lyric?.Translations?.ChineseTranslation;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
