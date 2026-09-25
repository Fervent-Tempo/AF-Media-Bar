using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 网易云取词的共用实现：并发请求两个端点，再交给 <see cref="NetEaseLyricMerge"/> 合并成歌词文档。
/// The shared NetEase retrieval implementation: it requests both endpoints concurrently and hands them to
/// <see cref="NetEaseLyricMerge"/> for the final document.
///
/// 为什么两个端点都要：旧端点给出行级正文与行级译文/音译，新端点给出逐字正文与逐字译文/音译，两边的缺失面并不重合——
/// 只取新端点会丢掉中文译文（它的行级字段装的是署名 JSON），只取旧端点则拿不到逐字。两次请求并发发出，延迟与单次相当，
/// 结果按曲目缓存，因此每首歌最多支付一次。
/// Both endpoints are needed because their gaps do not overlap: the legacy one carries the line-level body plus line-level
/// translation and romanization, while the new one carries the syllable body plus syllable translation and romanization.
/// Taking only the new one loses the Chinese translation (its line-level field holds credit JSON), and taking only the legacy
/// one loses the syllables. The two requests run concurrently, so the latency stays close to a single call, and results are
/// cached per track, so each song pays for it once.
///
/// 网易云来源有两个入口（来源专用提供器按 id 精确取词、通用链上的搜索兜底提供器先搜 id），两者必须取同一份歌词，
/// 因此取词与文档构建都留在这里，两个提供器只负责自己拿到 id 的方式。
/// NetEase has two entry points (the source-specific provider retrieving by id and the search fallback on the generic chain),
/// and both have to retrieve the same lyrics, so retrieval and document building live here while each provider only owns the
/// way it obtains an id.
/// </summary>
internal static class NetEaseLyricFetcher
{
    /// <summary>
    /// 按歌曲 id 取词并构建文档：两个端点并发，任一侧失败只损失那一侧的内容。
    /// Retrieves by song id and builds the document: both endpoints run concurrently, and a failure on either side only costs
    /// that side's content.
    /// </summary>
    /// <param name="api">网易云接口实例 / NetEase API instance.</param>
    /// <param name="songId">歌曲 id / Song id.</param>
    /// <param name="sourceName">来源标识 / Source identifier.</param>
    /// <param name="request">歌词请求 / Lyric request.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>歌词结果；没有可用行时为 null / The lyric result, or null without usable lines.</returns>
    public static async Task<LyricsResult?> FetchAndBuildAsync(
        Api api,
        string songId,
        string sourceName,
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var legacyTask = TryGetAsync(() => api.GetLyric(songId));
        var wordLevelTask = TryGetAsync(() => api.GetLyricNew(songId));
        await Task.WhenAll(legacyTask, wordLevelTask);

        cancellationToken.ThrowIfCancellationRequested();
        return NetEaseLyricMerge.Resolve(sourceName, await legacyTask, await wordLevelTask, request);
    }

    /// <summary>
    /// 请求单个端点；失败返回 null，让另一侧的结果继续可用。
    /// Requests one endpoint; a failure returns null so the other side's result stays usable.
    /// </summary>
    private static async Task<LyricResult?> TryGetAsync(Func<Task<LyricResult?>> request)
    {
        try
        {
            return await request();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
