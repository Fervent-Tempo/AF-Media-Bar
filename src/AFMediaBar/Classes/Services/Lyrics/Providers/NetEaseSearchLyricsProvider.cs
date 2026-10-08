using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Netease;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 网易云搜索备用源：用曲名与歌手搜索歌曲 id，再按 id 取歌词。
/// The NetEase search fallback finds a song id by title and artist, then retrieves lyrics by id.
///
/// 原始搜索候选由统一元数据策略评分；当前播放器为网易云时保留低分候选，否则应用固定门槛。
/// </summary>
public sealed class NetEaseSearchLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.NetEaseSearch;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        var match = await LyricsSearch.MatchAsync(request, Searchers.Netease, cancellationToken,
            retainBelowMinimum: LyricsSourcePolicy.IsCurrentPlaybackSource(request.PlaybackSourceId, SourceName));
        if (match?.Candidate is not NeteaseSearchResult netease || string.IsNullOrWhiteSpace(netease.Id))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await NetEaseLyricFetcher.FetchAndBuildAsync(
            _api,
            netease.Id,
            SourceName,
            request,
            cancellationToken);
        return result is null ? null : result with { MatchScore = match!.Score };
    }
}
