using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 单个歌词源：返回该源命中的歌词，未命中返回 null。
/// A single lyric source; returns matched lyrics or null when it has nothing.
/// </summary>
public interface ILyricsProvider
{
    string SourceName { get; }

    /// <summary>
    /// 异步获取歌词结果。
    /// Asynchronously retrieves a lyric result.
    /// </summary>
    /// <param name="request">歌词查询请求 / Lyric query request.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>命中的歌词或 null / Matched lyrics or null.</returns>
    Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken);
}
