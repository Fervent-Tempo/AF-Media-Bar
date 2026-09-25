using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.QQMusic;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// QQ 音乐歌词源：按曲名与歌手搜索曲目，再取解密后的 QRC 逐字歌词与独立译文。
/// The QQ Music source: searches a track by title and artist, then retrieves the decrypted QRC syllable lyrics plus the
/// separate translation.
///
/// 取词按三条路径依次尝试：先读本地缓存（见 <see cref="QQMusicLocalCache"/>），命中即返回、不碰网络；未命中时走两条
/// 网络路径——新接口按数字歌曲 id 返回解密后的正文，旧接口按 songmid 返回 base64 正文；三条都拿不到正文时按未命中处理。
/// Retrieval tries three paths in order: the local cache first (see <see cref="QQMusicLocalCache"/>), returning immediately
/// on a hit without any network traffic; on a miss the two network paths follow — the new endpoint returns decrypted text for
/// the numeric song id and the legacy endpoint returns base64 text for the songmid; when none yields text the source counts as
/// a miss.
/// </summary>
public sealed class QQMusicLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();
    private readonly Func<LyricsRequest, CancellationToken, LyricsResult?> _cacheReader;

    /// <summary>使用应用内 QQ 歌词缓存读取器创建提供器。/ Creates the provider with the app's QQ lyric-cache reader.</summary>
    public QQMusicLyricsProvider() : this(TryReadCachedLyrics)
    {
    }

    internal QQMusicLyricsProvider(Func<LyricsRequest, CancellationToken, LyricsResult?> cacheReader)
    {
        _cacheReader = cacheReader ?? throw new ArgumentNullException(nameof(cacheReader));
    }

    public string SourceName => LyricsSourceCatalog.QQMusic;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        // 缓存目录扫描、文件读取、解密和解析都可能阻塞，必须在后台执行；命中时仍不访问网络。
        // Directory scanning, file reads, decryption, and parsing can block, so run them off the UI thread;
        // a cache hit still avoids the network entirely.
        var cachedResult = await Task.Run(() => _cacheReader(request, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (cachedResult is not null)
        {
            return cachedResult;
        }

        var track = LyricsSearch.ToTrackMetadata(request);
        var minimumMatch = LyricsMatchPolicy.ToMinimumMatch(request.MatchStrictness);
        var match = await LyricsSearch.MatchAsync(track, Searchers.QQMusic, minimumMatch, cancellationToken);
        if (match is not QQMusicSearchResult qq)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (main, translation) = await FetchLyricAsync(qq, cancellationToken);
        if (string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        var document = LyricsTextParser.Parse(
            main,
            translation,
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(SourceName, document) : null;
    }

    private static LyricsResult? TryReadCachedLyrics(LyricsRequest request, CancellationToken cancellationToken)
    {
        if (!QQMusicLocalCache.TryRead(
                request.Title, request.Album, cancellationToken, out var main, out var translation) ||
            string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = LyricsTextParser.Parse(
            main,
            translation,
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(LyricsSourceCatalog.QQMusic, document) : null;
    }

    private async Task<(string? Main, string? Translation)> FetchLyricAsync(
        QQMusicSearchResult match,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(match.Id))
        {
            try
            {
                var response = await _api.GetLyricsAsync(match.Id);
                if (!string.IsNullOrWhiteSpace(response?.Lyrics))
                {
                    return (response!.Lyrics, response.Trans);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 新接口失败时继续尝试旧接口。 / A failed new endpoint still leaves the legacy endpoint to try.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(match.Mid))
        {
            return (null, null);
        }

        try
        {
            var legacy = (await _api.GetLyric(match.Mid))?.Decode();
            return (legacy?.Lyric, legacy?.Trans);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }
}
