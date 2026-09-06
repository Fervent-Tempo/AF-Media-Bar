using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 读取网易云客户端内存并提供更精确的进度、歌曲标识、封面和歌词。
/// Reads NetEase client memory and provides precise progress, song identity, artwork, and lyrics.
/// </summary>
public sealed class NetEaseMediaProvider : IMediaSourceProvider
{
    private const string MemoryPlayerSourceId = "cloudmusic";
    private const string NetEaseWindowClass = "OrpheusBrowserHost";
    private const string UnknownArtistName = "未知艺术家";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(233);
    private readonly Dispatcher _dispatcher;
    private readonly LyricsService _lyricsService;
    private readonly Dictionary<string, BitmapImage?> _artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingArtwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LyricsResult?> _lyricsCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private NetEase? _memoryPlayer;
    private PlayerInfo? _currentInfo;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private int _version;
    private bool _isDisposed;

    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    public NetEaseMediaProvider(LyricsService lyricsService)
    {
        _dispatcher = Application.Current.Dispatcher;
        _lyricsService = lyricsService;
    }

    /// <summary>
    /// 判断来源标识是否属于网易云音乐。
    /// Determines whether the source identifier belongs to NetEase Cloud Music.
    /// </summary>
    public bool CanHandle(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("163music", StringComparison.OrdinalIgnoreCase);

    public void UpdateSessionSnapshot(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
    }

    public void Start()
    {
        if (_isDisposed || _cancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _ = PollAsync(cancellation, cancellation.Token);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation?.Cancel();
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
    }

    private async Task PollAsync(CancellationTokenSource cancellation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                PlayerInfo? playerInfo = null;
                try
                {
                    playerInfo = ReadMemoryPlayerInfo();
                }
                catch
                {
                    ResetMemoryPlayer();
                }

                if (_isDisposed)
                {
                    return;
                }

                if (playerInfo is { } info && ShouldUseMemoryPlayerInfo(info))
                {
                    PublishPlayerInfo(info, token);
                }
                else
                {
                    PublishSnapshot(null);
                }

                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private PlayerInfo? ReadMemoryPlayerInfo()
    {
        var hwnd = NativeMethods.FindWindow(NetEaseWindowClass, null);
        if (hwnd == IntPtr.Zero)
        {
            ResetMemoryPlayer();
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId <= 0)
        {
            ResetMemoryPlayer();
            return null;
        }

        if (_memoryPlayer is null || !_memoryPlayer.Validate(processId))
        {
            _memoryPlayer?.Dispose();
            _memoryPlayer = new NetEase(processId);
        }

        return _memoryPlayer.GetPlayerInfo();
    }

    private void ResetMemoryPlayer()
    {
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
        _currentInfo = null;
        _version++;
    }

    private bool ShouldUseMemoryPlayerInfo(PlayerInfo playerInfo)
    {
        if (!playerInfo.Pause)
        {
            return true;
        }

        return !_sessionSnapshot.IsConnected ||
            CanHandle(_sessionSnapshot.SourceId) ||
            string.Equals(
                _sessionSnapshot.SourceName,
                MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, "未知来源"),
                StringComparison.OrdinalIgnoreCase);
    }

    private void PublishPlayerInfo(PlayerInfo info, CancellationToken token)
    {
        var version = _currentInfo is { } current &&
            string.Equals(current.Identity, info.Identity, StringComparison.Ordinal) &&
            string.Equals(current.Cover, info.Cover, StringComparison.Ordinal)
                ? _version
                : ++_version;
        _currentInfo = info;

        ImageSource? artwork = null;
        if (_artworkCache.TryGetValue(info.Cover, out var cachedArtwork))
        {
            artwork = cachedArtwork;
        }
        else if (!string.IsNullOrWhiteSpace(info.Cover) && _pendingArtwork.Add(info.Cover))
        {
            _ = LoadArtworkAsync(info.Cover, version, token);
        }

        _lyricsCache.TryGetValue(info.Identity, out var lyrics);
        var shouldLoadLyrics = !_lyricsCache.ContainsKey(info.Identity) && _pendingLyrics.Add(info.Identity);
        PublishSnapshot(CreateSnapshot(info, artwork, lyrics));
        if (shouldLoadLyrics)
        {
            _ = LoadLyricsAsync(info, token);
        }
    }

    private MediaSnapshot CreateSnapshot(PlayerInfo info, ImageSource? artwork, LyricsResult? lyrics) =>
        new(
            true,
            !info.Pause,
            false,
            false,
            false,
            info.Title,
            string.IsNullOrWhiteSpace(info.Artists) ? UnknownArtistName : info.Artists,
            MemoryPlayerSourceId,
            MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, "未知来源"),
            artwork,
            lyrics,
            info.Schedule);

    private void PublishSnapshot(MediaSnapshot? snapshot)
    {
        if (_isDisposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _dispatcher.BeginInvoke(
            () => SnapshotChanged?.Invoke(this, snapshot),
            DispatcherPriority.Background);
    }

    private async Task LoadArtworkAsync(string coverUrl, int version, CancellationToken token)
    {
        try
        {
            var artwork = await BitmapHelper.GetImageFromUrlAsync(coverUrl, token);
            _artworkCache[coverUrl] = artwork;
            if (artwork is not null && !_isDisposed && version == _version &&
                _currentInfo is { } info && string.Equals(info.Cover, coverUrl, StringComparison.OrdinalIgnoreCase))
            {
                _lyricsCache.TryGetValue(info.Identity, out var lyrics);
                PublishSnapshot(CreateSnapshot(info, artwork, lyrics));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _artworkCache[coverUrl] = null;
        }
        finally
        {
            _pendingArtwork.Remove(coverUrl);
        }
    }

    private async Task LoadLyricsAsync(PlayerInfo info, CancellationToken token)
    {
        try
        {
            var request = new LyricsRequest(info.Title, info.Artists, info.Album, info.Duration, info.Identity);
            var result = await _lyricsService.GetLyricsAsync(request, token);
            _lyricsCache[info.Identity] = result;
            if (!_isDisposed && _currentInfo is { } current &&
                string.Equals(current.Identity, info.Identity, StringComparison.Ordinal))
            {
                _artworkCache.TryGetValue(current.Cover, out var artwork);
                PublishSnapshot(CreateSnapshot(current, artwork, result));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _lyricsCache[info.Identity] = null;
        }
        finally
        {
            _pendingLyrics.Remove(info.Identity);
        }
    }

}
