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
    /// <summary>在线阶段（含优先源与备用源）的总等待上限；本地阶段有自己的一份预算，不占这里。/ Total wait limit for the online stage; the local stage has its own budget.</summary>
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(12);
    /// <summary>手动规则的单源等待上限，不受默认整链预算限制。</summary>
    public static readonly TimeSpan DefaultManualPerSourceBudget = TimeSpan.FromSeconds(5);
    /// <summary>Apple Music 原名解析的等待上限；在线阶段的 deadline 因此是总预算再加这一份（默认 14 秒）。</summary>
    public static readonly TimeSpan DefaultOriginalNameBudget = TimeSpan.FromSeconds(2);
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _perSourceBudget;
    private readonly TimeSpan _totalBudget;
    private readonly TimeSpan _manualPerSourceBudget;
    private readonly RepeatedFailureReporter _failures = new();
    private readonly ILyricsProvider? _localProvider;
    private readonly AppleMusicOriginalMetadataResolver? _originalNames;
    private readonly TimeSpan _originalNameBudget = DefaultOriginalNameBudget;

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

    /// <summary>带 Apple Music 原名解析器的构造；解析器的生命周期归组合根。</summary>
    internal LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, TimeSpan originalNameBudget, ILyricsProvider? localProvider,
        AppleMusicOriginalMetadataResolver originalNames, params ILyricsProvider[] providers)
        : this(perSourceBudget, totalBudget, localProvider, providers)
    {
        _originalNames = originalNames;
        _originalNameBudget = originalNameBudget > TimeSpan.Zero ? originalNameBudget : DefaultOriginalNameBudget;
    }

    /// <summary>固定本次设置快照，按启用来源及所选规则取词。</summary>
    public async Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = SettingsManager.Current;
        var effectiveRequest = request with
        {
            FilterInfoLines = settings.LyricsInfoLineFilterEnabled,
            HideInstrumentalLyrics = settings.LyricsInstrumentalTitleOnlyEnabled,
            ArtistSeparators = settings.LyricsArtistSeparators
        };
        var sourceSettings = settings.LyricsSource.Normalize();
        var active = LyricsSourcePolicy.ResolveActive(_providers, sourceSettings);
        var trace = new LyricsRequestLog(effectiveRequest, sourceSettings, AppLogService.Current);
        if (UsesOriginalNames(effectiveRequest, sourceSettings, active))
            return trace.Finish(await GetAppleMusicLyricsAsync(effectiveRequest, sourceSettings, active, trace, cancellationToken).ConfigureAwait(false));
        // 默认本地排在所有在线规则之前：命中就直接用，未命中或解析失败才继续在线链路。
        // 它不参与启用列表与排序，因此用户怎么排在线来源都不会把本地挤到后面。
        // 开关关闭时整段跳过：不给它留预算，也不会在日志里留下一次空的本地尝试。
        var useLocal = sourceSettings.LocalEnabled;
        LyricsResult? localFirst = null;
        if (useLocal && !sourceSettings.LocalFallbackOnly)
        {
            // 本地是独立阶段：用自己的单来源预算，耗时不计入在线总预算。
            localFirst = await TryLocalAsync(effectiveRequest, _perSourceBudget, trace, cancellationToken).ConfigureAwait(false);
            // 有歌手或时长佐证的命中直接采信；只靠标题命中的先让在线查一轮，同名翻唱才不会顶掉原版。
            if (localFirst is { LowConfidence: false } confident && IsAvailable(confident)) return trace.Finish(confident);
        }

        // 在线阶段从本地结束后重新计时，本地扫目录再慢也不会吃掉在线的回退机会。
        var onlineStarted = Stopwatch.GetTimestamp();
        var result = sourceSettings.Rule == LyricsSourceRule.Manual
            ? await GetManualLyricsAsync(effectiveRequest, active, sourceSettings, trace, cancellationToken).ConfigureAwait(false)
            : await GetDefaultLyricsAsync(effectiveRequest, active, onlineStarted, trace, cancellationToken).ConfigureAwait(false);

        // 保守模式：在线来源一个都没给出歌词时才用本地兜底，避免同名本地文件顶掉本来正常的在线歌词。
        if (useLocal && sourceSettings.LocalFallbackOnly && !IsAvailable(result))
        {
            var fallback = await TryLocalAsync(effectiveRequest, _perSourceBudget, trace, cancellationToken).ConfigureAwait(false);
            if (IsAvailable(fallback)) return trace.Finish(fallback);
        }

        // 在线也没给出歌词时，低置信度的本地命中仍然比没有歌词强。
        // 在线给出了歌词、但分数明显不如本地时同理：只靠标题命中的本地结果，也可能比一条勉强及格的在线结果可靠。
        if (localFirst is { } localCandidate && IsAvailable(localCandidate) &&
            ShouldPreferLocal(localCandidate, result)) return trace.Finish(localCandidate);

        return trace.Finish(result);
    }

    /// <summary>在线结果要低出这么多分，才认定它明显不如本地命中。/ How far the online result must fall behind before the local hit wins.</summary>
    private const int LocalPrecedenceMargin = 20;

    /// <summary>
    /// 本地结果何时压过在线结果：只有「只靠标题命中」这一类低置信参与分差比较——它可能只是文件没写歌手，
    /// 而在线那条可能连及格线都没过。有反证的本地命中（标题不够像、歌手相反、版本冲突）一律让位；
    /// 在线没给出歌词时，本地依旧兜底。
    /// </summary>
    internal static bool ShouldPreferLocal(LyricsResult local, LyricsResult? online) =>
        online is not { } found || !IsAvailable(found) ||
        (local.LowConfidenceReason == LyricsLowConfidenceReason.TitleOnly &&
         local.MatchScore > found.MatchScore + LocalPrecedenceMargin);

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

    /// <summary>
    /// 默认规则、Apple Music 来源、开关开启、歌手不为空且至少启用一个在线来源时，才解析原名。
    /// 歌手为空时校验不可能通过，Apple Music 加载下一首时的「Connecting…」占位也因此不会请求曲库。
    /// </summary>
    private bool UsesOriginalNames(LyricsRequest request, LyricsSourceSettings settings, IReadOnlyList<ILyricsProvider> active) =>
        _originalNames is not null && settings.Rule == LyricsSourceRule.Default && settings.AppleMusicOriginalNamesEnabled &&
        active.Count > 0 && !string.IsNullOrWhiteSpace(request.Artist) && AppleMusicCatalogPolicy.IsAppleMusic(request.PlaybackSourceId);

    /// <summary>
    /// Apple Music 的默认规则取词：本地阶段与首轮在线照旧；首轮不可靠时把译名还原为原名再取一轮，解析与第二轮都放进在线阶段，
    /// 与备用源并行，共用一个 deadline。保守模式的本地兜底移到两轮之后，一次扫描同时匹配两组名称。
    /// </summary>
    private async Task<LyricsResult?> GetAppleMusicLyricsAsync(LyricsRequest request, LyricsSourceSettings settings,
        IReadOnlyList<ILyricsProvider> active, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        var local = settings.LocalEnabled && _localProvider is { } provider ? new LocalLookup(provider) : null;
        LyricsResult? localFirst = null;
        if (local is { } firstLocal && !settings.LocalFallbackOnly)
        {
            // 本地是独立阶段：目录清单保留到本次取词结束，第二轮只在清单上匹配原名。
            localFirst = await TryProviderAsync(firstLocal.SourceName, token => firstLocal.MatchAsync(request, token),
                _perSourceBudget, cancellationToken, trace).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (AppleMusicCatalogPolicy.IsCorroborated(localFirst)) return localFirst;
        }

        var rounds = await GetOriginalNameRoundsAsync(request, settings, active, Stopwatch.GetTimestamp(),
            settings.LocalFallbackOnly ? null : local, trace, cancellationToken).ConfigureAwait(false);
        // 第二轮在取消后仍会交回已完成的部分；调用方取消必须照常向上抛出。
        cancellationToken.ThrowIfCancellationRequested();
        var picked = AppleMusicCatalogPolicy.Pick(new(rounds.First, localFirst, rounds.SecondOnline, rounds.SecondLocal));
        if (local is not { } fallbackLocal || !settings.LocalFallbackOnly || IsAvailable(picked)) return picked;

        // 保守模式：两轮在线都没有歌词才读本地；有佐证的优先，同级时 SMTC 名优先，之后才轮到确认无歌词。
        var original = rounds.Original;
        var fallback = await TryProviderAsync(fallbackLocal.SourceName, async token =>
        {
            var current = await fallbackLocal.MatchAsync(request, token).ConfigureAwait(false);
            if (original is null || AppleMusicCatalogPolicy.IsCorroborated(current)) return current;
            var restored = await fallbackLocal.MatchAsync(original, token).ConfigureAwait(false);
            return AppleMusicCatalogPolicy.IsCorroborated(restored) || !IsAvailable(current) ? restored : current;
        }, _perSourceBudget, cancellationToken, trace).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return IsAvailable(fallback) ? fallback : picked;
    }

    /// <summary>在线阶段两轮的结果；第二轮未运行、未解析出原名或被提前结束时对应项为 null。</summary>
    private readonly record struct OriginalNameRounds(
        LyricsResult? First, LyricsResult? SecondOnline, LyricsResult? SecondLocal, LyricsRequest? Original);

    /// <summary>第二轮：原名请求及其在线、本地结果。</summary>
    private sealed record OriginalNameRound(LyricsRequest Request, LyricsResult? Online, LyricsResult? Local);

    /// <summary>
    /// 在线阶段：首轮与现有默认规则相同（单源与首轮预算不变）。优先阶段结束时结果不可靠，就与备用源同时启动第二轮。
    /// deadline 为在线阶段起点加总预算与解析预算，到期取消第二轮未完成的部分；调用方取消照常向上抛出。
    /// </summary>
    private async Task<OriginalNameRounds> GetOriginalNameRoundsAsync(LyricsRequest request, LyricsSourceSettings settings,
        IReadOnlyList<ILyricsProvider> active, long started, LocalLookup? local, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        var plan = LyricsRetrievalPolicy.Plan(_providers, active);
        var deadline = _totalBudget + _originalNameBudget;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var stage = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stage.CancelAfter(deadline);
        Task<LyricsResult?>? first = null;
        Task<OriginalNameRound?>? second = null;
        try
        {
            LyricsResult? preferredResult = null;
            if (plan.Preferred is { } preferred)
            {
                preferredResult = await TryProviderAsync(preferred, request, Remaining(started), work.Token, trace).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (preferredResult is not null && preferredResult.MatchScore >= LyricsRetrievalPolicy.PreferredMinimumScore)
                {
                    if (AppleMusicCatalogPolicy.IsReliable(preferredResult)) return new(preferredResult, null, null, null);
                    // 优先源确认无歌词且分数够高：首轮按现有规则结束，只追加第二轮。
                    first = Task.FromResult<LyricsResult?>(preferredResult);
                }
            }
            first ??= GetFallbackLyricsAsync(plan, request, preferredResult, started, work.Token, trace, cancellationToken);
            var localDecided = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            second = GetOriginalNameRoundAsync(request, settings, active, started, deadline, local, localDecided, stage.Token, cancellationToken);
            while (true)
            {
                // 第二轮本地有佐证的命中排在首轮之前，一出现就返回。
                if (localDecided.Task.IsCompletedSuccessfully && AppleMusicCatalogPolicy.IsCorroborated(localDecided.Task.Result))
                    return new(first.IsCompletedSuccessfully ? first.Result : null, null, localDecided.Task.Result, null);
                if (first.IsCompleted)
                {
                    var firstResult = await first.ConfigureAwait(false);
                    // 首轮可靠，且第二轮已不可能给出有佐证的本地命中：不再等第二轮。
                    if (AppleMusicCatalogPolicy.IsReliable(firstResult) && localDecided.Task.IsCompleted)
                        return new(firstResult, null, null, null);
                    if (second.IsCompleted)
                    {
                        var round = await second.ConfigureAwait(false);
                        return new(firstResult, round?.Online, round?.Local, round?.Request);
                    }
                }
                // 判断与收集之间任务可能已全部完成；WhenAny 不接受空集合，交给下一轮判断。
                var pending = new Task[] { first, second, localDecided.Task }.Where(task => !task.IsCompleted).ToArray();
                if (pending.Length > 0) await Task.WhenAny(pending).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            work.Cancel();
            stage.Cancel();
            // 提前返回时遗留的首轮备用源与第二轮会因取消退出，异常照常观察。
            Observe(first);
            Observe(second);
        }
    }

    /// <summary>
    /// 第二轮：先在 min(解析预算, deadline 剩余) 内解析原名；成功后用原名并发查所有已启用在线来源，并在已有目录清单上匹配本地，
    /// 两者共用 min(单源预算, deadline 剩余)。<paramref name="localDecided"/> 在本地结果确定或已不可能时完成。
    /// </summary>
    private async Task<OriginalNameRound?> GetOriginalNameRoundAsync(LyricsRequest request, LyricsSourceSettings settings,
        IReadOnlyList<ILyricsProvider> active, long started, TimeSpan deadline, LocalLookup? local,
        TaskCompletionSource<LyricsResult?> localDecided, CancellationToken token, CancellationToken cancellationToken)
    {
        if (local is not { CanMatchAgain: true }) localDecided.TrySetResult(null);
        LyricsRequestLog? trace = null;
        LyricsRequest? originalRequest = null;
        LyricsResult? online = null, localResult = null;
        try
        {
            var original = await ResolveOriginalAsync(request, Until(started, deadline, _originalNameBudget), token, cancellationToken)
                .ConfigureAwait(false);
            if (original is null) return null;
            var restored = request with
            {
                Title = original.Title,
                Artist = original.Artist,
                Album = original.Album,
                DurationSeconds = AppleMusicCatalogPolicy.SecondRoundDuration(request.DurationSeconds, original.DurationSeconds)
            };
            originalRequest = restored;
            trace = new LyricsRequestLog(restored, settings, AppLogService.Current);
            var budget = Until(started, deadline, _perSourceBudget);
            var localTask = local is { CanMatchAgain: true } files
                ? TryProviderAsync(files.SourceName, cancellation => files.MatchAsync(restored, cancellation), budget, token, trace)
                : Task.FromResult<LyricsResult?>(null);
            _ = localTask.ContinueWith(task => localDecided.TrySetResult(task.IsCompletedSuccessfully ? task.Result : null),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try { online = await GetOriginalOnlineAsync(restored, active, budget, token, trace).ConfigureAwait(false); }
            finally { Observe(localTask); }
            localResult = await localTask.ConfigureAwait(false);
            return new(restored, online, localResult);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 到达 deadline，或首轮已可靠而不再等待：未完成的部分记为未命中。
            return originalRequest is null ? null : new(originalRequest, online, localResult);
        }
        finally
        {
            localDecided.TrySetResult(null);
            trace?.Finish(AppleMusicCatalogPolicy.Pick(new(null, null, online, localResult)));
        }
    }

    /// <summary>解析原名；请求本身随预算或 deadline 取消，超时与失败都只表示没有原名可重试。</summary>
    private async Task<AppleMusicOriginal?> ResolveOriginalAsync(LyricsRequest request, TimeSpan budget, CancellationToken token,
        CancellationToken cancellationToken)
    {
        if (budget <= TimeSpan.Zero || _originalNames is not { } resolver) return null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(budget);
        try { return await resolver.ResolveAsync(request, limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    /// <summary>第二轮在线：所有已启用来源用原名并发查询；优先源可靠时立即采纳，否则按默认规则选择。</summary>
    private async Task<LyricsResult?> GetOriginalOnlineAsync(LyricsRequest request, IReadOnlyList<ILyricsProvider> active,
        TimeSpan budget, CancellationToken token, LyricsRequestLog trace)
    {
        var plan = LyricsRetrievalPolicy.Plan(_providers, active);
        using var round = CancellationTokenSource.CreateLinkedTokenSource(token);
        var preferred = plan.Preferred is { } source ? TryProviderAsync(source, request, budget, round.Token, trace) : null;
        var fallbackTasks = plan.Fallbacks.Select(provider => TryProviderAsync(provider, request, budget, round.Token, trace)).ToArray();
        var fallbacks = Task.WhenAll(fallbackTasks);
        try
        {
            // 到 deadline 时来源可能先被取消而不是先超时；已经拿到的结果不能因此丢掉，调用方取消由上层检查。
            LyricsResult? preferredResult = null;
            if (preferred is not null)
            {
                try { preferredResult = await preferred.ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                if (AppleMusicCatalogPolicy.IsReliable(preferredResult)) return preferredResult;
            }
            try { await fallbacks.ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return Select(plan, request, preferredResult,
                fallbackTasks.Select(task => task.IsCompletedSuccessfully ? task.Result : null).ToArray());
        }
        finally
        {
            round.Cancel();
            Observe(fallbacks);
        }
    }

    /// <summary>距 deadline 的剩余时间，不超过 <paramref name="cap"/>。</summary>
    private static TimeSpan Until(long started, TimeSpan deadline, TimeSpan cap)
    {
        var remaining = deadline - Stopwatch.GetElapsedTime(started);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < cap ? remaining : cap;
    }

    /// <summary>不再等待的任务仍会结束；观察其异常，避免未观察异常。</summary>
    private static void Observe(Task? task) =>
        task?.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>
    /// 一次取词内的本地来源：文件来源只枚举一次目录，之后按不同名称匹配同一份清单；其它实现每次按名称直接调用。
    /// 各次匹配由调用方串行开始，清单随本次取词结束丢弃。
    /// </summary>
    private sealed class LocalLookup(ILyricsProvider provider)
    {
        private readonly LocalFileLyricsProvider? _files = provider as LocalFileLyricsProvider;
        private Task<LocalLyricsScan?>? _scan;

        public string SourceName => provider.SourceName;

        /// <summary>目录清单已就绪可复用，或来源本身不需要清单。</summary>
        public bool CanMatchAgain => _files is null || _scan is { IsCompletedSuccessfully: true, Result: not null };

        public async Task<LyricsResult?> MatchAsync(LyricsRequest request, CancellationToken token)
        {
            if (_files is null) return await provider.GetLyricsAsync(request, token).ConfigureAwait(false);
            var scan = await (_scan ??= _files.ScanAsync(token)).WaitAsync(token).ConfigureAwait(false);
            return scan is null ? null : await LocalFileLyricsProvider.MatchAsync(scan, request, token).ConfigureAwait(false);
        }
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
            return await GetFallbackLyricsAsync(plan, request, preferredResult, started, work.Token, trace, cancellationToken).ConfigureAwait(false);
        }
        finally { work.Cancel(); }
    }

    /// <summary>默认规则的备用阶段：备用源并发，与优先阶段共用在线总预算。</summary>
    private async Task<LyricsResult?> GetFallbackLyricsAsync(LyricsRetrievalPlan plan, LyricsRequest request, LyricsResult? preferredResult,
        long started, CancellationToken token, LyricsRequestLog trace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var budget = Remaining(started);
        if (budget <= TimeSpan.Zero)
        {
            // 优先源可能用尽在线总预算：还有备用源时仍给一份单来源预算，否则「有回退」只是名义上的。
            if (plan.Fallbacks.Count == 0) return preferredResult;
            budget = _perSourceBudget;
        }
        // 等的是各自受预算约束的包装任务，不会无限等待底层歌词库。
        var tasks = plan.Fallbacks.Select(provider => TryProviderAsync(provider, request, budget, token, trace)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Select(plan, request, preferredResult, results);
    }

    /// <summary>当前播放器对应来源的结果优先，其余按匹配分选择。</summary>
    private static LyricsResult? Select(LyricsRetrievalPlan plan, LyricsRequest request, LyricsResult? preferredResult,
        IReadOnlyList<LyricsResult?> results)
    {
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

    internal static bool IsAvailable(LyricsResult? result) =>
        result is { Status: LyricsResultStatus.Available, Document.Lines.Count: > 0 };

    private TimeSpan Remaining(long started)
    {
        var remaining = _totalBudget - Stopwatch.GetElapsedTime(started);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < _perSourceBudget ? remaining : _perSourceBudget;
    }

    private Task<LyricsResult?> TryProviderAsync(ILyricsProvider provider, LyricsRequest request, TimeSpan budget, CancellationToken token, LyricsRequestLog trace) =>
        TryProviderAsync(provider.SourceName, cancellation => provider.GetLyricsAsync(request, cancellation), budget, token, trace);

    private async Task<LyricsResult?> TryProviderAsync(string sourceName, Func<CancellationToken, Task<LyricsResult?>> retrieve,
        TimeSpan budget, CancellationToken token, LyricsRequestLog trace)
    {
        if (budget <= TimeSpan.Zero) return null;
        var attempt = trace.Begin(sourceName);
        // 超时取消合作式工作；库内不可取消的请求继续结束，晚到结果不采纳且异常被观察。
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<LyricsResult?>? task = null;
        try
        {
            task = retrieve(source.Token);
            var result = await task.WaitAsync(budget, token).ConfigureAwait(false);
            trace.Record(attempt, sourceName, result?.Status.ToString() ?? "FailedOrUnmatched", result?.MatchScore);
            if (result is not null)
                _failures.Recover(AppLogService.Current, "Lyrics", sourceName, "歌词来源请求已恢复");
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // WaitAsync 使用 TaskCanceledException；统一为调用方令牌的取消出口，保持既有契约。
            trace.Record(attempt, sourceName, "Cancelled");
            token.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception exception)
        {
            trace.Record(attempt, sourceName, exception is TimeoutException ? "Timeout" : $"Error({exception.GetType().Name})");
            _failures.Report(AppLogService.Current, "Lyrics", sourceName, "歌词来源请求失败", exception);
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
