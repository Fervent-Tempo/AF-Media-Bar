using System.Diagnostics;
using System.Net.Http;
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
/// 达到 80 分可直接结束优先阶段；低分候选也取词，供协调器与备用源比较。
/// 先尝试数字歌曲 id 的新接口，再尝试 songmid 的旧接口。
/// 旧接口成功返回空正文时确认无歌词；请求或解析失败仍允许备用来源兜底。
/// </summary>
public sealed class QQMusicLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();
    private readonly HttpClient? _client;

    public QQMusicLyricsProvider() { }

    internal QQMusicLyricsProvider(HttpClient? client) => _client = client;

    public string SourceName => LyricsSourceCatalog.QQMusic;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        // 保留低分候选参与跨源比较，是否直接采纳由 LyricsService 决定。
        var match = await LyricsSearch.MatchAsync(request, Searchers.QQMusic, cancellationToken,
            LyricsRetrievalPolicy.PreferredMinimumScore, retainBelowMinimum: true);
        if (match?.Candidate is not QQMusicSearchResult qq)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (main, translation, confirmedNoLyrics) = await FetchLyricAsync(qq, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (confirmedNoLyrics) return LyricsResult.NoLyrics(SourceName, match.Score);
        return await CompleteLyricsAsync(main, translation, match.Score, request,
            token => QQMusicRomanizationProvider.FetchAsync(qq.Id, request, token, _client), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<LyricsResult?> CompleteLyricsAsync(string? main, string? translation, int score,
        LyricsRequest request, Func<CancellationToken, Task<string?>> fetchRomanization, CancellationToken cancellationToken)
    {
        var document = LyricsTextParser.Parse(main, translation, request: request,
            durationSeconds: request.DurationSeconds, filterInfoLines: request.FilterInfoLines);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Lines.Count == 0)
            return null;

        var result = new LyricsResult(LyricsSourceCatalog.QQMusic, document) { MatchScore = score };
        var wait = QQMusicRomanizationProvider.RemainingWait(request);
        if (wait < QQMusicRomanizationProvider.MinimumUsefulWait)
            return result;

        using var supplement = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = supplement.Token;
        // 同一等待上限覆盖请求、解密和合并，即使其中某一步无法立即取消，正文也能按时返回。
        var work = Task.Run(async () =>
        {
            var romanization = await fetchRomanization(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(romanization)
                ? result
                : result with { Document = LyricsTextParser.AttachRomanization(document, romanization) };
        }, token);

        try
        {
            return await work.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[QQMusic] romanization unavailable: {exception.Message}");
            return result;
        }
        finally
        {
            supplement.Cancel();
            _ = work.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private async Task<(string? Main, string? Translation, bool ConfirmedNoLyrics)> FetchLyricAsync(
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
                    return (response!.Lyrics, response.Trans, false);
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
            return (null, null, false);
        }

        try
        {
            var legacy = (await _api.GetLyric(match.Mid))?.Decode();
            if (legacy is not { Code: 0 }) return (null, null, false);
            return (legacy.Lyric, legacy.Trans, string.IsNullOrWhiteSpace(legacy.Lyric));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null, false);
        }
    }
}
