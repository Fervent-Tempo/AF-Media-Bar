using AFMediaBar.Models;

namespace AFMediaBar.Abstractions;

/// <summary>
/// 单个歌词源：返回该源命中的歌词，未命中返回 null。
/// A single lyric source; returns matched lyrics or null when it has nothing.
/// </summary>
public interface ILyricsProvider
{
    string SourceName { get; }

    Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken);
}
