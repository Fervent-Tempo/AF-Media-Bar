using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 网易云歌词源（来源专用）：播放器本身就是网易云时，用歌曲 id 精确取词。
/// The NetEase source provider for the NetEase player itself: it retrieves exactly by song id.
///
/// 通用取词链不会为网易云来源重复请求，因此这里通常是唯一命中网易云歌词的地方；播放其他来源时由搜索兜底提供器负责。
/// The generic chain never requests NetEase lyrics for a NetEase session, so this is usually the only place a NetEase lyric
/// is matched; other players are covered by the search fallback provider.
/// </summary>
public sealed class NetEaseLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.NetEase;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NetEaseSongId))
        {
            return null;
        }

        return await NetEaseLyricFetcher.FetchAndBuildAsync(
            _api,
            request.NetEaseSongId,
            SourceName,
            request,
            cancellationToken);
    }
}
