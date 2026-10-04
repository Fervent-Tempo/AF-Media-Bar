// Owns catalogue HTTP requests and the atomic disk cache; no installation or update-state mutations.
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>Loads an offline-first, separately published release catalogue.</summary>
public sealed class ReleaseHighlightsService : IDisposable
{
    /// <summary>Replaces all remote endpoints with a test file or HTTPS endpoint.</summary>
    public const string OverrideVariable = "AFMEDIABAR_RELEASE_HIGHLIGHTS_URL";
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _cachePath;
    private readonly AppLogService? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private ReleaseHighlightsResult? _current;
    private bool _disposed;

    /// <summary>Creates the application-owned HTTP reader; injected clients remain caller-owned.</summary>
    public ReleaseHighlightsService(AppLogService? log = null, HttpClient? client = null, string? cachePath = null)
    {
        _log = log;
        _ownsClient = client is null;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AFMediaBar", "cache", "release-highlights.json");
        _lifetimeToken = _lifetime.Token;
    }

    /// <summary>Returns the embedded snapshot or last usable disk cache without contacting any server.</summary>
    public async Task<ReleaseHighlightsResult> ReadInitialAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { return await InitializeAsync(linked.Token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>Refreshes on demand, keeping valid content when all sources fail.</summary>
    public async Task<ReleaseHighlightsResult> LoadAsync(bool force, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        var token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var initial = await InitializeAsync(token).ConfigureAwait(false);
            var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!force && string.IsNullOrWhiteSpace(overridden) && initial.FetchedAt is { } fetched &&
                DateTimeOffset.UtcNow - fetched is { TotalHours: >= 0 and < 24 })
                return initial;
            var endpoints = string.IsNullOrWhiteSpace(overridden) ? new[]
            {
                "https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/release-metadata/release/highlights.json",
                "https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@release-metadata/release/highlights.json"
            } : new[] { overridden.Trim() };
            foreach (var endpoint in endpoints)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var readToken = timeout.Token;
                    string json;
                    if (File.Exists(endpoint))
                    {
                        await using var file = File.OpenRead(endpoint);
                        json = await ReadBoundedAsync(file, readToken).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                            !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                            throw new FormatException("Invalid highlights source.");
                        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.UserAgent.ParseAdd("AFMediaBar-release-highlights");
                        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, readToken).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var stream = await response.Content.ReadAsStreamAsync(readToken).ConfigureAwait(false);
                        json = await ReadBoundedAsync(stream, readToken).ConfigureAwait(false);
                    }
                    var releases = ReleaseHighlightsPolicy.Parse(json);
                    token.ThrowIfCancellationRequested();
                    var now = DateTimeOffset.UtcNow;
                    var cacheFailure = false;
                    try { await WriteCacheAsync(json, now, token).ConfigureAwait(false); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        cacheFailure = true;
                        _log?.Warn("ReleaseHighlights", "Could not persist catalogue cache.", e);
                    }
                    token.ThrowIfCancellationRequested();
                    return _current = new(releases, false, now, cacheFailure ? "ReleaseHighlights.Status.CacheWriteFailed" : null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or JsonException or FormatException)
                { _log?.Warn("ReleaseHighlights", "Catalogue source failed; retaining usable release content.", e); }
            }
            token.ThrowIfCancellationRequested();
            return _current = initial with { IsCached = true, FailureKey = "ReleaseHighlights.Status.RefreshFailed" };
        }
        finally { _gate.Release(); }
    }

    private async Task<ReleaseHighlightsResult> InitializeAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_current is not null) return _current;
        try
        {
            if (File.Exists(_cachePath))
            {
                await using var file = File.OpenRead(_cachePath);
                using var cache = JsonDocument.Parse(await ReadBoundedAsync(file, token, ReleaseHighlightsPolicy.MaximumBytes + 65536).ConfigureAwait(false));
                var content = cache.RootElement.GetProperty("catalogue").GetRawText();
                var fetched = cache.RootElement.GetProperty("fetchedAt").GetDateTimeOffset();
                return _current = new(ReleaseHighlightsPolicy.Parse(content), true, fetched, null);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { _log?.Warn("ReleaseHighlights", "Invalid cache; using bundled catalogue.", e); }
        await using var bundled = typeof(ReleaseHighlightsService).Assembly.GetManifestResourceStream("AFMediaBar.Resources.ReleaseHighlights.json")
            ?? throw new InvalidOperationException("Missing embedded release highlights.");
        return _current = new(ReleaseHighlightsPolicy.Parse(await ReadBoundedAsync(bundled, token).ConfigureAwait(false)), true, null, null);
    }

    private async Task WriteCacheAsync(string json, DateTimeOffset now, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var content = JsonDocument.Parse(json);
            var cache = JsonSerializer.Serialize(new { fetchedAt = now, catalogue = content.RootElement },
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            await File.WriteAllTextAsync(temporary, cache, new UTF8Encoding(false), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, _cachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token, int maximumBytes = ReleaseHighlightsPolicy.MaximumBytes)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > maximumBytes) throw new FormatException("Catalogue exceeds size limit.");
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)).TrimStart('\uFEFF');
    }

    /// <summary>Cancels outstanding reads and releases the HTTP client owned by this service.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_ownsClient) _client.Dispose();
        _lifetime.Dispose();
    }
}
