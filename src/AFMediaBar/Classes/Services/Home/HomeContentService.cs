// 应用拥有首页请求、限流和磁盘缓存；页面只取消自己的等待，不持有网络资源。
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models.Home;

namespace AFMediaBar.Classes.Services.Home;

/// <summary>读取公开首页资料；没有统计上传或安装行为。</summary>
public sealed class HomeContentService : IDisposable
{
    private const int MaximumJsonBytes = 524288;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _directory;
    private readonly AppLogService _log;
    private readonly SemaphoreSlim _projectGate = new(1, 1), _catalogueGate = new(1, 1), _imageGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Json, string? Etag)> _responses = new();
    private DateTimeOffset _projectAttempt, _catalogueAttempt, _blockedUntil;
    private bool _disposed;
    private HomeCatalogueResult? _catalogue;
    private ProjectActivitySnapshot? _project;
    public static ProjectActivitySnapshot EmptyProject => new(null, null, null, null, [], [], null);
    public HomeContentService(AppLogService log, HttpClient? client = null, string? directory = null)
    {
        _log = log;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        _ownsClient = client is null;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar", "cache", "home");
        _lifetimeToken = _lifetime.Token;
    }

    public async Task<ProjectActivitySnapshot> ReadProjectAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
        await _projectGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { return await ReadProjectCoreAsync(linked.Token).ConfigureAwait(false); }
        finally { _projectGate.Release(); }
    }
    private async Task<ProjectActivitySnapshot> ReadProjectCoreAsync(CancellationToken token)
    {
        if (_project is not null) return _project;
        var cached = await ReadCacheAsync<ProjectActivitySnapshot>("project.json", token).ConfigureAwait(false);
        if (cached is not null && cached.Issues is { Count: <= 3 } && cached.PullRequests is { Count: <= 3 } &&
            new[] { cached.Stars, cached.OpenIssues, cached.RecentIssues, cached.OpenPullRequests }.All(count => count is null or >= 0) &&
            cached.Issues.Concat(cached.PullRequests).All(item => item is not null && item.Number > 0 && HomeContentPolicy.IsOfficialLink(item.Url)))
            return _project = cached with { IsCached = true };
        return _project = EmptyProject;
    }

    public async Task<ProjectActivitySnapshot> RefreshProjectAsync(bool force, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        var token = linked.Token;
        await _projectGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var previous = await ReadProjectCoreAsync(token).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            if (now < _blockedUntil || now - _projectAttempt < TimeSpan.FromMinutes(5) ||
                !force && previous.FetchedAt is { } fetched && now - fetched is { TotalHours: >= 0 and < 1 }) return previous;
            _projectAttempt = now;
            var repo = "https://api.github.com/repos/" + HomeContentPolicy.Repository;
            var search = "https://api.github.com/search/issues?per_page=3&sort=updated&order=desc&q=";
            var queries = new[] { repo,
                search + Uri.EscapeDataString($"repo:{HomeContentPolicy.Repository} is:issue is:open"),
                search + Uri.EscapeDataString($"repo:{HomeContentPolicy.Repository} is:issue created:>={now.AddDays(-7):yyyy-MM-ddTHH:mm:ssZ}"),
                search + Uri.EscapeDataString($"repo:{HomeContentPolicy.Repository} is:pr is:open") };
            var roots = new JsonDocument?[4];
            try
            {
                for (var index = 0; index < queries.Length; index++)
                {
                    if (DateTimeOffset.UtcNow < _blockedUntil) break;
                    try { roots[index] = JsonDocument.Parse(await ReadRemoteAsync(new Uri(queries[index]), MaximumJsonBytes, token).ConfigureAwait(false)); }
                    catch (Exception exception) when (IsReadFailure(exception, token)) { _log.Warn("Home", "公开项目资料暂不可用", exception); }
                }
                token.ThrowIfCancellationRequested();
                // 部分失败保留整个旧快照，避免把旧计数标成刚刚更新。
                if (roots.Any(root => root is null) && previous.FetchedAt is not null) return previous with { IsCached = true };
                _project = new(roots[0] is { } a ? HomeContentPolicy.Count(a.RootElement, "stargazers_count") : null,
                    roots[1] is { } b ? HomeContentPolicy.Count(b.RootElement, "total_count") : null,
                    roots[2] is { } c ? HomeContentPolicy.Count(c.RootElement, "total_count") : null,
                    roots[3] is { } d ? HomeContentPolicy.Count(d.RootElement, "total_count") : null,
                    roots[1] is { } issues ? HomeContentPolicy.Threads(issues.RootElement, false) : [],
                    roots[3] is { } prs ? HomeContentPolicy.Threads(prs.RootElement, true) : [],
                    roots.All(root => root is not null) ? now : null, roots.Any(root => root is null));
                if (_project.FetchedAt is not null) await WriteCacheAsync("project.json", _project, token).ConfigureAwait(false);
                return _project;
            }
            finally { foreach (var root in roots) root?.Dispose(); }
        }
        finally { _projectGate.Release(); }
    }

    public async Task<HomeCatalogueResult> ReadCatalogueAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
        await _catalogueGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { return await ReadCatalogueCoreAsync(linked.Token).ConfigureAwait(false); }
        finally { _catalogueGate.Release(); }
    }
    private async Task<HomeCatalogueResult> ReadCatalogueCoreAsync(CancellationToken token)
    {
        if (_catalogue is not null) return _catalogue;
        var cached = await ReadCacheAsync<HomeCatalogueResult>("catalogue.json", token).ConfigureAwait(false);
        if (cached is not null)
        {
            try { HomeContentPolicy.ParseCatalogue(JsonSerializer.Serialize(cached.Catalogue, HomeContentPolicy.JsonOptions)); return _catalogue = cached with { IsCached = true }; }
            catch (FormatException) { }
        }
        return _catalogue = new(new(1, []), null, true);
    }
    public async Task<HomeCatalogueResult> RefreshCatalogueAsync(bool force, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        var token = linked.Token;
        await _catalogueGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var previous = await ReadCatalogueCoreAsync(token).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            if (now - _catalogueAttempt < TimeSpan.FromMinutes(5) || !force && previous.FetchedAt is { } time && now - time is { TotalHours: >= 0 and < 6 }) return previous;
            _catalogueAttempt = now;
            foreach (var url in new[] { "https://raw.githubusercontent.com/" + HomeContentPolicy.Repository + "/release-metadata/home/catalogue.json",
                "https://cdn.jsdelivr.net/gh/" + HomeContentPolicy.Repository + "@release-metadata/home/catalogue.json" })
            {
                try
                {
                    var catalogue = HomeContentPolicy.ParseCatalogue(await ReadRemoteAsync(new Uri(url), MaximumJsonBytes, token).ConfigureAwait(false));
                    token.ThrowIfCancellationRequested();
                    _catalogue = new(catalogue, now, false);
                    await WriteCacheAsync("catalogue.json", _catalogue, token).ConfigureAwait(false);
                    return _catalogue;
                }
                catch (Exception exception) when (IsReadFailure(exception, token)) { _log.Warn("Home", "首页目录暂不可用，保留本地内容", exception); }
            }
            return previous;
        }
        finally { _catalogueGate.Release(); }
    }

    public async Task<BitmapSource?> ReadImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (!HomeContentPolicy.IsOfficialImage(url)) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        var token = linked.Token;
        await _imageGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var file = Path.Combine(_directory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url!))) + ".image");
            byte[] bytes;
            if (File.Exists(file) && new FileInfo(file).Length <= 2097152) bytes = await File.ReadAllBytesAsync(file, token).ConfigureAwait(false);
            else
            {
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                bytes = await ReadBytesAsync(response, 2097152, token).ConfigureAwait(false);
            }
            // 先核对原始像素尺寸，再以展示所需分辨率解码；冻结后才能跨线程呈现。
            var image = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames[0].PixelWidth > 4096 || decoder.Frames[0].PixelHeight > 4096) throw new FormatException("Home image dimensions exceed limit.");
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1600;
                bitmap.StreamSource = new MemoryStream(bytes); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }, token).ConfigureAwait(false);
            Directory.CreateDirectory(_directory);
            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, bytes, token).ConfigureAwait(false);
            foreach (var obsolete in new DirectoryInfo(_directory).GetFiles("*.image").OrderByDescending(info => info.LastWriteTimeUtc).Skip(10)) obsolete.Delete();
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch (Exception exception) when (IsReadFailure(exception, token) || exception is NotSupportedException or ArgumentException) { _log.Warn("Home", "首页图片不可用", exception); return null; }
        finally { _imageGate.Release(); }
    }

    private static bool IsReadFailure(Exception exception, CancellationToken token) => !token.IsCancellationRequested &&
        exception is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or FormatException or OperationCanceledException or InvalidOperationException;
    private async Task<string> ReadRemoteAsync(Uri uri, int maximum, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("AFMediaBar-home/1.0");
        request.Headers.Accept.ParseAdd("application/json");
        if (_responses.TryGetValue(uri.AbsoluteUri, out var old) && old.Etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", old.Etag);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && old.Json is not null) return old.Json;
        if (uri.Host == "api.github.com" && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5);
            _blockedUntil = DateTimeOffset.UtcNow + retry;
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var seconds))
                _blockedUntil = DateTimeOffset.FromUnixTimeSeconds(seconds) > _blockedUntil ? DateTimeOffset.FromUnixTimeSeconds(seconds) : _blockedUntil;
        }
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBytesAsync(response, maximum, token).ConfigureAwait(false);
        var json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        if (_responses.Count >= 16) foreach (var key in _responses.Keys.Take(8)) _responses.TryRemove(key, out _);
        _responses[uri.AbsoluteUri] = (json, response.Headers.ETag?.ToString());
        return json;
    }
    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new FormatException("Home response exceeds size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var target = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (target.Length + read > maximum) throw new FormatException("Home response exceeds size limit.");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }
    private async Task<T?> ReadCacheAsync<T>(string name, CancellationToken token) where T : class
    {
        try
        {
            var file = Path.Combine(_directory, name);
            if (!File.Exists(file) || new FileInfo(file).Length > MaximumJsonBytes) return null;
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(file, token).ConfigureAwait(false), HomeContentPolicy.JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { _log.Warn("Home", "首页缓存不可用", exception); return null; }
    }
    private async Task WriteCacheAsync<T>(string name, T value, CancellationToken token)
    {
        var temporary = Path.Combine(_directory, name + ".tmp");
        try
        {
            Directory.CreateDirectory(_directory);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, HomeContentPolicy.JsonOptions), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(_directory, name), true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _log.Warn("Home", "首页缓存写入失败", exception); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); if (_ownsClient) _client.Dispose(); _lifetime.Dispose(); }
}
