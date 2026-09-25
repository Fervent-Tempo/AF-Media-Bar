using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 播放器本地缓存的共用取词边界：在后台读取并交给现有解析器，未命中时让提供器继续在线取词。
/// Shared boundary for player-local caches: reads and parses off the UI thread, leaving online lookup to the provider on a miss.
/// </summary>
internal static class LocalLyricsCacheLookup
{
    internal readonly record struct CachedText(string Main, string? Translation = null);

    public static Task<LyricsResult?> TryReadAsync(
        string sourceName,
        LyricsRequest request,
        Func<LyricsRequest, CancellationToken, CachedText?> read,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var cached = read(request, cancellationToken);
                if (cached is null || string.IsNullOrWhiteSpace(cached.Value.Main))
                {
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var document = LyricsTextParser.Parse(
                    cached.Value.Main,
                    cached.Value.Translation,
                    request: request,
                    durationSeconds: request.DurationSeconds,
                    filterInfoLines: request.FilterInfoLines);
                return document.Lines.Count > 0 ? new LyricsResult(sourceName, document) : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLogService.Current?.Warn(
                    "Lyrics",
                    $"{sourceName} 本地缓存读取失败，回退在线取词 / local cache failed: {exception.GetType().Name}");
                return null;
            }
        }, cancellationToken);
}
