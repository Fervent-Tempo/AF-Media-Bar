// 仅从本机 Nons 封面端点取低分辨率缩略图；拥有 HttpClient、缓存和有界下载并发。
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

/// <summary>队列列表的低清封面缩略图服务；失败只回退占位图，不影响队列功能。</summary>
public sealed class NonsCoverThumbnailService : IDisposable
{
    private const int DecodePixelWidth = 40;
    private const int MaximumCachedImages = 256;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<BitmapSource?>> _inFlight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloadGate = new(4, 4);
    private bool _disposed;

    public NonsCoverThumbnailService() : this(CreateClient())
    {
    }

    internal NonsCoverThumbnailService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>取一个已冻结的 40px 缩略图；同一曲目共用缓存和进行中的请求。</summary>
    public Task<BitmapSource?> LoadAsync(PlayerCompanionTrack track, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) || string.IsNullOrWhiteSpace(track.CoverUrl))
            return Task.FromResult<BitmapSource?>(null);

        lock (_gate)
        {
            if (_cache.TryGetValue(track.TrackId, out var cached))
                return Task.FromResult<BitmapSource?>(cached);
            if (_inFlight.TryGetValue(track.TrackId, out var existing))
            {
                if (!existing.IsCompleted)
                    return existing;
                _inFlight.Remove(track.TrackId);
            }

            var task = LoadCoreAsync(track, cancellationToken);
            _inFlight[track.TrackId] = task;
            return task;
        }
    }

    private async Task<BitmapSource?> LoadCoreAsync(PlayerCompanionTrack track, CancellationToken cancellationToken)
    {
        try
        {
            // 只接受 Nons 返回的相对本机路径；不跟随任意绝对地址。
            if (!Uri.TryCreate(track.CoverUrl, UriKind.Relative, out var relative))
                return null;

            await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var uri = new Uri(NonsApiClient.DefaultBaseAddress, relative);
                var bytes = await _httpClient.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
                var image = await Task.Run(() => Decode(bytes), cancellationToken).ConfigureAwait(false);
                if (image is not null)
                {
                    lock (_gate)
                    {
                        if (_cache.Count >= MaximumCachedImages)
                            _cache.Clear();
                        _cache[track.TrackId] = image;
                    }
                }
                return image;
            }
            finally
            {
                _downloadGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            // 封面是装饰性内容；失败保留主题占位图，不把错误传播到队列。
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(track.TrackId);
            }
        }
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = DecodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };
        return new HttpClient(handler)
        {
            BaseAddress = NonsApiClient.DefaultBaseAddress,
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed))
            return;

        Volatile.Write(ref _disposed, true);
        _httpClient.Dispose();
        _downloadGate.Dispose();
    }
}
