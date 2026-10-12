using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AFMediaBar.Classes.Models.Credits;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 开发人员与赞助名单的读取与缓存服务。先显示内置副本或较新的缓存，按需在后台刷新；
/// 贡献者仍优先使用 GitHub API，失败回退仓库快照。旧线上快照不覆盖较新的内置名单。
/// 服务拥有未注入的 HttpClient、请求取消源和并发门；释放后不再发布状态。
/// </summary>
public sealed class CreditsService : IDisposable
{
    private readonly AppLogService? _log;
    private readonly UpdateService? _updateService;
    private readonly CreditsCacheStore _cache;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly DateTimeOffset? _bundledContributorsUtc;
    private readonly DateTimeOffset? _bundledSponsorsUtc;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private int _activeFetches;
    private int _fetchGateDisposed;
    private volatile bool _disposed;

    /// <summary>当前快照；界面只读它，不自己拼数据。/ The current snapshot; the interface only reads it and never assembles data itself.</summary>
    public CreditsSnapshot Current { get; private set; } = CreditsSnapshot.Empty;

    /// <summary>快照变化时触发（可能在后台线程上）。/ Raised when the snapshot changes; this can be on a background thread.</summary>
    public event Action<CreditsSnapshot>? SnapshotChanged;

    /// <summary>
    /// 创建名单服务。
    /// Creates the credits service.
    /// </summary>
    /// <param name="log">程序日志。/ The application log.</param>
    /// <param name="updateService">更新服务：只为读它当前清单里的加速站点列表，不触发任何更新动作。/ The update service, read only for the accelerator list in its current manifest; no update action is ever triggered from here.</param>
    /// <param name="cache">缓存存储；省略时用默认目录（测试可传临时目录）。/ The cache store, defaulting to the standard directory; tests may pass a temporary one.</param>
    /// <param name="httpClient">注入的客户端由调用方释放；省略时由服务创建并拥有。</param>
    public CreditsService(AppLogService? log, UpdateService? updateService = null, CreditsCacheStore? cache = null, HttpClient? httpClient = null)
    {
        _log = log;
        _updateService = updateService;
        _cache = cache ?? new CreditsCacheStore();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttpClient = httpClient is null;

        var bundledContributors = ReadBundledList("Contributors", "generatedUtc", CreditsJsonParser.ParseContributors);
        var bundledSponsors = ReadBundledList("Sponsors", "updatedUtc", CreditsJsonParser.ParseSponsors);
        _bundledContributorsUtc = bundledContributors.UpdatedUtc;
        _bundledSponsorsUtc = bundledSponsors.UpdatedUtc;
        var cached = _cache.TryRead();
        var contributorsOverride = CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable).Length > 0;
        var sponsorsOverride = CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.SponsorsUrlOverrideVariable).Length > 0;
        var useCachedContributors = !contributorsOverride && cached is { Contributors.Count: > 0 } &&
            !IsOlder(cached.FetchedUtc, _bundledContributorsUtc);
        var useCachedSponsors = !sponsorsOverride && cached is not null &&
            (cached.SponsorsLoaded ?? (cached.Sponsors.Count > 0 || !cached.Partial)) &&
            !IsOlder(cached.FetchedUtc, _bundledSponsorsUtc);
        var contributors = contributorsOverride ? [] : useCachedContributors ? cached!.Contributors : bundledContributors.Result.Contributors ?? [];
        var sponsors = sponsorsOverride ? [] : useCachedSponsors ? cached!.Sponsors : bundledSponsors.Result.Sponsors ?? [];
        var cacheReplaced = cached is not null && (!useCachedContributors || !useCachedSponsors);
        Current = new CreditsSnapshot(
            contributors,
            sponsors,
            FromCache: useCachedContributors || useCachedSponsors,
            FetchedUtc: cacheReplaced ? null : cached?.FetchedUtc,
            FailureReason: cached is { Partial: true } && !cacheReplaced ? Translations.Get("Credits.Status.PartialCached") : null,
            IsLoading: false,
            ContributorsLoaded: contributors.Count > 0,
            SponsorsLoaded: !sponsorsOverride && (useCachedSponsors || bundledSponsors.Result.Sponsors is not null));
    }

    /// <summary>当前生效的加速站点模板：来自更新清单；清单里没写时由策略使用内置默认列表。/ The accelerator templates in effect: taken from the update manifest, while the policy falls back to the built-in defaults when the manifest says nothing.</summary>
    public IReadOnlyList<string>? Accelerators => _updateService?.CurrentState.Manifest?.Accelerators;

    /// <summary>
    /// 取一次名单；缓存有效且不是手动触发时直接返回，不做任何网络请求。
    /// Fetches the lists once, returning immediately with no network access while the cache is fresh and the call was not manual.
    /// </summary>
    /// <param name="isManual">是否由用户手动触发。/ Whether the user triggered this manually.</param>
    public void RequestRefresh(bool isManual)
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // 覆盖变量在场时等同于手动请求：明确指向某个地址的那一次取数不该被缓存挡住（见 CreditsSourcePolicy.HasAnyOverride）。
        // While an override is in place this counts as a manual request: a fetch explicitly pointed at an address must not be held back by the cache, which is what
        // CreditsSourcePolicy.HasAnyOverride explains.
        var bypassCache = isManual || CreditsSourcePolicy.HasAnyOverride;
        if (!CreditsCachePolicy.ShouldFetch(Current.FetchedUtc, now, bypassCache, Current.IsPartial))
        {
            return;
        }

        if (CreditsSourcePolicy.HasAnyOverride)
        {
            _log?.Info(
                "Credits",
                $"使用覆盖地址（提交者快照 contributors snapshot={CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable)}，" +
                $"赞助名单 sponsors={CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.SponsorsUrlOverrideVariable)}）");
        }

        Interlocked.Increment(ref _activeFetches);
        if (_disposed)
        {
            if (Interlocked.Decrement(ref _activeFetches) == 0) DisposeFetchGateIfIdle();
            return;
        }
        var cancellation = new CancellationTokenSource();
        Cancel(Interlocked.Exchange(ref _cancellation, cancellation));

        Publish(Current with { IsLoading = true });

        // 在后台线程执行：这一页可能在窗口刚打开时构造，网络请求绝不能挡在构造路径上。
        // Runs on a background thread: this page can be constructed while a window is opening, and a network request must never sit on that path.
        _ = Task.Run(() => FetchAsync(isManual, cancellation), CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel(Interlocked.Exchange(ref _cancellation, null));
        DisposeFetchGateIfIdle();
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private async Task FetchAsync(bool isManual, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var entered = false;
        try
        {
            await _fetchGate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            token.ThrowIfCancellationRequested();
            var contributors = Current.Contributors;
            var sponsors = Current.Sponsors;
            var failures = new List<string>();
            var contributorsLoaded = Current.ContributorsLoaded;
            var sponsorsLoaded = Current.SponsorsLoaded;
            var fetchedSomething = false;

            var loadedContributors = await LoadContributorsAsync(token).ConfigureAwait(false);
            if (loadedContributors.Contributors is { Count: > 0 })
            {
                contributors = loadedContributors.Contributors;
                contributorsLoaded = true;
                fetchedSomething = true;
            }
            else if (loadedContributors.FailureReason is { } contributorFailure)
            {
                failures.Add(contributorFailure);
            }

            var loadedSponsors = await LoadSponsorsAsync(token).ConfigureAwait(false);
            if (loadedSponsors.Sponsors is not null)
            {
                sponsors = loadedSponsors.Sponsors;
                sponsorsLoaded = true;
                fetchedSomething = true;
            }
            else if (loadedSponsors.FailureReason is { } sponsorFailure)
            {
                failures.Add(sponsorFailure);
            }

            if (!fetchedSomething)
            {
                var reason = failures.Count > 0
                    ? string.Join(" ", failures)
                    : null;
                _log?.Warn("Credits", $"名单获取失败: {reason}");
                Publish(Current with { IsLoading = false, FailureReason = reason }, cancellation);
                return;
            }

            var fetchedUtc = DateTimeOffset.UtcNow;

            // 只取到一部分时也要写缓存（否则已取到的那半份会被丢掉），但要标成"不完整"：
            // 缓存时效据此从 24 小时缩到 1 小时，没取到的那份名单因此不会被一整天的缓存挡住。
            // A partial result is cached as well — otherwise the half that did arrive would be thrown away — but it is marked incomplete, which shortens the cache
            // from 24 hours to one; the list that failed therefore never gets stuck behind a whole day of cache.
            var isPartial = failures.Count > 0;
            if (!CanPublish(cancellation)) return;
            _cache.TryWrite(contributors, sponsors, fetchedUtc, isPartial, sponsorsLoaded);
            Publish(new CreditsSnapshot(
                contributors,
                sponsors,
                FromCache: false,
                fetchedUtc,
                FailureReason: failures.Count > 0 ? string.Join(" ", failures) : null,
                IsLoading: false,
                ContributorsLoaded: contributorsLoaded,
                SponsorsLoaded: sponsorsLoaded), cancellation);

            _log?.Info(
                "Credits",
                $"名单已更新（{contributors.Count} 位贡献者 contributors，{sponsors.Count} 位赞助者 sponsors，" +
                $"不完整 partial={isPartial}，手动 manual={isManual}）");
        }
        catch (OperationCanceledException)
        {
            // 替代请求负责结束加载，已取消的请求不再发布状态。
        }
        catch (Exception exception)
        {
            // 关于页的名单只是署名，任何失败都不该让页面出错，记一行即可。
            // These lists are attribution only, so no failure here should break the page; one log line is enough.
            _log?.Warn("Credits", $"名单获取异常: {exception.Message}");
            Publish(Current with { IsLoading = false, FailureReason = exception.Message }, cancellation);
        }
        finally
        {
            if (entered) _fetchGate.Release();
            Interlocked.CompareExchange(ref _cancellation, null, cancellation);
            cancellation.Dispose();
            if (Interlocked.Decrement(ref _activeFetches) == 0) DisposeFetchGateIfIdle();
        }
    }

    private async Task<CreditsListParseResult> LoadContributorsAsync(CancellationToken cancellationToken)
    {
        string? firstFailure = null;

        foreach (var endpoint in CreditsSourcePolicy.BuildContributorsApiPlan())
        {
            var text = await ReadEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                continue;
            }

            var parsed = CreditsJsonParser.ParseContributors(text);
            if (parsed.Succeeded)
            {
                _log?.Info("Credits", $"贡献者取自接口（{parsed.Contributors!.Count}）");
                return parsed;
            }

            firstFailure ??= parsed.FailureReason;
        }

        // 旧线上快照不能把刚随程序更新的离线名单降回去；显式覆盖仍按指定地址读取。
        foreach (var endpoint in CreditsSourcePolicy.BuildContributorsSnapshotPlan(Accelerators))
        {
            var text = await ReadEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                continue;
            }

            var parsed = CreditsJsonParser.ParseContributors(text);
            if (parsed.Succeeded)
            {
                if (CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable).Length == 0 &&
                    IsOlder(ReadSourceTimestamp(text, "generatedUtc"), _bundledContributorsUtc))
                {
                    continue;
                }
                _log?.Info("Credits", $"贡献者取自仓库快照（{parsed.Contributors!.Count}）");
                return parsed;
            }

            firstFailure ??= parsed.FailureReason;
        }

        return CreditsListParseResult.Failure(firstFailure ?? Translations.Get("Credits.Reason.Unavailable"));
    }

    private async Task<CreditsListParseResult> LoadSponsorsAsync(CancellationToken cancellationToken)
    {
        string? firstFailure = null;
        foreach (var endpoint in CreditsSourcePolicy.BuildSponsorsPlan(Accelerators))
        {
            var text = await ReadEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                continue;
            }

            var parsed = CreditsJsonParser.ParseSponsors(text);
            if (parsed.Succeeded)
            {
                if (CreditsSourcePolicy.ResolveOverride(CreditsSourcePolicy.SponsorsUrlOverrideVariable).Length == 0 &&
                    IsOlder(ReadSourceTimestamp(text, "updatedUtc"), _bundledSponsorsUtc))
                {
                    continue;
                }
                return parsed;
            }

            firstFailure ??= parsed.FailureReason;
        }

        return CreditsListParseResult.Failure(firstFailure ?? Translations.Get("Credits.Reason.Unavailable"));
    }

    private async Task<string?> ReadEndpointAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            // 与更新清单一致：验收时可以指向本地文件，从而在没有网络的情况下走完整条链路。
            // Matching the update manifest: acceptance runs may point this at a local file and exercise the whole path without a network.
            if (File.Exists(endpoint))
            {
                return await File.ReadAllTextAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("AFMediaBar-Credits");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[Credits] {endpoint} returned {(int)response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Credits] {endpoint} failed: {exception.Message}");
            return null;
        }
    }

    private static (CreditsListParseResult Result, DateTimeOffset? UpdatedUtc) ReadBundledList(
        string name, string timestampProperty, Func<string?, CreditsListParseResult> parse)
    {
        using var stream = typeof(CreditsService).Assembly.GetManifestResourceStream($"AFMediaBar.Resources.{name}.json");
        if (stream is null) return (CreditsListParseResult.Failure(Translations.Get("Credits.Reason.Empty")), null);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var result = parse(text);
        return (result, result.Succeeded ? ReadSourceTimestamp(text, timestampProperty) : null);
    }

    private static DateTimeOffset? ReadSourceTimestamp(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty(property, out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(timestamp.GetString(), out var parsed) ? parsed : null;
    }

    private static bool IsOlder(DateTimeOffset? timestamp, DateTimeOffset? bundledTimestamp) =>
        timestamp is { } source && bundledTimestamp is { } bundled && source < bundled;

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void DisposeFetchGateIfIdle()
    {
        if (_disposed && Volatile.Read(ref _activeFetches) == 0 && Interlocked.Exchange(ref _fetchGateDisposed, 1) == 0)
            _fetchGate.Dispose();
    }

    private bool CanPublish(CancellationTokenSource cancellation) =>
        !_disposed && ReferenceEquals(_cancellation, cancellation) && !cancellation.IsCancellationRequested;

    private void Publish(CreditsSnapshot snapshot, CancellationTokenSource? cancellation = null)
    {
        if (_disposed || cancellation is not null && !CanPublish(cancellation)) return;
        Current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    /// <summary>解析一份名单文本；仅供测试与诊断使用。/ Parses one list text; for tests and diagnostics only.</summary>
    /// <param name="json">JSON 文本。/ JSON text.</param>
    /// <returns>解析结果。/ The parse result.</returns>
    internal static CreditsListParseResult ParseForDiagnostics(string json)
    {
        using var document = JsonDocument.Parse(json);
        _ = document;
        return document.RootElement.TryGetProperty("sponsors", out _)
            ? CreditsJsonParser.ParseSponsors(json)
            : CreditsJsonParser.ParseContributors(json);
    }
}
