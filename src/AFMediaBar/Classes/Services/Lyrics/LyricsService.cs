using System.Diagnostics;
using System.Linq;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词服务：把一次取词并发派发给多个来源，并按用户选择的采纳策略决定用哪一个结果。
/// The lyric service: it dispatches one lookup to several sources concurrently and picks the result to use by the
/// adoption mode the user chose.
///
/// 调度 Dispatch:
/// 1. 默认接口：由请求里的 SMTC 来源标识（AppID）映射而来（见 <see cref="LyricsConcurrencyPolicy.MapDefaultSource"/>）。
///    它不受来源开关管辖、立即并发发起，并且跨批次持续等待——慢一拍也不影响它的资格。
///    The default interface comes from the request's SMTC source id (see MapDefaultSource); it ignores the source toggles,
///    is dispatched immediately, and keeps waiting across batches — being slow never disqualifies it.
/// 2. 优先级批次：启用来源按用户顺序每 N 个一批并发；当前批次全部未命中才推进下一批。
///    Priority batches: the enabled sources run N at a time in the user's order; the next batch starts only after the
///    current one has all missed.
/// 3. 默认接口若恰好也在优先级列表里，批次计划已去重，只发一次。
///    A default interface that also sits in the priority list is de-duplicated by the batching plan and dispatched once.
///
/// 采纳 Adoption（见 <see cref="LyricsAdoptionMode"/>）：
/// - 先到先得：任一请求先返回非空结果即采纳，其余全部放弃；
/// - 偏心默认：优先级批次里先到的非空结果只作候补，默认接口命中随时取代它，默认接口未命中时候补转正；
/// - 偏心默认（限时）：候补出现后给默认接口一个倒计时，超时候补转正，不让慢的默认接口拖住整链。
/// First arrival adopts the earliest non-null result and drops the rest; prefer-default keeps the earliest priority hit
/// only as a candidate that a default-interface hit replaces at any time; the deadline variant bounds that wait with a
/// countdown so a slow default interface cannot stall the chain.
///
/// 单源预算按"等待"计算而不是"取消"（与串行时代一致）：已引用的歌词库内部请求不接受取消令牌，因此被放弃的请求
/// 仍在后台自行结束，其结果被丢弃，异常不会被观察成未处理异常。
/// The per-source budget measures waiting rather than cancellation (as in the serial era): requests inside the referenced
/// lyric library take no cancellation token, so abandoned requests finish on their own in the background, their results
/// are dropped, and their exceptions are never left unobserved.
/// </summary>
public sealed class LyricsService
{
    /// <summary>单个来源的默认等待上限：超过它就当作未命中并继续下一个来源。
    /// Default wait limit for one source: beyond it the source counts as a miss and the next one runs.</summary>
    public static readonly TimeSpan DefaultPerSourceBudget = TimeSpan.FromSeconds(6);

    /// <summary>整条兜底链的默认时间上限：用完就返回未命中，不再等待剩余来源。
    /// Default time limit for the whole chain: once used up it returns a miss instead of waiting for the remaining sources.</summary>
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(12);

    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _perSourceBudget;
    private readonly TimeSpan _totalBudget;

    /// <summary>
    /// 用默认预算创建歌词服务。
    /// Creates the lyric service with the default budgets.
    /// </summary>
    /// <param name="providers">按优先级排列的提供器 / Providers in priority order.</param>
    public LyricsService(params ILyricsProvider[] providers)
        : this(DefaultPerSourceBudget, DefaultTotalBudget, providers)
    {
    }

    /// <summary>
    /// 用显式预算创建歌词服务；非正数预算回退到默认值。
    /// Creates the lyric service with explicit budgets; a non-positive budget falls back to the default.
    /// </summary>
    /// <param name="perSourceBudget">单个来源的等待上限 / Wait limit for one source.</param>
    /// <param name="totalBudget">整条兜底链的时间上限 / Time limit for the whole chain.</param>
    /// <param name="providers">按优先级排列的提供器 / Providers in priority order.</param>
    public LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, params ILyricsProvider[] providers)
    {
        _providers = providers;
        _perSourceBudget = perSourceBudget > TimeSpan.Zero ? perSourceBudget : DefaultPerSourceBudget;
        _totalBudget = totalBudget > TimeSpan.Zero ? totalBudget : DefaultTotalBudget;
    }

    /// <summary>
    /// 获取歌词：按当前设置的并发与采纳参数派发请求，返回被采纳的结果。
    /// Get lyrics: dispatch the requests with the concurrency and adoption options of the current settings, and return the adopted result.
    /// </summary>
    /// <param name="request">歌词查询请求 / Lyric query request.</param>
    /// <param name="cancellationToken">取消令牌；调用方取消时抛出 / Cancellation token; a caller cancellation is thrown.</param>
    /// <returns>命中的歌词；全部未命中或预算用尽时为 null / The matched lyrics, or null after every miss or once the budget runs out.</returns>
    public Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken) =>
        GetLyricsAsync(request, LyricsRetrievalOptions.FromSettings(), cancellationToken);

    /// <summary>
    /// 获取歌词：并发与采纳参数由调用方显式给出（测试与特殊调用路径用）。
    /// Get lyrics with caller-supplied concurrency and adoption options (for tests and special call paths).
    /// </summary>
    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        LyricsRetrievalOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 按序查询即传统串行链：批次 1 + 先到先得 + 无默认接口——与并发机制同一代码路径，只是参数等价于逐个尝试。
        // The sequential strategy is the classic serial chain: a batch of one plus first arrival and no default interface —
        // the same code path as concurrency with options equivalent to trying sources one by one.
        if (options.QueryStrategy == LyricsQueryStrategy.Sequential)
        {
            options = LyricsRetrievalOptions.Sequential;
        }

        // 取词选项与启用来源都在发起前从设置解析：提供器因此不读设置、保持无状态；"改来源后当前这首也要重新取词"
        // 由缓存失效策略负责（见 LyricsCacheInvalidationPolicy）。
        // Both the retrieval options and the enabled sources are resolved from the settings before the first request, which keeps
        // providers stateless and free of settings reads; making the current track refetch after a source change belongs to the
        // cache invalidation policy instead (see LyricsCacheInvalidationPolicy).
        var settings = SettingsManager.Current;
        var effectiveRequest = request with
        {
            MatchStrictness = settings.LyricsMatchStrictness,
            FilterInfoLines = settings.LyricsInfoLineFilterEnabled
        };
        var priority = LyricsSourcePolicy.ResolveActive(_providers, settings.LyricsSource);

        // 默认接口在全部提供器里找，而不是只在启用的里面：来源开关只管优先级，用户关掉默认来源也不影响它照发。
        // 按序查询没有默认接口的概念——策略切换时上面已把选项换成串行参数，这里跳过映射。
        // The default interface is looked up among every provider, not only the enabled ones: the source toggles govern
        // the priority list alone, and a default source the user turned off is still dispatched. The sequential strategy
        // has no default interface; the mapping is skipped because the strategy swap above already replaced the options.
        var defaultSourceName = options.QueryStrategy == LyricsQueryStrategy.Sequential
            ? null
            : LyricsConcurrencyPolicy.MapDefaultSource(
                effectiveRequest.SourceAppId,
                effectiveRequest.NetEaseSongId,
                settings.LyricsDefaultBindings);
        var defaultProvider = defaultSourceName is null
            ? null
            : _providers.FirstOrDefault(provider => string.Equals(provider.SourceName, defaultSourceName, StringComparison.Ordinal));
        var batches = LyricsConcurrencyPolicy.PlanBatches(priority, defaultProvider, options.BatchSize);

        var startedAt = Stopwatch.GetTimestamp();
        AppLogService.Current?.Info(
            "Lyrics",
            $"取词开始 / retrieving: \"{effectiveRequest.Title}\" — \"{effectiveRequest.Artist}\" " +
            $"default={defaultProvider?.SourceName ?? "无 / none"} " +
            $"priority=[{string.Join(", ", priority.Select(provider => provider.SourceName))}] " +
            $"mode={options.AdoptionMode} batch={options.BatchSize}");

        // 取消哨兵：DefaultProvider 挂起时 WhenAny 也要能在调用方取消的那一刻醒来。
        // A cancellation sentinel: while providers hang, WhenAny must still wake the moment the caller cancels.
        var cancellationWatch = Task.Delay(Timeout.Infinite, cancellationToken);
        ProviderCall? defaultCall = defaultProvider is null
            ? null
            : new ProviderCall(defaultProvider, IsDefault: true, TryProviderAsync(
                defaultProvider, effectiveRequest, ResolveRemainingBudget(startedAt), cancellationToken));
        var candidate = (LyricsResult?)null;

        // 默认接口可能已经同步完成（本地缓存一类的快路径全程无 await）：它的结果必须在这里先查一次，
        // 否则下面的扇出循环会因为它"已完成"而永远不把它放进当批，命中就这样被静默丢掉。
        // The default interface may have completed synchronously (fast paths like the local cache never await): its
        // result has to be checked here once, or the dispatch loop below would leave a completed default out of every
        // batch and silently drop the hit.
        if (defaultCall is { } settled && settled.Task.IsCompleted)
        {
            var settledResult = await settled.Task.ConfigureAwait(false);
            defaultCall = null;
            if (settledResult is not null)
            {
                AppLogService.Current?.Info(
                    "Lyrics",
                    $"取词命中（默认接口，同步完成）/ hit (default, synchronous): {settledResult.Source} " +
                    $"{settledResult.Document.SourceFormat}/{settledResult.Document.SyncType} " +
                    $"lines={settledResult.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                return settledResult;
            }

            AppLogService.Current?.Verbose("Lyrics", "默认接口同步完成但未命中 / the default interface completed synchronously and missed");
        }

        foreach (var batch in batches)
        {
            // 候补到手后不再推进批次：它的价值就是立刻转正或被默认接口取代。
            // Batches stop advancing once a candidate exists: its whole value is to turn final at once or to be replaced by the default.
            if (candidate is not null)
            {
                break;
            }

            var budget = ResolveRemainingBudget(startedAt);
            if (budget <= TimeSpan.Zero)
            {
                AppLogService.Current?.Warn("Lyrics", "整链预算用尽 / the total budget ran out");
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var active = new List<ProviderCall>(batch.Count + 1);
            foreach (var provider in batch)
            {
                active.Add(new ProviderCall(provider, IsDefault: false, TryProviderAsync(
                    provider, effectiveRequest, budget, cancellationToken)));
            }

            if (defaultCall is { } pending && !pending.Task.IsCompleted)
            {
                active.Add(pending);
            }

            while (active.Count > 0)
            {
                // WhenAny 的公共类型是 Task（取消哨兵在列）；这里若不是哨兵赢了，赢的一定是某个提供器任务。
                // WhenAny's common type is Task (the cancellation sentinel is in the set); unless the sentinel won, the
                // winner is one of the provider tasks.
                var finishedTask = (Task<LyricsResult?>)await Task.WhenAny(
                    active.Select(entry => (Task)entry.Task).Append(cancellationWatch)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var finished = active.First(entry => ReferenceEquals(entry.Task, finishedTask));
                active.Remove(finished);

                if (finished.IsDefault)
                {
                    // 默认接口命中随时取代一切（包括先到的候补），未命中则继续等优先级。
                    // A default-interface hit replaces everything at any time (including an earlier candidate); a miss
                    // leaves the priority requests alone.
                    var defaultResult = await finishedTask.ConfigureAwait(false);
                    if (defaultResult is not null)
                    {
                        AppLogService.Current?.Info(
                            "Lyrics",
                            $"取词命中（默认接口）/ hit (default): {defaultResult.Source} {defaultResult.Document.SourceFormat}/{defaultResult.Document.SyncType} " +
                            $"lines={defaultResult.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                        return defaultResult;
                    }

                    AppLogService.Current?.Verbose("Lyrics", "默认接口未命中 / the default interface missed");
                    continue;
                }

                var result = await finishedTask.ConfigureAwait(false);
                if (result is null)
                {
                    AppLogService.Current?.Verbose("Lyrics", $"来源未命中 / miss: {finished.Provider.SourceName}");
                    continue;
                }

                if (options.AdoptionMode == LyricsAdoptionMode.FirstArrival)
                {
                    AppLogService.Current?.Info(
                        "Lyrics",
                        $"取词命中 / hit: {result.Source} {result.Document.SourceFormat}/{result.Document.SyncType} " +
                        $"lines={result.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                    return result;
                }

                // 偏心默认：本结果只作候补。停掉批次里其余优先级请求（默认接口不在其列），转去等默认接口。
                // Prefer-default: this result is only a candidate. The batch's remaining priority requests are dropped
                // (the default interface is not among them) while the default interface is waited for.
                foreach (var abandoned in active.Where(entry => !entry.IsDefault))
                {
                    ObserveFault(abandoned.Task);
                }

                active.RemoveAll(entry => !entry.IsDefault);
                candidate = result;
                AppLogService.Current?.Info(
                    "Lyrics",
                    $"优先级命中（候补）/ priority hit (candidate): {result.Source} " +
                    $"lines={result.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                break;
            }
        }

        if (candidate is null)
        {
            // 优先级全部未命中：若默认接口仍在飞，等它收尾——它自己的单源预算兜底，不会无限等。
            // Every priority source missed: while the default interface is still in flight it is awaited to its own
            // per-source budget, so the wait is never unbounded.
            if (defaultCall is { } settle && !settle.Task.IsCompleted)
            {
                var defaultResult = await settle.Task.ConfigureAwait(false);
                if (defaultResult is not null)
                {
                    AppLogService.Current?.Info(
                        "Lyrics",
                        $"取词命中（默认接口）/ hit (default): {defaultResult.Source} " +
                        $"lines={defaultResult.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                    return defaultResult;
                }
            }

            AppLogService.Current?.Info(
                "Lyrics",
                $"全部来源未命中 / no source hit ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
            return null;
        }

        // 候补在手：给默认接口最后的机会。已完成的默认接口也要查一次结果——
        // 它可能在候补出现前就命中了，只是调度循环先拿到了候补。
        // With a candidate in hand the default interface gets its last chance. A completed default is checked too:
        // it may have hit before the candidate appeared, while the dispatch loop picked up the candidate first.
        if (defaultCall is { } last)
        {
            if (!last.Task.IsCompleted &&
                options.AdoptionMode == LyricsAdoptionMode.PreferDefaultSourceWithDeadline)
            {
                var winner = await Task.WhenAny(
                    last.Task,
                    Task.Delay(options.AdoptionDeadline, cancellationToken)).ConfigureAwait(false);
                if (!ReferenceEquals(winner, last.Task))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ObserveFault(last.Task);
                    AppLogService.Current?.Info(
                        "Lyrics",
                        $"默认接口倒计时到期，候补转正 / the default deadline expired, the candidate wins: {candidate.Source}");
                    return candidate;
                }
            }

            // 无倒计时模式：等默认接口跑完它自己的单源预算；已完成的直接取结果。
            // Without a deadline the default interface is awaited to the end of its own per-source budget; a completed
            // one just yields its result.
            var result = await last.Task.ConfigureAwait(false);
            if (result is not null)
            {
                AppLogService.Current?.Info(
                    "Lyrics",
                    $"取词命中（默认接口取代候补）/ hit (default replaces the candidate): {result.Source} " +
                    $"lines={result.Document.Lines.Count} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
                return result;
            }
        }

        AppLogService.Current?.Info(
            "Lyrics",
            $"候补转正 / the candidate wins: {candidate.Source} ({Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms)");
        return candidate;
    }

    private TimeSpan ResolveRemainingBudget(long startedAt)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var remaining = _totalBudget - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < _perSourceBudget ? remaining : _perSourceBudget;
    }

    private static async Task<LyricsResult?> TryProviderAsync(
        ILyricsProvider provider,
        LyricsRequest request,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // 同步抛出的提供器同样按未命中处理：兜底链的健壮性不能依赖每个提供器都自己捕获异常。
        // A provider that throws synchronously counts as a miss too: the chain's robustness cannot depend on every provider
        // catching its own exceptions.
        Task<LyricsResult?> task;
        try
        {
            task = provider.GetLyricsAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }

        var finished = await Task.WhenAny(task, Task.Delay(budget));
        if (!ReferenceEquals(finished, task))
        {
            ObserveFault(task);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            return await task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 提供器自身的取消按未命中处理，调用方的取消在上面已经抛出。
            // A provider's own cancellation counts as a miss; a caller cancellation was already thrown above.
            return null;
        }
        catch
        {
            // 单个来源的异常不得打断整个兜底链。 / One source's exception must not break the whole fallback chain.
            return null;
        }
    }

    /// <summary>
    /// 消费被放弃任务的异常，避免它变成未观察的任务异常。
    /// Consumes the exception of an abandoned task so it cannot become an unobserved task exception.
    /// </summary>
    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>一次并发请求的登记：提供器、它是不是默认接口，以及它自己的飞行任务。
    /// One concurrent request's registration: the provider, whether it is the default interface, and its own in-flight task.</summary>
    private readonly record struct ProviderCall(
        ILyricsProvider Provider,
        bool IsDefault,
        Task<LyricsResult?> Task);
}
