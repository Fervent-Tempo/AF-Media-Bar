using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.LRCLIB;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// LRCLIB 兜底源：按歌名/歌手/专辑/时长搜索，返回带时间轴的歌词文档。
/// LRCLIB fallback: searches by title, artist, album, and duration, and returns a timed lyric document.
/// </summary>
public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.Lrclib;

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

        // 纯文本歌词没有时间轴，解析后行集合为空，按未命中处理以便继续尝试下一个来源。
        // Plain lyrics carry no timeline, so the parsed line collection is empty and the miss lets the next source run.
        var document = LyricsTextParser.Parse(
            lrc.Trim(),
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(SourceName, document) : null;
    }
}
