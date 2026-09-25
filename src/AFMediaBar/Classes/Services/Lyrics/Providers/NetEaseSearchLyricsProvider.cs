using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Netease;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 网易云搜索兜底源：播放器不是网易云时，用曲名与歌手搜索歌曲 id，再按 id 取同一份歌词。
/// The NetEase search fallback: when the player is not NetEase, it searches for a song id by title and artist and then
/// retrieves the same lyrics by id.
///
/// 这是通用取词链上唯一能拿到网易云歌词（含中文译文）的入口：来源专用提供器只在网易云客户端播放时生效。
/// This is the only entry on the generic retrieval chain that can reach NetEase lyrics including Chinese translations: the
/// source-specific provider only applies while the NetEase client itself is playing.
///
/// 搜索必须过评分：低于用户选择的匹配严格度（默认 High）一律不采用，避免同名现场版、翻唱或纯伴奏串词。
/// The search has to pass the score: anything below the strictness the user chose (High by default) is rejected, which keeps a
/// same-named live version, a cover, or an instrumental from supplying the wrong lyrics.
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

        var track = LyricsSearch.ToTrackMetadata(request);
        var minimumMatch = LyricsMatchPolicy.ToMinimumMatch(request.MatchStrictness);
        var match = await LyricsSearch.MatchAsync(track, Searchers.Netease, minimumMatch, cancellationToken);
        if (match is not NeteaseSearchResult netease || string.IsNullOrWhiteSpace(netease.Id))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await NetEaseLyricFetcher.FetchAndBuildAsync(
            _api,
            netease.Id,
            SourceName,
            request,
            cancellationToken);
    }
}
