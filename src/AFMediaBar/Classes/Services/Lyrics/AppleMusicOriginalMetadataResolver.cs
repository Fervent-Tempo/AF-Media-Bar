// 向 Apple 曲库（itunes.apple.com）查询 Apple Music 译名对应的原名；拥有 HTTP 客户端、内存缓存、403/429 冷却与客户端限流。
// 由组合根以单例创建并在退出时释放：释放取消在途请求、释放自建的 HttpClient，之后不再发出请求。
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>把 Apple Music 的译名还原为原名，供取词第二轮使用；任何失败都返回 null，不写缓存。</summary>
public sealed class AppleMusicOriginalMetadataResolver : IDisposable
{
    private const int CacheCapacity = 128;
    private const int RequestLimit = 18;
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly TimeSpan _noOriginalLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan _blockedCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _requestWindow = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly AppLogService? _log;
    private readonly TimeProvider _timeProvider;
    private readonly LruCache<(string Title, string Artist), Resolution> _cache;
    private readonly object _gate = new();
    private readonly Queue<long> _requestTimes = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private long? _blockedAt;
    private volatile bool _disposed;

    /// <summary>创建应用持有的解析器；传入的 HttpClient 由调用方负责释放。</summary>
    public AppleMusicOriginalMetadataResolver(AppLogService? log = null, HttpClient? client = null)
        : this(log, client, TimeProvider.System) { }

    internal AppleMusicOriginalMetadataResolver(AppLogService? log, HttpClient? client, TimeProvider timeProvider)
    {
        _log = log;
        _ownsClient = client is null;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _timeProvider = timeProvider;
        _cache = new LruCache<(string Title, string Artist), Resolution>(CacheCapacity, null, 0, timeProvider);
        _lifetimeToken = _lifetime.Token;
    }

    /// <summary>
    /// 解析一次原名。找到原名的结果在本次运行内缓存，确认无原名的缓存 6 小时；时长对不上时的未命中、冷却、限流、
    /// 网络或解析失败返回 null 且不缓存。
    /// 请求直接使用传入的令牌，调用方取消时向上抛出。
    /// </summary>
    internal async Task<AppleMusicOriginal?> ResolveAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        if (_disposed || string.IsNullOrWhiteSpace(request.Title)) return null;
        var key = (request.Title, request.Artist);
        if (_cache.TryGetValue(key, out var cached) && cached is not null)
        {
            Log(request, cached.Original is null ? "Cached(None)" : "Cached(Found)", cached.Original, null);
            return cached.Original;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            var (outcome, original) = await LookUpAsync(AppleMusicCatalogPolicy.ForCatalog(request), linked.Token).ConfigureAwait(false);
            if (outcome is Outcome.Found or Outcome.None)
                _cache.Set(key, new Resolution(original), original is null ? _noOriginalLifetime : null);
            Log(request, outcome.ToString(), original, started);
            return original;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log(request, "Cancelled", null, started);
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException or
                                              IOException or FormatException or ObjectDisposedException)
        {
            Log(request, $"Error({exception.GetType().Name})", null, started);
            return null;
        }
    }

    /// <summary>
    /// JP 区搜索（无候选时补搜 TW 区；组合搜索词两区都零结果时改用曲名），US/CN 区 lookup 校验，必要时补查 KR/TW/HK 后按文字选原名。
    /// </summary>
    private async Task<(Outcome Outcome, AppleMusicOriginal? Original)> LookUpAsync(LyricsRequest request, CancellationToken token)
    {
        IReadOnlyList<AppleMusicTrack> anchors = [];
        var searched = new List<AppleMusicTrack>();
        var terms = AppleMusicCatalogPolicy.SearchTerms(request);
        for (var index = 0; index < terms.Count && searched.Count == 0; index++)
        {
            var term = terms[index];
            // 首个搜索词按 JP → TW 顺序，JP 区有时长对得上的候选就不再搜 TW；回退搜索词两区并发，避免超出解析预算。
            var batches = index == 0
                ? AppleMusicCatalogPolicy.SearchRegions.Select(region => new[] { region }).ToArray()
                : new[] { AppleMusicCatalogPolicy.SearchRegions };
            foreach (var regions in batches)
            {
                var (found, blocked) = await RequestAsync(regions,
                    region => AppleMusicCatalogPolicy.SearchUri(term, region), token).ConfigureAwait(false);
                if (found is null) return (blocked, null);
                foreach (var region in regions)
                {
                    searched.AddRange(found[region]);
                    if (anchors.Count == 0) anchors = AppleMusicCatalogPolicy.FilterByDuration(found[region], request.DurationSeconds);
                }
                if (anchors.Count > 0) break;
            }
        }
        if (searched.Count == 0) return (Outcome.None, null);

        // 时长对得上的候选排在前面；lookup 与搜索一样最多带 25 个 id。
        var ids = anchors.Concat(searched).Select(track => track.TrackId).Distinct().Take(AppleMusicCatalogPolicy.ResultLimit).ToArray();
        var (lookups, lookupBlocked) = await RequestAsync(AppleMusicCatalogPolicy.LookupRegions,
            region => AppleMusicCatalogPolicy.LookupUri(ids, region), token).ConfigureAwait(false);
        if (lookups is null) return (lookupBlocked, null);
        var anchor = AppleMusicCatalogPolicy.SelectCandidate(request, anchors, lookups);
        // 切歌瞬间 SMTC 常沿用上一首的时长，可能筛掉正确曲目、只留下时长碰巧相同的无关曲目。时长对得上的候选都不通过时，
        // 对全部搜索结果按「没有时长」处理，曲名与歌手都一致才采用；这样仍未命中可能只是时长不可信，不写缓存。
        var durationDropped = anchor is null && request.DurationSeconds is > 0;
        if (durationDropped)
        {
            request = request with { DurationSeconds = null };
            anchor = AppleMusicCatalogPolicy.SelectCandidate(request, searched, lookups);
        }
        if (anchor is null) return (durationDropped ? Outcome.UnmatchedWithoutDuration : Outcome.None, null);

        var names = AppleMusicCatalogPolicy.NamesOf(anchor, lookups);
        var extra = AppleMusicCatalogPolicy.ExtraRegions(request, anchor, names);
        if (extra.Count > 0)
        {
            var (more, extraBlocked) = await RequestAsync(extra,
                region => AppleMusicCatalogPolicy.LookupUri([anchor.TrackId], region), token).ConfigureAwait(false);
            if (more is null) return (extraBlocked, null);
            foreach (var (region, tracks) in more)
            {
                if (tracks.FirstOrDefault(track => track.TrackId == anchor.TrackId) is { } match) names[region] = match;
            }
        }

        var original = AppleMusicCatalogPolicy.SelectOriginal(request, anchor, names);
        return (original is null ? Outcome.None : Outcome.Found, original);
    }

    /// <summary>按地区并发请求；冷却期内或超出限流时一个都不发，返回阻断原因。</summary>
    private async Task<(IReadOnlyDictionary<string, IReadOnlyList<AppleMusicTrack>>? Tracks, Outcome Blocked)> RequestAsync(
        IReadOnlyList<string> regions, Func<string, Uri> uriOf, CancellationToken token)
    {
        if (TryReserve(regions.Count) is { } blocked) return (null, blocked);
        var responses = await Task.WhenAll(regions.Select(region => GetTracksAsync(uriOf(region), region, token))).ConfigureAwait(false);
        if (responses.Any(tracks => tracks is null)) return (null, Outcome.Blocked);
        var byRegion = new Dictionary<string, IReadOnlyList<AppleMusicTrack>>(StringComparer.Ordinal);
        for (var index = 0; index < regions.Count; index++) byRegion[regions[index]] = responses[index]!;
        return (byRegion, Outcome.Found);
    }

    /// <summary>403/429 返回 null 并开始冷却；其它失败状态抛出 HttpRequestException。</summary>
    private async Task<IReadOnlyList<AppleMusicTrack>?> GetTracksAsync(Uri uri, string region, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.UserAgent.ParseAdd("AFMediaBar");
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            lock (_gate) _blockedAt = _timeProvider.GetTimestamp();
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return ParseTracks(await ReadBoundedAsync(stream, token).ConfigureAwait(false), region);
    }

    /// <summary>冷却与滑动窗口限流在同一把锁内判定，一次占用整批请求的名额；返回 null 表示可以发送。</summary>
    private Outcome? TryReserve(int count)
    {
        lock (_gate)
        {
            if (_blockedAt is { } blockedAt && _timeProvider.GetElapsedTime(blockedAt) < _blockedCooldown) return Outcome.CoolingDown;
            while (_requestTimes.Count > 0 && _timeProvider.GetElapsedTime(_requestTimes.Peek()) >= _requestWindow)
                _requestTimes.Dequeue();
            if (_requestTimes.Count + count > RequestLimit) return Outcome.RateLimited;
            var now = _timeProvider.GetTimestamp();
            for (var index = 0; index < count; index++) _requestTimes.Enqueue(now);
            return null;
        }
    }

    private static IReadOnlyList<AppleMusicTrack> ParseTracks(string json, string region)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new FormatException("Unexpected catalogue response.");
        var tracks = new List<AppleMusicTrack>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "kind") != "song" ||
                !item.TryGetProperty("trackId", out var id) || !id.TryGetInt64(out var trackId)) continue;
            var name = Text(item, "trackName");
            if (name.Length == 0) continue;
            double? duration = item.TryGetProperty("trackTimeMillis", out var millis) && millis.TryGetDouble(out var value) && value > 0
                ? value / 1000 : null;
            tracks.Add(new AppleMusicTrack(region, trackId, name, Text(item, "artistName"), Text(item, "collectionName"),
                duration, Text(item, "primaryGenreName")));
        }
        return tracks;
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaximumResponseBytes) throw new FormatException("Catalogue response exceeds size limit.");
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private void Log(LyricsRequest request, string outcome, AppleMusicOriginal? original, long? started)
    {
        var found = original is null ? string.Empty
            : $" original=\"{original.Title}\" originalArtist=\"{original.Artist}\" region={original.Region}";
        var elapsed = started is { } value ? $" elapsed={Stopwatch.GetElapsedTime(value).TotalMilliseconds:0}ms" : string.Empty;
        _log?.Info("Lyrics", $"Apple Music 原名解析: title=\"{request.Title}\" artist=\"{request.Artist}\" result={outcome}{found}{elapsed}");
    }

    /// <summary>取消在途请求并释放自建的 HttpClient；可重复调用。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_ownsClient) _client.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>缓存值：null 原名表示已确认没有可用原名。</summary>
    private sealed record Resolution(AppleMusicOriginal? Original);

    /// <summary>一次解析的结论；只有 Found 与 None 写缓存，其余是暂时结论或阻断。</summary>
    private enum Outcome
    {
        Found,
        None,
        /// <summary>时长对得上的候选都不通过、按严格规则也没有候选；可能只是 SMTC 时长滞后，不缓存。</summary>
        UnmatchedWithoutDuration,
        CoolingDown,
        RateLimited,
        Blocked
    }
}
