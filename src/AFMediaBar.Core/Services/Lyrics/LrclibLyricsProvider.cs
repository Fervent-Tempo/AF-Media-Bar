using AFMediaBar.Abstractions;
using AFMediaBar.Models;
using Lyricify.Lyrics.Providers.Web.LRCLIB;

namespace AFMediaBar.Services.Lyrics;

/// <summary>
/// LRCLIB 兜底源：按歌名/歌手/专辑/时长搜索，返回带时间轴的 LRC。
/// LRCLIB fallback: searches by title/artist/album/duration and returns synced LRC.
/// </summary>
public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => "LRCLIB";

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return null;
        }

        GetLyricResult? result;
        try
        {
            result = await _api.Get(
                request.Title,
                request.Artist,
                request.Album,
                request.DurationSeconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result is null || result.Instrumental)
        {
            return null;
        }

        var lrc = result.SyncedLyrics ?? result.PlainLyrics;
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        return new LyricsResult(SourceName, lrc.Trim(), null);
    }
}
