using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 读取网易云客户端内存并提供更精确的进度、歌曲标识、封面和歌词。
/// Reads NetEase client memory and provides precise progress, song identity, artwork, and lyrics.
/// </summary>
public sealed class NetEaseMediaProvider : IMediaSourceProvider, IMemoryPrunable
{
    private const string MemoryPlayerSourceId = "cloudmusic";
    private const string NetEaseWindowClass = "OrpheusBrowserHost";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(233);

    /// <summary>
    /// 空闲档位的轮询周期。空闲意味着已经有五分钟没有媒体、十分钟没有用户操作：此时连"有没有在放"都不需要每秒问四次，
    /// 两秒一次足够在用户按下播放后很快跟上，而内存读取频率降到原来的十二分之一。
    /// The poll period at the idle level. Idle means no media for five minutes and no user input for ten, so "is anything playing" does not need to be
    /// asked four times a second: once every two seconds still follows a play press closely while cutting memory reads to a twelfth.
    /// </summary>
    private const int IdlePollIntervalMilliseconds = 2_000;

    /// <summary>
    /// 歌词缓存的容量：来源变多以后必须封顶，否则长时间播放会一直堆积解析结果。
    /// 条数之外还受 <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/> 约束（见该策略的说明）。
    /// Capacity of the lyric cache: with more sources it has to be capped, otherwise long playback keeps accumulating parsed results. On top of that entry
    /// count it is bounded by <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/>; see that policy for why.
    /// </summary>
    private const int LyricsCacheCapacity = 64;

    /// <summary>
    /// 封面缓存的容量：每张封面解码后约 256 KB，无界字典会随播放曲目数一直涨（长时间播放是一条稳定的内存增长曲线）。
    /// 只留最后几首的封面足够：封面总与当前曲目一起出现，切回上一首时重新下载一次的代价远小于常驻几十兆。
    /// Capacity of the artwork cache: one decoded cover is about 256 KB, and an unbounded dictionary grows with the number of played
    /// tracks, which is a steady memory climb over a long session. Keeping the last few covers is enough: a cover only appears together
    /// with its track, and re-downloading one beats keeping tens of megabytes resident.
    /// </summary>
    private const int ArtworkCacheCapacity = 8;

    private readonly Dispatcher _dispatcher;
    private readonly LyricsService _lyricsService;
    private readonly LruCache<string, BitmapImage?> _artworkCache = new(ArtworkCacheCapacity);
    private readonly HashSet<string> _pendingArtwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly LruCache<string, LyricsResult?> _lyricsCache = new(
        LyricsCacheCapacity,
        result => LyricsCacheBudgetPolicy.EstimateBytes(result?.Document),
        LyricsCacheBudgetPolicy.DefaultBudgetBytes);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    /// <summary>缓存代次：取词设置变化时自增，让仍在飞行中的结果写不回来。/ Cache generation: incremented when retrieval settings change, so an in-flight result cannot be written back.</summary>
    private int _lyricsCacheGeneration;
    private CancellationTokenSource? _cancellation;
    private NetEase? _memoryPlayer;
    private PlayerInfo? _currentInfo;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private int _version;
    private bool _isDisposed;

    /// <summary>
    /// 轮询周期。剪枝会改写它，而轮询线程在另一个线程上读取，因此这是一个 volatile 字段而不是配置常量。
    /// The poll period. Pruning rewrites it and the polling thread reads it from another thread, so it is a volatile field rather than a constant.
    /// </summary>
    private volatile int _pollIntervalMilliseconds = (int)PollInterval.TotalMilliseconds;

    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    /// <summary>
    /// 创建网易云来源提供器；实际进程读取在显式启动后进行，并由本实例负责释放。
    /// Creates the NetEase source provider; process reading begins only after explicit start and is owned by this instance.
    /// </summary>
    public NetEaseMediaProvider(LyricsService lyricsService)
    {
        _dispatcher = Application.Current.Dispatcher;
        _lyricsService = lyricsService;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 取词相关设置变化时清空缓存：本提供器每 233 毫秒轮询一次，因此下一次轮询会用新设置重新取词。
    /// Clears the cache after a retrieval-related settings change: this provider polls every 233 ms, so the next poll refetches with
    /// the new settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;
    }

    /// <summary>
    /// 判断来源标识是否属于网易云音乐。
    /// Determines whether the source identifier belongs to NetEase Cloud Music.
    /// </summary>
    public bool CanHandle(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("163music", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 更新 SMTC 基线快照，供来源专用数据合并时保持媒体身份一致。
    /// Updates the SMTC baseline snapshot used to preserve media identity during source-specific enrichment.
    /// </summary>
    public void UpdateSessionSnapshot(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
    }

    /// <summary>
    /// 幂等启动来源轮询；重复调用不会创建额外计时器。
    /// Idempotently starts source polling without creating additional timers on repeated calls.
    /// </summary>
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

    /// <summary>
    /// 停止轮询并释放当前播放器读取器，阻止释放后继续发布快照。
    /// Stops polling and disposes the active player reader so no snapshots are published after disposal.
    /// </summary>
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

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "netease-source";

    /// <summary>
    /// 按档位丢弃封面与歌词缓存，并放慢或停掉内存轮询。
    /// Drops the artwork and lyric caches for the level and slows down or stops the memory poll.
    ///
    /// 两级处理是有区别的：空闲档位只是"没人看，别那么勤快"，而显示器关闭或系统睡眠时连"看看有没有在放"都不必做——恢复由媒体事件驱动，
    /// 协调器一收到播放状态变化就会把档位调回常规，本提供器随即被重新启动。
    /// The two levels differ on purpose: the idle level only says "nobody is looking, so do not be so eager", while a closed display or a suspending
    /// system does not even need the "is anything playing" check, because the restore is driven by media events: the coordinator drops back to the
    /// ordinary level the moment a playback state changes, which restarts this provider.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_isDisposed)
        {
            return;
        }

        if (level >= MemoryPruneLevel.Idle)
        {
            // 缓存清掉不会让界面变空：正在显示的那张封面由快照自己持有，这里丢掉的只是"下次再要时不用重新下载"的那一份。
            // Clearing the caches does not blank the interface: the cover on screen is held by the snapshot itself, and what is dropped here is only
            // the copy that saved a re-download.
            _artworkCache.Clear();
            _lyricsCache.Clear();
            _lyricsCacheGeneration++;
        }

        if (level >= MemoryPruneLevel.DisplayOff)
        {
            StopPolling();
            return;
        }

        _pollIntervalMilliseconds = level == MemoryPruneLevel.Idle
            ? IdlePollIntervalMilliseconds
            : (int)PollInterval.TotalMilliseconds;

        // 从 L2/L3 回到 L0/L1 时轮询是停着的，这里按需重启（Start 自身幂等）。
        // Coming back from L2/L3 the poll is stopped, so it is restarted here on demand; Start is idempotent by itself.
        Start();
    }

    /// <summary>
    /// 停止轮询并释放内存读取器，保留提供器本身可再次启动。
    /// Stops polling and releases the memory reader while keeping the provider restartable.
    /// </summary>
    private void StopPolling()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        ResetMemoryPlayer();
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

                await Task.Delay(_pollIntervalMilliseconds, token);
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

        // SMTC 报出的曲名随基线快照一起传进去：私人FM 的 fmPlay 队列在按 id 查不到当前曲目时只按 currentIndex
        // 兜底，而队列可能是上一次私人FM 会话留下的，因此那一条 MUST 与 SMTC 的曲名对得上才会被采纳。
        // The title SMTC reports travels with the baseline snapshot: the private-FM fmPlay queue falls back to currentIndex when the
        // current track cannot be found by id, and that queue may be left over from a previous FM session, so such an entry is
        // accepted only when its title matches the one SMTC reports.
        return _memoryPlayer.GetPlayerInfo(_sessionSnapshot.IsConnected ? _sessionSnapshot.Title : null);
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
                MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, Translations.Get("Service.MediaSource.Unknown")),
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

        var hasCachedLyrics = _lyricsCache.TryGetValue(info.Identity, out var lyrics);
        var shouldLoadLyrics = !hasCachedLyrics && _pendingLyrics.Add(info.Identity);
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
            info.Artists,
            MemoryPlayerSourceId,
            MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, Translations.Get("Service.MediaSource.Unknown")),
            artwork,
            lyrics,
            info.Schedule,
            info.Duration,
            false,
            false,
            MediaRepeatMode.Unavailable,
            1,
            DateTimeOffset.UtcNow);

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
            var artwork = await ArtworkLoader.GetImageFromUrlAsync(coverUrl, token);
            _artworkCache.Set(coverUrl, artwork);
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
            _artworkCache.Set(coverUrl, null);
        }
        finally
        {
            _pendingArtwork.Remove(coverUrl);
        }
    }

    private async Task LoadLyricsAsync(PlayerInfo info, CancellationToken token)
    {
        var generation = _lyricsCacheGeneration;
        try
        {
            var request = new LyricsRequest(info.Title, info.Artists, info.Album, info.Duration, info.Identity);
            var result = await _lyricsService.GetLyricsAsync(request, token);

            // 取词过程中设置若被改过，这次结果已经不属于当前配置，写入只会让用户以为设置没生效。
            // If the settings changed while this retrieval ran, the result no longer belongs to the current configuration and
            // writing it would only make the setting look ineffective.
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, result);
            }

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
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, null);
            }
        }
        finally
        {
            _pendingLyrics.Remove(info.Identity);
        }
    }

}
