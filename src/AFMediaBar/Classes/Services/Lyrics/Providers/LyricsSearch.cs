using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词请求与库之间的两件共用工作：把请求转成曲目元数据，以及按匹配评分做一次搜索。
/// The two shared jobs between a lyric request and the library: turning the request into track metadata and running one
/// search guarded by the match score.
/// </summary>
internal static class LyricsSearch
{
    /// <summary>
    /// 把歌词请求转成曲目元数据；时长优先取调用方传入的值，其次取请求自身的值。
    /// Converts a lyric request into track metadata; the duration comes from the caller when given, otherwise from the request.
    /// </summary>
    /// <param name="request">歌词请求 / Lyric request.</param>
    /// <param name="durationSeconds">调用方已知的时长（秒）/ Duration in seconds known by the caller.</param>
    public static TrackMetadata ToTrackMetadata(LyricsRequest? request, double? durationSeconds = null) => new()
    {
        Title = request?.Title,
        Artist = request?.Artist,
        // 库用 null 表示未知；空字符串会作为不匹配的专辑扣分。
        // The library treats null as unknown, but an empty string as an album mismatch.
        Album = string.IsNullOrWhiteSpace(request?.Album) ? null : request.Album,
        DurationMs = ToDurationMilliseconds(durationSeconds ?? request?.DurationSeconds)
    };

    /// <summary>
    /// 秒转毫秒；无效或非正数返回 null。
    /// Converts seconds into milliseconds, returning null for an invalid or non-positive value.
    /// </summary>
    public static int? ToDurationMilliseconds(double? durationSeconds) =>
        durationSeconds is { } seconds && double.IsFinite(seconds) && seconds > 0
            ? (int)Math.Round(seconds * 1000)
            : null;

    /// <summary>
    /// 用一个搜索源匹配曲目；命中评分低于 <paramref name="minimumMatch"/> 时库自身就会返回 null。
    /// Matches a track with one search source; the library itself returns null below <paramref name="minimumMatch"/>.
    ///
    /// 搜索失败、超时或解析异常都只表现为"这个源没有命中"，不冒泡也不打断兜底链。
    /// A failed, timed-out, or unparsable search only shows up as "this source did not match": nothing bubbles up and the
    /// fallback chain keeps running.
    /// </summary>
    /// <param name="track">曲目元数据 / Track metadata.</param>
    /// <param name="searcher">搜索源 / Search source.</param>
    /// <param name="minimumMatch">最低匹配要求 / Minimum required match.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>匹配到的曲目，未命中为 null / The matched track, or null without a match.</returns>
    public static async Task<ISearchResult?> MatchAsync(
        TrackMetadata track,
        Searchers searcher,
        CompareHelper.MatchType minimumMatch,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await SearchHelper.Search(track, searcher, minimumMatch);
            cancellationToken.ThrowIfCancellationRequested();
            AppLogService.Current?.Info("Lyrics",
                $"歌曲匹配 / track match: source={searcher} minimum={minimumMatch} " +
                $"result={result?.MatchType.ToString() ?? "none"}");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogService.Current?.Warn("Lyrics",
                $"歌曲搜索异常 / track search failed: source={searcher} error={ex.GetType().Name}");
            return null;
        }
    }
}
