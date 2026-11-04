// 独占后台读取器与歌词请求；UI 线程维护快照和缓存，停止后丢弃迟到结果。
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>提供独立酷狗来源，SMTC 可用时补充同曲目的封面和控制。</summary>
public sealed class KuGouMediaProvider : IIndependentMediaSourceProvider, IMemoryPrunable
{
    private const int CacheCapacity = 64;
    private readonly KuGouSourcePolicy _sourcePolicy = new();
    private readonly Dispatcher _dispatcher;
    private readonly AppLogService? _log;
    private readonly LyricsService _lyricsService;
    private readonly Func<IKuGouPlaybackReader> _createReader;
    private readonly LruCache<string, LyricsResult?> _lyricsCache;
    private readonly LyricsRetryTracker<string> _lyricsRetries = new(CacheCapacity);
    private readonly Dictionary<string, CancellationTokenSource> _pendingLyrics = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task _pollTask = Task.CompletedTask;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private KuGouPlaybackInfo? _currentInfo;
    private DateTimeOffset _readAt;
    private int _cacheGeneration;
    private MemoryPruneLevel _pruneLevel;
    private volatile int _pollMilliseconds = 100;
    private volatile bool _isDisposed;

    public IMediaSourcePolicy SourcePolicy => _sourcePolicy;
    public bool ProvidesLyrics => true;
    public string PruneParticipantName => "kugou-source";
    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    public KuGouMediaProvider(LyricsService lyricsService, Dispatcher dispatcher, AppLogService? log = null)
        : this(lyricsService, dispatcher, () => new KuGouPlaybackReader(), log: log) { }

    internal KuGouMediaProvider(LyricsService lyricsService, Dispatcher dispatcher,
        Func<IKuGouPlaybackReader> createReader, TimeProvider? timeProvider = null, AppLogService? log = null)
    {
        _lyricsService = lyricsService;
        _dispatcher = dispatcher;
        _log = log;
        _createReader = createReader;
        _lyricsCache = new(CacheCapacity, result => LyricsCacheBudgetPolicy.EstimateBytes(result?.Document),
            LyricsCacheBudgetPolicy.DefaultBudgetBytes, timeProvider);
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public bool CanHandle(string sourceId) => _sourcePolicy.Matches(sourceId);
    public void UpdateSessionSnapshot(MediaSnapshot snapshot) => Volatile.Write(ref _sessionSnapshot,
        snapshot.IsConnected && CanHandle(snapshot.SourceId) ? snapshot : MediaSnapshot.Disconnected);
    private bool IsAllowed => MediaSourceFilterPolicy.IsAllowed(KuGouSourcePolicy.SourceId,
        SettingsManager.Current.SmtcSourceFilter, _sourcePolicy.NormalizeSourceId);

    public void Start()
    {
        if (_isDisposed || _cancellation is not null || !IsAllowed || _pruneLevel >= MemoryPruneLevel.DisplayOff) return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _pollTask = Task.Run(() => PollAsync(cancellation));
    }

    private async Task PollAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var entered = false;
        IKuGouPlaybackReader? reader = null;
        try
        {
            await _readerGate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            reader = _createReader();
            while (!token.IsCancellationRequested)
            {
                var baseline = Volatile.Read(ref _sessionSnapshot);
                var info = reader.Read(baseline);
                var readAt = DateTimeOffset.UtcNow;
                token.ThrowIfCancellationRequested();
                if (_dispatcher.HasShutdownStarted) return;
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed || token.IsCancellationRequested || !ReferenceEquals(_cancellation, cancellation) || !IsAllowed) return;
                    if (info is { UsesSessionMetadata: true } && !ReferenceEquals(baseline, Volatile.Read(ref _sessionSnapshot))) return;
                    ApplyRead(info, readAt, token);
                }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
                await Task.Delay(Math.Max(_pollMilliseconds, info is null ? 1000 : 100), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_isDisposed && !token.IsCancellationRequested)
                _log?.Error("Media", "酷狗独立来源轮询失败", exception);
        }
        finally
        {
            cancellation.Cancel();
            try { reader?.Dispose(); }
            catch (Exception exception) { _log?.Error("Media", "酷狗读取器释放失败", exception); }
            if (entered) _readerGate.Release();
            try
            {
                if (!_dispatcher.HasShutdownStarted)
                    await _dispatcher.InvokeAsync(() =>
                    {
                        if (!_isDisposed && ReferenceEquals(_cancellation, cancellation))
                        {
                            _cancellation = null;
                            ApplyRead(null, DateTimeOffset.UtcNow, CancellationToken.None);
                        }
                    }, DispatcherPriority.Background).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { _log?.Error("Media", "酷狗来源停止发布失败", exception); }
            finally { cancellation.Dispose(); }
        }
    }

    private void ApplyRead(KuGouPlaybackInfo? info, DateTimeOffset readAt, CancellationToken token)
    {
        if (_currentInfo?.Identity != info?.Identity) CancelLyrics();
        _currentInfo = info;
        _readAt = readAt;
        if (info is null)
        {
            SnapshotChanged?.Invoke(this, null);
            return;
        }
        var hasCached = _lyricsCache.TryGetValue(info.Identity, out var lyrics);
        var pending = _pendingLyrics.ContainsKey(info.Identity);
        var fetch = !hasCached && !pending && _lyricsRetries.CanRequest(info.Identity, info.IsPlaying);
        Publish(info, lyrics, pending || fetch);
        if (!fetch || _isDisposed || token.IsCancellationRequested || _currentInfo?.Identity != info.Identity) return;
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _pendingLyrics.Add(info.Identity, requestCancellation);
        _ = LoadLyricsAsync(info, requestCancellation);
    }

    private void Publish(KuGouPlaybackInfo info, LyricsResult? lyrics, bool searching)
    {
        if (_isDisposed) return;
        SnapshotChanged?.Invoke(this, new MediaSnapshot(true, info.IsPlaying, false, false, false,
            info.Title, info.Artist, KuGouSourcePolicy.SourceId,
            MediaSourceNameFormatter.GetDisplayName(KuGouSourcePolicy.SourceId, ""), null, lyrics,
            info.Position, info.Duration, false, false, MediaRepeatMode.Unavailable, 1, _readAt)
        { IsSearchingLyrics = searching });
    }

    private async Task LoadLyricsAsync(KuGouPlaybackInfo info, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var generation = _cacheGeneration;
        try
        {
            var result = await _lyricsService.GetLyricsAsync(new(info.Title, info.Artist, "", info.Duration, null,
                PlaybackSourceId: KuGouSourcePolicy.SourceId), token);
            if (_isDisposed || token.IsCancellationRequested || generation != _cacheGeneration) return;
            _lyricsRetries.RecordResult(info.Identity, result, info.Title);
            _lyricsCache.Set(info.Identity, result, LyricsCacheRetentionPolicy.Lifetime(result));
            if (_currentInfo is { } current && current.Identity == info.Identity) Publish(current, result, false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_isDisposed && !token.IsCancellationRequested && generation == _cacheGeneration)
            {
                _lyricsRetries.RecordResult(info.Identity, null, info.Title);
                _lyricsCache.Set(info.Identity, null, LyricsCacheRetentionPolicy.Lifetime(null));
                _log?.Error("Lyrics", "酷狗来源取词失败", exception);
            }
        }
        finally
        {
            if (_pendingLyrics.TryGetValue(info.Identity, out var current) && ReferenceEquals(current, cancellation))
                _pendingLyrics.Remove(info.Identity);
            cancellation.Dispose();
        }
    }

    private void CancelLyrics()
    {
        var requests = _pendingLyrics.Values.ToArray();
        _pendingLyrics.Clear();
        foreach (var request in requests) request.Cancel();
    }

    private void StopPolling()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        CancelLyrics();
        _currentInfo = null;
        if (!_isDisposed) SnapshotChanged?.Invoke(this, null);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_isDisposed) return;
        if (LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            _cacheGeneration++;
            _lyricsCache.Clear();
            _lyricsRetries.Clear();
            CancelLyrics();
        }
        if (IsAllowed) Start();
        else StopPolling();
    }

    public void Prune(MemoryPruneLevel level)
    {
        if (_isDisposed) return;
        _pruneLevel = level;
        if (level != MemoryPruneLevel.None)
        {
            _cacheGeneration++;
            _lyricsCache.Clear();
            CancelLyrics();
        }
        if (level >= MemoryPruneLevel.DisplayOff) { StopPolling(); return; }
        _pollMilliseconds = level == MemoryPruneLevel.Idle ? 2000 : 100;
        Start();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        StopPolling();
        _cacheGeneration++;
        _lyricsCache.Clear();
    }

    internal Task PollCompletion => _pollTask;
}
