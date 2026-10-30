// 协调默认与手动规则取词，拥有一次请求的预算与取消；不持久化原文或管理媒体身份。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>按默认规则仲裁，或按手动请求方式与优先级获取歌词。</summary>
public sealed class LyricsService
{
    /// <summary>单源等待上限。/ Per-source wait limit.</summary>
    public static readonly TimeSpan DefaultPerSourceBudget = TimeSpan.FromSeconds(6);
    /// <summary>包括优先阶段的总等待上限。/ Total wait limit including the preferred stage.</summary>
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(12);
    /// <summary>手动规则的单源等待上限，不受默认整链预算限制。</summary>
    public static readonly TimeSpan DefaultManualPerSourceBudget = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _perSourceBudget;
    private readonly TimeSpan _totalBudget;
    private readonly TimeSpan _manualPerSourceBudget;
    private readonly RepeatedFailureReporter _failures = new();
    private readonly ILyricsProvider? _localProvider;

    /// <summary>使用默认预算创建协调器。/ Creates the coordinator with default budgets.</summary>
    public LyricsService(params ILyricsProvider[] providers) : this(DefaultPerSourceBudget, DefaultTotalBudget, providers) { }

    /// <summary>使用显式预算创建协调器。/ Creates the coordinator with explicit wait budgets.</summary>
    public LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, params ILyricsProvider[] providers)
        : this(perSourceBudget, totalBudget, DefaultManualPerSourceBudget, providers) { }

    /// <summary>带本地来源的构造：本地默认先跑一次，保守模式改为在线全空之后兜底。/ Creates the coordinator with a local source that runs first by default and only as a fallback in conservative mode.</summary>
    public LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, ILyricsProvider? localProvider, params ILyricsProvider[] providers)
        : this(perSourceBudget, totalBudget, DefaultManualPerSourceBudget, providers) => _localProvider = localProvider;

    internal LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, TimeSpan manualPerSourceBudget, params ILyricsProvider[] providers)
    {
        _providers = providers;
        _perSourceBudget = perSourceBudget > TimeSpan.Zero ? perSourceBudget : DefaultPerSourceBudget;
        _totalBudget = totalBudget > TimeSpan.Zero ? totalBudget : DefaultTotalBudget;
        _manualPerSourceBudget = manualPerSourceBudget > TimeSpan.Zero ? manualPerSourceBudget : DefaultManualPerSourceBudget;
    }

    /// <summary>固定本次设置快照，按启用来源及所选规则取词。</summary>
    public async Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = SettingsManager.Current;
        var effectiveRequest = request with
        {
            FilterInfoLines = settings.LyricsInfoLineFilterEnabled,
            ArtistSeparators = settings.LyricsArtistSeparators
        };
        var sourceSettings = settings.LyricsSource.Normalize();
        var active = LyricsSourcePolicy.ResolveActive(_providers, sourceSettings);
        var trace = new LyricsRequestLog(effectiveRequest, sourceSettings, AppLogService.Current);
        var started = Stopwatch.GetTimestamp();
        // 默认本地排在所有在线规则之前：命中就直接用，未命中或解析失败才继续在线链路。
        // 它不参与启用列表与排序，因此用户怎么排在线来源都不会把本地挤到后面。
        LyricsResult? localFirst = null;
        if (!sourceSettings.LocalFallbackOnly)
        {
            localFirst = await TryLocalAsync(effectiveRequest, Remaining(started), trace, cancellationToken).ConfigureAwait(false);
            // 有歌手或时长佐证的命中直接采信；只靠标题命中的先让在线查一轮，同名翻唱才不会顶掉原版。
            if (localFirst is { LowConfidence: false } confident && IsAvailable(confident)) return trace.Finish(confident);
        }

        var result = sourceSettings.Rule == LyricsSourceRule.Manual
            ? await GetManualLyricsAsync(effectiveRequest, active, sourceSettings, trace, cancellationToken).ConfigureAwait(false)
            : await GetDefaultLyricsAsync(effectiveRequest, active, started, trace, cancellationToken).ConfigureAwait(false);

        // 保守模式：在线来源一个都没给出歌词时才用本地兜底，避免同名本地文件顶掉本来正常的在线歌词。
        if (sourceSettings.LocalFallbackOnly && !IsAvailable(result))
        {
            var fallback = await TryLocalAsync(effectiveRequest, LocalFallbackBudget(started), trace, cancellationToken).ConfigureAwait(false);
            if (IsAvailable(fallback)) return trace.Finish(fallback);
        }

        // 在线也没给出歌词时，低置信度的本地命中仍然比没有歌词强。
        if (!IsAvailable(result) && IsAvailable(localFirst)) return trace.Finish(localFirst);

        return trace.Finish(result);
    }

    /// <summary>跑一次本地来源；未配置或没命中时返回 null。/ Runs the local source once and returns null when it is unconfigured or finds nothing.</summary>
    private async Task<LyricsResult?> TryLocalAsync(LyricsRequest request, TimeSpan budget, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        if (_localProvider is not { } local) return null;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var result = await TryProviderAsync(local, request, budget, work.Token, trace).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { work.Cancel(); }
    }

    private async Task<LyricsResult?> GetDefaultLyricsAsync(LyricsRequest request, IReadOnlyList<ILyricsProvider> active,
        long started, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        var plan = LyricsRetrievalPolicy.Plan(_providers, active);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LyricsResult? preferredResult = null;
        try
        {
            if (plan.Preferred is { } preferred)
            {
                preferredResult = await TryProviderAsync(preferred, request, Remaining(started), work.Token, trace).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (preferredResult is not null && preferredResult.MatchScore >= LyricsRetrievalPolicy.PreferredMinimumScore)
                    return preferredResult;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var budget = Remaining(started);
            if (budget <= TimeSpan.Zero) return preferredResult;
            // 等的是各自受预算约束的包装任务，不会无限等待底层歌词库。
            var tasks = plan.Fallbacks.Select(provider => TryProviderAsync(provider, request, budget, work.Token, trace)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Provider identity owns routing; a result's display/source label may differ from SourceName.
            var currentPlayback = plan.Preferred is { } currentPreferred &&
                LyricsSourcePolicy.IsCurrentPlaybackSource(request.PlaybackSourceId, currentPreferred.SourceName)
                ? preferredResult : null;
            for (var index = 0; currentPlayback is null && index < plan.Fallbacks.Count; index++)
            {
                if (LyricsSourcePolicy.IsCurrentPlaybackSource(request.PlaybackSourceId, plan.Fallbacks[index].SourceName))
                    currentPlayback = results[index];
            }
            return LyricsRetrievalPolicy.Select(results, preferredResult, currentPlayback);
        }
        finally { work.Cancel(); }
    }

    private async Task<LyricsResult?> GetManualLyricsAsync(LyricsRequest request, IReadOnlyList<ILyricsProvider> providers,
        LyricsSourceSettings settings, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LyricsResult? noLyrics = null;
        try
        {
            if (settings.AttemptMode == LyricsSourceAttemptMode.Sequential)
            {
                foreach (var provider in providers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await TryProviderAsync(provider, request, _manualPerSourceBudget, work.Token, trace).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsAvailable(result)) return result;
                    if (result?.Status == LyricsResultStatus.NoLyrics) noLyrics ??= result;
                }
                return noLyrics;
            }

            var firstAvailable = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<LyricsResult?> QueryAsync(ILyricsProvider provider)
            {
                var result = await TryProviderAsync(provider, request, _manualPerSourceBudget, work.Token, trace).ConfigureAwait(false);
                if (IsAvailable(result)) firstAvailable.TrySetResult(result);
                return result;
            }
            var tasks = providers.Select(QueryAsync).ToArray();
            var all = Task.WhenAll(tasks);
            // 提前采纳结果后，其余包装任务仍会因取消退出；它们的异常也必须被观察。
            _ = all.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            if (settings.ResultMode == LyricsSourceResultMode.FirstAvailable)
            {
                await Task.WhenAny(firstAvailable.Task, all).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (firstAvailable.Task.IsCompletedSuccessfully)
                    return await firstAvailable.Task.ConfigureAwait(false);
            }
            // 顺序等待已同时启动的任务；后序即使先返回，也不能越过仍在等待的前序来源。
            foreach (var task in tasks)
            {
                var result = await task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (IsAvailable(result)) return result;
                if (result?.Status == LyricsResultStatus.NoLyrics) noLyrics ??= result;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return noLyrics;
        }
        finally { work.Cancel(); }
    }

    private static bool IsAvailable(LyricsResult? result) =>
        result is { Status: LyricsResultStatus.Available, Document.Lines.Count: > 0 };

    /// <summary>本地兜底至少有一份单来源预算：在线阶段可能已耗尽总预算，不能让已经找到文件的本地来源白等。</summary>
    private TimeSpan LocalFallbackBudget(long started)
    {
        var remaining = Remaining(started);
        return remaining > TimeSpan.Zero ? remaining : _perSourceBudget;
    }

    private TimeSpan Remaining(long started)
    {
        var remaining = _totalBudget - Stopwatch.GetElapsedTime(started);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < _perSourceBudget ? remaining : _perSourceBudget;
    }

    private async Task<LyricsResult?> TryProviderAsync(ILyricsProvider provider, LyricsRequest request, TimeSpan budget, CancellationToken token, LyricsRequestLog trace)
    {
        if (budget <= TimeSpan.Zero) return null;
        var attempt = trace.Begin(provider.SourceName);
        // 超时取消合作式工作；库内不可取消的请求继续结束，晚到结果不采纳且异常被观察。
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<LyricsResult?>? task = null;
        try
        {
            task = provider.GetLyricsAsync(request, source.Token);
            var result = await task.WaitAsync(budget, token).ConfigureAwait(false);
            trace.Record(attempt, provider.SourceName, result?.Status.ToString() ?? "FailedOrUnmatched", result?.MatchScore);
            if (result is not null)
                _failures.Recover(AppLogService.Current, "Lyrics", provider.SourceName, "歌词来源请求已恢复");
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // WaitAsync 使用 TaskCanceledException；统一为调用方令牌的取消出口，保持既有契约。
            trace.Record(attempt, provider.SourceName, "Cancelled");
            token.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception exception)
        {
            trace.Record(attempt, provider.SourceName, exception is TimeoutException ? "Timeout" : $"Error({exception.GetType().Name})");
            _failures.Report(AppLogService.Current, "Lyrics", provider.SourceName, "歌词来源请求失败", exception);
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

}
