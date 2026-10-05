// 协调优先源/并发备用源取词，拥有一次请求的预算与取消；不持久化原文或管理媒体身份。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>QQ 优先；低于门槛时并发备用源，优先当前播放器对应源，再按分数选择。</summary>
public sealed class LyricsService
{
    /// <summary>单源等待上限。/ Per-source wait limit.</summary>
    public static readonly TimeSpan DefaultPerSourceBudget = TimeSpan.FromSeconds(6);
    /// <summary>包括优先阶段的总等待上限。/ Total wait limit including the preferred stage.</summary>
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(12);
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _perSourceBudget;
    private readonly TimeSpan _totalBudget;

    /// <summary>使用默认预算创建协调器。/ Creates the coordinator with default budgets.</summary>
    public LyricsService(params ILyricsProvider[] providers) : this(DefaultPerSourceBudget, DefaultTotalBudget, providers) { }

    /// <summary>使用显式预算创建协调器。/ Creates the coordinator with explicit wait budgets.</summary>
    public LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, params ILyricsProvider[] providers)
    {
        _providers = providers;
        _perSourceBudget = perSourceBudget > TimeSpan.Zero ? perSourceBudget : DefaultPerSourceBudget;
        _totalBudget = totalBudget > TimeSpan.Zero ? totalBudget : DefaultTotalBudget;
    }

    /// <summary>按启用来源执行固定策略。/ Retrieves with the fixed policy and enabled sources.</summary>
    public async Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = SettingsManager.Current;
        var effectiveRequest = request with
        {
            FilterInfoLines = settings.LyricsInfoLineFilterEnabled,
            ArtistSeparators = settings.LyricsArtistSeparators
        };
        var plan = LyricsRetrievalPolicy.Plan(_providers, LyricsSourcePolicy.ResolveActive(_providers, settings.LyricsSource));
        var started = Stopwatch.GetTimestamp();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LyricsResult? preferredResult = null;
        try
        {
            if (plan.Preferred is { } preferred)
            {
                preferredResult = await TryProviderAsync(preferred, effectiveRequest, Remaining(started), work.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (preferredResult is not null && preferredResult.MatchScore >= LyricsRetrievalPolicy.PreferredMinimumScore)
                    return LogResult(preferredResult, started);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var budget = Remaining(started);
            if (budget <= TimeSpan.Zero) return LogResult(preferredResult, started);
            // 等的是各自受预算约束的包装任务，不会无限等待底层歌词库。
            var tasks = plan.Fallbacks.Select(provider => TryProviderAsync(provider, effectiveRequest, budget, work.Token)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Provider identity owns routing; a result's display/source label may differ from SourceName.
            var currentPlayback = plan.Preferred is { } currentPreferred &&
                LyricsSourcePolicy.IsCurrentPlaybackSource(effectiveRequest.PlaybackSourceId, currentPreferred.SourceName)
                ? preferredResult : null;
            for (var index = 0; currentPlayback is null && index < plan.Fallbacks.Count; index++)
            {
                if (LyricsSourcePolicy.IsCurrentPlaybackSource(effectiveRequest.PlaybackSourceId, plan.Fallbacks[index].SourceName))
                    currentPlayback = results[index];
            }
            return LogResult(LyricsRetrievalPolicy.Select(results, preferredResult, currentPlayback), started);
        }
        finally { work.Cancel(); }
    }

    private TimeSpan Remaining(long started)
    {
        var remaining = _totalBudget - Stopwatch.GetElapsedTime(started);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < _perSourceBudget ? remaining : _perSourceBudget;
    }

    private static async Task<LyricsResult?> TryProviderAsync(ILyricsProvider provider, LyricsRequest request, TimeSpan budget, CancellationToken token)
    {
        if (budget <= TimeSpan.Zero) return null;
        // 超时取消合作式工作；库内不可取消的请求继续结束，晚到结果不采纳且异常被观察。
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<LyricsResult?>? task = null;
        try
        {
            task = provider.GetLyricsAsync(request, source.Token);
            var result = await task.WaitAsync(budget, token).ConfigureAwait(false);
            AppLogService.Current?.Info("Lyrics", $"来源取词结果: source={provider.SourceName} " +
                $"status={result?.Status.ToString() ?? "FailedOrUnmatched"} score={result?.MatchScore.ToString() ?? "none"}");
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // WaitAsync 使用 TaskCanceledException；统一为调用方令牌的取消出口，保持既有契约。
            token.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Lyrics", $"来源取词失败: {provider.SourceName} {exception.GetType().Name}");
            return null;
        }
        finally
        {
            source.Cancel();
            if (task is not null)
                _ = task.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private static LyricsResult? LogResult(LyricsResult? result, long started)
    {
        AppLogService.Current?.Info("Lyrics", $"取词结束: source={result?.Source ?? "none"} " +
            $"score={result?.MatchScore.ToString() ?? "none"} status={result?.Status.ToString() ?? "FailedOrUnmatched"} " +
            $"elapsed={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms");
        return result;
    }
}
