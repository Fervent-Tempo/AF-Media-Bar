using AFMediaBar.Abstractions;
using AFMediaBar.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace AFMediaBar.Services.Lyrics;

/// <summary>
/// 网易云歌词源：用歌曲 id 精确取词，返回 LRC 与可选译文。
/// NetEase provider: fetches lyrics exactly by song id, returning LRC plus an optional translation.
/// </summary>
public sealed class NetEaseLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => "Netease";

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NetEaseSongId))
        {
            return null;
        }

        LyricResult? result;
        try
        {
            result = await _api.GetLyric(request.NetEaseSongId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 单源网络失败不应中断整个兜底链。 / A single source's failure must not break the fallback chain.
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lrc = result?.Lrc?.Lyric;
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        return new LyricsResult(
            SourceName,
            lrc.Trim(),
            Normalize(result?.Tlyric?.Lyric));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
