// 应用拥有下载任务、HTTP 客户端和临时文件；页面只订阅快照，Host 停止时取消并等待任务。
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using AFMediaBar.Classes.Models.Plugins;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services.Plugins;

/// <summary>下载固定附件并验证大小和 SHA-256，不执行或安装下载的文件。</summary>
public sealed class PluginDownloadService : IHostedService, IDisposable
{
    private const long MaximumDownloadBytes = 100L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _root;
    private readonly AppLogService? _log;
    private readonly Func<PluginAsset, IReadOnlyList<string>> _sourcePlan;
    private readonly Dictionary<string, PluginDownloadState> _states = new(StringComparer.Ordinal);
    private CancellationTokenSource? _operation;
    private Task _task = Task.CompletedTask;
    private bool _stopping;
    private bool _disposed;

    /// <summary>可注入独立目录和网络客户端；注入客户端由调用方释放。</summary>
    public PluginDownloadService(AppLogService? log = null, string? directoryPath = null, HttpClient? client = null,
        Func<PluginAsset, IReadOnlyList<string>>? sourcePlan = null)
    {
        _log = log;
        _root = Path.GetFullPath(directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar", "PluginDownloads"));
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _sourcePlan = sourcePlan ?? (asset => GitHubAcceleratorPolicy.Expand(asset.Url, null).Append(asset.Url).ToArray());
    }

    /// <summary>通知可能来自后台线程；订阅方负责切回 UI 并检查自身生命周期。</summary>
    public event Action? StateChanged;

    public bool IsBusy { get { lock (_gate) return _operation is not null; } }

    public PluginDownloadState GetState(PluginDescriptor plugin, PluginAsset asset)
    {
        lock (_gate) return _states.GetValueOrDefault(Key(plugin, asset)) ?? new();
    }

    /// <summary>原子占用全局下载槽位；重复请求或停止后返回 false，不启动后台任务。</summary>
    public bool TryStartDownload(PluginDescriptor plugin, PluginAsset asset)
    {
        var target = GetDestination(plugin, asset);
        lock (_gate)
        {
            if (_disposed || _stopping || _operation is not null) return false;
            var operation = new CancellationTokenSource();
            _operation = operation;
            _states[Key(plugin, asset)] = new(PluginDownloadPhase.Verifying);
            _task = Task.Run(() => RunAsync(plugin, asset, target, operation));
        }
        Notify();
        return true;
    }

    public void Cancel()
    {
        CancellationTokenSource? operation;
        lock (_gate) operation = _operation;
        TryCancel(operation);
    }

    /// <summary>等待当前一轮，既不取消，也不阻塞 UI 线程。</summary>
    public Task WhenIdleAsync() { lock (_gate) return _task; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? operation;
        Task task;
        lock (_gate) { _stopping = true; operation = _operation; task = _task; }
        TryCancel(operation);
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(PluginDescriptor plugin, PluginAsset asset, string target, CancellationTokenSource operation)
    {
        var token = operation.Token;
        var key = Key(plugin, asset);
        try
        {
            EnsureOwnedDirectory(Path.GetDirectoryName(target)!);
            RejectReparsePoint(target);
            if (File.Exists(target) && await VerifyAsync(target, asset, token).ConfigureAwait(false))
            {
                Publish(key, new(PluginDownloadPhase.Completed, 100, FilePath: target), operation);
                return;
            }

            var failureKey = "Plugins.Error.Network";
            foreach (var url in _sourcePlan(asset))
            {
                token.ThrowIfCancellationRequested();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    Publish(key, new(PluginDownloadPhase.Downloading, SourceHost: uri.Host), operation);
                    await DownloadAttemptAsync(url, temporary, asset, progress =>
                        Publish(key, new(PluginDownloadPhase.Downloading, progress, uri.Host), operation), token).ConfigureAwait(false);
                    Publish(key, new(PluginDownloadPhase.Verifying, 100, uri.Host), operation);
                    token.ThrowIfCancellationRequested();
                    EnsureOwnedDirectory(Path.GetDirectoryName(target)!);
                    RejectReparsePoint(target);
                    File.Move(temporary, target, true);
                    Publish(key, new(PluginDownloadPhase.Completed, 100, uri.Host, target), operation);
                    _log?.Info("Plugins", $"插件下载完成: id={plugin.Id} version={plugin.Version} host={uri.Host}");
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    failureKey = exception is InvalidDataException ? "Plugins.Error.Integrity" : "Plugins.Error.Network";
                    _log?.Warn("Plugins", $"插件下载换源: id={plugin.Id} host={uri.Host} error={exception.GetType().Name}");
                }
                finally { RemoveTemporary(temporary); }
            }
            Publish(key, new(PluginDownloadPhase.Failed, FailureKey: failureKey), operation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (!_disposed && !_stopping && ReferenceEquals(_operation, operation))
                    _states[key] = new(PluginDownloadPhase.Canceled);
            }
        }
        catch (Exception exception)
        {
            _log?.Warn("Plugins", $"插件下载失败: id={plugin.Id} error={exception.GetType().Name}");
            Publish(key, new(PluginDownloadPhase.Failed, FailureKey: "Plugins.Error.File"), operation);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_operation, operation))
                {
                    if (!_disposed && !_stopping && token.IsCancellationRequested &&
                        _states[key].Phase is PluginDownloadPhase.Downloading or PluginDownloadPhase.Verifying)
                        _states[key] = new(PluginDownloadPhase.Canceled);
                    _operation = null;
                }
            }
            operation.Dispose();
            Notify();
        }
    }

    private async Task DownloadAttemptAsync(string url, string temporary, PluginAsset asset, Action<double> progress,
        CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromMinutes(3));
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
        headers.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
        headers.CancelAfter(Timeout.InfiniteTimeSpan);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Insecure download redirect.");
        if (response.Content.Headers.ContentLength is { } size && size != asset.Size)
            throw new InvalidDataException("Unexpected content length.");

        await using var source = await response.Content.ReadAsStreamAsync(attempt.Token).ConfigureAwait(false);
        await using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
            idle.CancelAfter(TimeSpan.FromSeconds(30));
            var count = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            if (count == 0) break;
            received += count;
            if (received > asset.Size) throw new InvalidDataException("Download exceeds declared size.");
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), attempt.Token).ConfigureAwait(false);
            var percent = (int)(received * 100 / asset.Size);
            if (percent != lastPercent) { lastPercent = percent; progress(percent); }
        }
        if (received != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin integrity check failed.");
        await destination.FlushAsync(attempt.Token).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyAsync(string path, PluginAsset asset, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != asset.Size) return false;
        var digest = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(digest).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private string GetDestination(PluginDescriptor plugin, PluginAsset asset)
    {
        if (!plugin.Assets.Contains(asset) || !IsSafeSegment(plugin.Id) || !IsSafeSegment(plugin.Version) ||
            !IsSafeSegment(asset.FileName) || asset.Size is <= 0 or > MaximumDownloadBytes ||
            asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Invalid plugin attachment.");
        return Path.Combine(_root, plugin.Id, plugin.Version, asset.FileName);
    }

    private static bool IsSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\') || Path.IsPathRooted(value))
            return false;
        var stem = value.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$") &&
            !(stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)));
    }

    private void EnsureOwnedDirectory(string directory)
    {
        RejectReparsePoint(_root);
        Directory.CreateDirectory(_root);
        var relative = Path.GetRelativePath(_root, directory);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new IOException("Plugin path escapes its root.");
        var current = _root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
            Directory.CreateDirectory(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Plugin paths cannot contain reparse points.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    /// <summary>仅显示本服务管理的完成文件，不执行该文件。</summary>
    public Task<bool> RevealAsync(PluginDescriptor plugin, PluginAsset asset) => Task.Run(() =>
    {
        try
        {
            var target = GetDestination(plugin, asset);
            EnsureOwnedDirectory(Path.GetDirectoryName(target)!);
            RejectReparsePoint(target);
            if (GetState(plugin, asset).Phase != PluginDownloadPhase.Completed || !File.Exists(target)) return false;
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log?.Warn("Plugins", $"无法显示插件文件: error={exception.GetType().Name}");
            return false;
        }
    });

    private static string Key(PluginDescriptor plugin, PluginAsset asset) => $"{plugin.Id}/{plugin.Version}/{asset.FileName}";

    private void Publish(string key, PluginDownloadState state, CancellationTokenSource operation)
    {
        lock (_gate)
        {
            if (_disposed || _stopping || operation.IsCancellationRequested || !ReferenceEquals(_operation, operation)) return;
            _states[key] = state;
        }
        Notify();
    }

    private void Notify()
    {
        Action? handlers;
        lock (_gate) { if (_disposed || _stopping) return; handlers = StateChanged; }
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception exception) { _log?.Warn("Plugins", $"下载状态订阅异常: error={exception.GetType().Name}"); }
        }
    }

    private void RemoveTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log?.Warn("Plugins", $"临时插件文件清理失败: error={exception.GetType().Name}");
        }
    }

    private static void TryCancel(CancellationTokenSource? operation)
    {
        try { operation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        CancellationTokenSource? operation;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stopping = true;
            operation = _operation;
            StateChanged = null;
        }
        TryCancel(operation);
        if (_ownsClient) _client.Dispose();
    }
}
