using System.Globalization;
using System.IO;
using AFMediaBar.Abstractions;
using AFMediaBar.Models;
using AFMediaBar.Services.Lyrics;
using AFMediaBar.Services.Players;
using AFMediaBar.Services.Win32Api;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AFMediaBar.Services;

/// <summary>
/// 订阅 GSMTC 会话，选择媒体来源，并发布 UI 无关的稳定快照。
/// Subscribes to GSMTC sessions, selects a source, and publishes UI-independent snapshots.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private const string MemoryPlayerSourceId = "cloudmusic";
    private const string NetEaseWindowClass = "OrpheusBrowserHost";
    private static readonly TimeSpan SessionReconnectGracePeriod = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MemoryPlayerPollInterval = TimeSpan.FromMilliseconds(233);
    // 播放器常先发布标题、后发布封面；短时重试等待来源完成更新。
    // Players often publish text before artwork; short retries wait for settled metadata.
    private static readonly TimeSpan[] ArtworkRefreshDelays =
    [
        TimeSpan.FromMilliseconds(220),
        TimeSpan.FromMilliseconds(380),
        TimeSpan.FromMilliseconds(600),
        TimeSpan.FromMilliseconds(900),
        TimeSpan.FromMilliseconds(1500)
    ];

    // GSMTC 回调可并发到达，此门锁串行化会话列表与当前来源切换。
    // GSMTC callbacks may overlap; this gate serializes list and selection changes.
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly List<SessionEntry> _entries = [];
    private readonly IArtworkDecoder _artworkDecoder;
    private readonly IArtworkUriLoader _artworkUriLoader;
    private readonly IStringLocalizer _localizer;
    private readonly object _publishGate = new();
    private readonly object _memoryPlayerGate = new();
    private readonly Dictionary<string, MemoryArtworkCacheEntry> _memoryArtworkCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingMemoryArtworkUrls =
        new(StringComparer.OrdinalIgnoreCase);
    // 歌词按歌曲 id 缓存（null 表示已尝试但无歌词），pending 集合去重并发拉取。
    // Lyrics cache per song id (null marks an attempted miss); pending set deduplicates in-flight fetches.
    private readonly Dictionary<string, LyricsResult?> _lyricsCache =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLyricsIdentities =
        new(StringComparer.Ordinal);
    private readonly LyricsService _lyricsService =
        new(new NetEaseLyricsProvider(), new LrclibLyricsProvider());
    // manager 和 session 都注册了事件，Dispose/SetSession 必须成对退订。
    // Both manager and session own event subscriptions that must be removed on teardown.
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _selectedKey;
    private string? _preferredSourceId;
    private string? _preferredSourceName;
    // 来源短暂消失时保留选择，取消令牌负责终止过期的宽限期刷新。
    // Preserve briefly missing sources; cancellation stops obsolete grace-period refreshes.
    private DateTime? _sessionMissingSinceUtc;
    private CancellationTokenSource? _sessionReconnectCancellation;
    private CancellationTokenSource? _artworkRefreshCancellation;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private MediaSnapshot? _memorySnapshot;
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    // identity 标识曲目，fingerprint 标识封面内容，pending 防止重复重试。
    // Identity tracks the item, fingerprint tracks pixels, and pending deduplicates retries.
    private string? _artworkIdentity;
    private string? _artworkFingerprint;
    private string? _pendingArtworkIdentity;
    // 单调版本号阻止较慢的旧异步读取覆盖较新的媒体来源。
    // A monotonic version prevents stale async reads from overwriting a newer source.
    private int _refreshVersion;
    private int _memoryPlayerVersion;
    private CancellationTokenSource? _memoryPlayerCancellation;
    private IMusicPlayer? _memoryPlayer;
    private PlayerInfo? _memoryPlayerInfo;
    private bool _disposed;

    public MediaSessionService(
        IArtworkDecoder artworkDecoder,
        IStringLocalizer localizer)
    {
        _artworkDecoder = artworkDecoder;
        _artworkUriLoader = new HttpArtworkLoader(artworkDecoder);
        _localizer = localizer;
    }

    public event EventHandler<MediaSnapshot>? SnapshotChanged;
    public event Action<IReadOnlyList<MediaSessionOption>>? SessionsChanged;

    public string SelectedSourceId => _lastSnapshot.SourceId;
    public string SelectedSourceName => _lastSnapshot.SourceName;

    public async Task InitializeAsync()
    {
        StartMemoryPlayerPoll();

        if (_manager is null)
        {
            // WinRT 事件是订阅制；Dispose 中必须退订，避免服务被 manager 保活。
            // WinRT events are subscription-based; Dispose must detach them.
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnManagerSessionsChanged;
            _manager.SessionsChanged += OnManagerSessionsChanged;
        }

        await RefreshSessionListAsync();
    }

    public Task ReconnectAsync()
    {
        return RefreshSessionListAsync();
    }

    public async Task SelectSessionAsync(string key)
    {
        SessionEntry? entry = null;
        await _sessionGate.WaitAsync();
        try
        {
            entry = _entries.FirstOrDefault(candidate => candidate.Key == key);
            if (entry is null)
            {
                return;
            }

            SelectEntry(entry);
            PublishSessions();
            Publish(MediaSnapshot.Disconnected with
            {
                IsConnected = true,
                Title = entry.DisplayName,
                Artist = _localizer.Get("Main.Media.LoadingArtist"),
                SourceId = entry.SourceId,
                SourceName = entry.DisplayName
            });
        }
        finally
        {
            _sessionGate.Release();
        }

        if (entry is not null)
        {
            await RefreshMediaPropertiesAsync();
        }
    }

    public Task SelectNextSessionAsync()
    {
        return SelectRelativeSessionAsync(1);
    }

    public Task SelectPreviousSessionAsync()
    {
        return SelectRelativeSessionAsync(-1);
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_session is not null)
        {
            await _session.TryTogglePlayPauseAsync();
        }
    }

    public async Task SkipPreviousAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipPreviousAsync();
        }
    }

    public async Task SkipNextAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipNextAsync();
        }
    }

    private string GetDisplayName(string? sourceId) =>
        MediaSourceNameFormatter.GetDisplayName(
            sourceId,
            _localizer.Get("Main.Media.UnknownSource"));

    private async Task SelectRelativeSessionAsync(int direction)
    {
        string? key = null;
        await _sessionGate.WaitAsync();
        try
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var currentIndex = _entries.FindIndex(entry =>
                entry.Key == _selectedKey);
            if (currentIndex < 0)
            {
                // 宽限期内原来源已离开列表；滚轮应直接选择剩余来源，即使只剩一个。
                // During the grace period the old source is absent; select a remaining source even when only one exists.
                key = direction > 0
                    ? _entries[0].Key
                    : _entries[^1].Key;
            }
            else if (_entries.Count > 1)
            {
                var nextIndex =
                    (currentIndex + direction + _entries.Count) % _entries.Count;
                key = _entries[nextIndex].Key;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (key is not null)
        {
            await SelectSessionAsync(key);
        }
    }

    private async void OnManagerSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        object args)
    {
        await RunSessionEventAsync(RefreshSessionListAsync);
    }

    private async Task RefreshSessionListAsync()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        await _sessionGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            foreach (var entry in _entries)
            {
                entry.Session.PlaybackInfoChanged -= OnAnyPlaybackInfoChanged;
            }

            var previousSession = _session;
            _entries.Clear();
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in _manager.GetSessions())
            {
                var sourceId = session.SourceAppUserModelId ?? string.Empty;
                occurrences.TryGetValue(sourceId, out var occurrence);
                occurrence++;
                occurrences[sourceId] = occurrence;
                var key = $"{sourceId}\u001f{occurrence}";
                var displayName = GetDisplayName(sourceId);
                if (occurrence > 1)
                {
                    displayName = $"{displayName} ({occurrence})";
                }

                var entry = new SessionEntry(key, sourceId, displayName, session);
                _entries.Add(entry);
                session.PlaybackInfoChanged += OnAnyPlaybackInfoChanged;
            }

            // 最后一个来源明确关闭时列表为空，应立即清除旧封面而非进入宽限期。
            // An empty list means the last source closed; clear stale metadata immediately.
            if (_entries.Count == 0)
            {
                CancelSessionReconnectGrace();
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                SetSession(null);
                PublishSessions();
                Publish(MediaSnapshot.Disconnected);
                return;
            }

            var selected = _entries.FirstOrDefault(entry =>
                ReferenceEquals(entry.Session, previousSession));
            selected ??= _entries.FirstOrDefault(entry => entry.Key == _selectedKey);
            selected ??= _entries.FirstOrDefault(entry =>
                !string.IsNullOrWhiteSpace(_preferredSourceId) &&
                string.Equals(
                    entry.SourceId,
                    _preferredSourceId,
                    StringComparison.OrdinalIgnoreCase));

            if (selected is null && ShouldHoldPreferredSource())
            {
                HoldPreferredSource();
                return;
            }

            CancelSessionReconnectGrace();
            if (selected is null)
            {
                var current = _manager.GetCurrentSession();
                selected = _entries.FirstOrDefault(entry =>
                    ReferenceEquals(entry.Session, current));
            }

            var playingEntry = _entries.FirstOrDefault(entry => IsPlaying(entry.Session));
            if (playingEntry is not null &&
                (selected is null || !IsPlaying(selected.Session)))
            {
                selected = playingEntry;
            }
            selected ??= _entries.FirstOrDefault();

            if (selected is not null)
            {
                SelectEntry(selected);
            }
            else
            {
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                SetSession(null);
            }

            PublishSessions();
            await RefreshMediaPropertiesAsync();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void SelectEntry(SessionEntry entry)
    {
        CancelSessionReconnectGrace();
        _selectedKey = entry.Key;
        _preferredSourceId = entry.SourceId;
        _preferredSourceName = entry.DisplayName;
        SetSession(entry.Session);
    }

    private bool ShouldHoldPreferredSource()
    {
        if (string.IsNullOrWhiteSpace(_preferredSourceId))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!_sessionMissingSinceUtc.HasValue)
        {
            _sessionMissingSinceUtc = now;
            _sessionReconnectCancellation = new CancellationTokenSource();
            _ = RefreshAfterReconnectGraceAsync(_sessionReconnectCancellation.Token);
            return true;
        }

        return now - _sessionMissingSinceUtc.Value < SessionReconnectGracePeriod;
    }

    private void HoldPreferredSource()
    {
        SetSession(null, resetSnapshot: false);
        PublishSessions();
        Publish(_sessionSnapshot with
        {
            IsConnected = true,
            IsPlaying = false,
            CanPlayPause = false,
            CanSkipPrevious = false,
            CanSkipNext = false,
            Artist = _localizer.Get("Main.Media.LoadingArtist"),
            SourceId = _preferredSourceId ?? _sessionSnapshot.SourceId,
            SourceName = _preferredSourceName ?? _sessionSnapshot.SourceName
        });
    }

    private async Task RefreshAfterReconnectGraceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SessionReconnectGracePeriod, cancellationToken);
            if (!_disposed)
            {
                // 后台宽限期刷新也必须隔离 WinRT 异常，避免界面永久停留在加载状态。
                // Grace-period refreshes also isolate WinRT failures so the UI cannot remain stuck loading.
                await RunSessionEventAsync(RefreshSessionListAsync);
            }
        }
        catch (OperationCanceledException)
        {
            // 来源已恢复或用户已改选。 / The source returned or the user changed selection.
        }
    }

    private void CancelSessionReconnectGrace()
    {
        _sessionMissingSinceUtc = null;
        _sessionReconnectCancellation?.Cancel();
        _sessionReconnectCancellation?.Dispose();
        _sessionReconnectCancellation = null;
    }

    private void SetSession(
        GlobalSystemMediaTransportControlsSession? session,
        bool resetSnapshot = true)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        CancelArtworkRefresh();
        _artworkIdentity = null;
        _artworkFingerprint = null;

        if (_session is not null)
        {
            // 始终先退订旧来源，再替换引用，避免旧回调更新当前界面。
            // Detach the old source before replacing it so stale callbacks cannot publish.
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }

        _session = session;
        if (resetSnapshot)
        {
            _sessionSnapshot = MediaSnapshot.Disconnected;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        }
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        await RunSessionEventAsync(() =>
            RefreshMediaPropertiesAsync(refreshArtwork: true));
    }

    private async void OnAnyPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args)
    {
        await RunSessionEventAsync(async () =>
        {
            if (_disposed)
            {
                return;
            }

            SessionEntry? replacement = null;
            var previousSourceId = string.Empty;
            await _sessionGate.WaitAsync();
            try
            {
                if (_disposed)
                {
                    return;
                }

                replacement = FindPlayingReplacement(sender);
                if (replacement is not null)
                {
                    previousSourceId = _lastSnapshot.SourceId;
                    SelectEntry(replacement);
                }

                PublishSessions();
                if (replacement is null && ReferenceEquals(sender, _session))
                {
                    RefreshPlaybackInfo();
                }
            }
            finally
            {
                _sessionGate.Release();
            }

            if (replacement is not null)
            {
                DiagnosticsLogService.Write(
                    "media-session-auto-switch",
                    details: $"From={previousSourceId};To={replacement.SourceId}");
                await RefreshMediaPropertiesAsync();
            }
        });
    }

    private SessionEntry? FindPlayingReplacement(
        GlobalSystemMediaTransportControlsSession eventSession)
    {
        var current = _session;
        if (current is null || IsPlaying(current))
        {
            return null;
        }

        var eventEntry = _entries.FirstOrDefault(entry =>
            !ReferenceEquals(entry.Session, current) &&
            ReferenceEquals(entry.Session, eventSession));
        if (eventEntry is not null && IsPlaying(eventEntry.Session))
        {
            return eventEntry;
        }

        return _entries.FirstOrDefault(entry =>
            !ReferenceEquals(entry.Session, current) &&
            IsPlaying(entry.Session));
    }

    private async Task RunSessionEventAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            if (!_disposed)
            {
                // WinRT 回调是 async void；隔离来源异常，等待下次事件重试。
                // WinRT callbacks are async void; isolate provider failures and retry later.
                try
                {
                    Publish(MediaSnapshot.Disconnected);
                }
                catch
                {
                    // 订阅方异常也不能逃出 async void。 / Subscriber failures stay contained.
                }
            }
        }
    }

    private async Task RefreshMediaPropertiesAsync(bool refreshArtwork = false)
    {
        // 每次读取占用一个版本；完成时只有最新版本可以发布。
        // Each read owns a version and only the newest completion may publish.
        var version = Interlocked.Increment(ref _refreshVersion);
        var session = _session;
        var entry = _entries.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Session, session));
        if (session is null || entry is null)
        {
            Publish(MediaSnapshot.Disconnected);
            return;
        }

        IArtworkImage? transientArtwork = null;
        try
        {
            // 不同播放器填充 GSMTC 字段的时机不同，缺失字段使用稳定回退值。
            // Publishers populate GSMTC fields at different times; use stable fallbacks.
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var title = string.IsNullOrWhiteSpace(mediaProperties.Title)
                ? entry.DisplayName
                : mediaProperties.Title;
            var artist = !string.IsNullOrWhiteSpace(mediaProperties.Artist)
                ? mediaProperties.Artist
                : mediaProperties.AlbumArtist;
            artist = string.IsNullOrWhiteSpace(artist)
                ? _localizer.Get("Main.Media.UnknownArtist")
                : artist;
            var artworkIdentity = BuildArtworkIdentity(
                entry,
                mediaProperties,
                title,
                artist);
            var identityMatches = string.Equals(
                    _artworkIdentity,
                    artworkIdentity,
                    StringComparison.Ordinal);
            var previousFingerprint = _artworkFingerprint;
            var artwork = identityMatches
                ? _sessionSnapshot.Artwork
                : null;
            var publishedFingerprint = artwork is not null
                ? _artworkFingerprint
                : null;
            var needsArtworkSettlement = refreshArtwork || artwork is null;
            var initialArtworkChanged = false;
            if (needsArtworkSettlement)
            {
                ArtworkDecodeResult initialArtwork;
                try
                {
                    // 首次属性读取中的 Thumbnail 可能只在本次 WinRT 快照有效，必须立即消费。
                    // Thumbnail may only be usable from this WinRT snapshot, so consume it immediately.
                    initialArtwork = await LoadArtworkAsync(
                        mediaProperties.Thumbnail,
                        CancellationToken.None);
                }
                catch
                {
                    initialArtwork = default;
                }

                if (initialArtwork.Artwork is not null &&
                    initialArtwork.Fingerprint is not null &&
                    !string.Equals(
                        initialArtwork.Fingerprint,
                        publishedFingerprint,
                        StringComparison.Ordinal) &&
                    (artwork is not null || !string.Equals(
                        initialArtwork.Fingerprint,
                        previousFingerprint,
                        StringComparison.Ordinal)))
                {
                    artwork = initialArtwork.Artwork;
                    transientArtwork = artwork;
                    publishedFingerprint = initialArtwork.Fingerprint;
                    initialArtworkChanged = true;
                }
                else
                {
                    DisposeTransientArtwork(initialArtwork.Artwork);
                }
            }

            if (version != _refreshVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            if (initialArtworkChanged)
            {
                _artworkIdentity = artworkIdentity;
                _artworkFingerprint = publishedFingerprint;
            }

            Publish(CreateSnapshot(session, entry, title, artist, artwork));
            transientArtwork = null;
            if (needsArtworkSettlement && !string.Equals(
                    _pendingArtworkIdentity,
                    artworkIdentity,
                    StringComparison.Ordinal))
            {
                CancelArtworkRefresh();
                _pendingArtworkIdentity = artworkIdentity;
                ScheduleSettledArtworkRefresh(
                    session,
                    entry,
                    artworkIdentity,
                    previousFingerprint,
                    publishedFingerprint);
            }
        }
        catch
        {
            if (version == _refreshVersion && ReferenceEquals(session, _session))
            {
                SetSession(null);
                _selectedKey = null;
                _preferredSourceId = null;
                _preferredSourceName = null;
                Publish(MediaSnapshot.Disconnected);
                _ = RefreshSessionListAsync();
            }
        }
        finally
        {
            DisposeTransientArtwork(transientArtwork);
        }
    }

    private MediaSnapshot CreateSnapshot(
        GlobalSystemMediaTransportControlsSession session,
        SessionEntry entry,
        string title,
        string artist,
        IArtworkImage? artwork)
    {
        var playbackInfo = session.GetPlaybackInfo();
        var controls = playbackInfo.Controls;
        return new MediaSnapshot(
                true,
                playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                title,
                artist,
                entry.SourceId,
                entry.DisplayName,
                artwork,
                null,
                // GSMTC 不推送连续进度，位置由内存播放器（网易云）的 233ms 轮询提供。
                // GSMTC does not push continuous progress; position comes from the memory player poll.
                0);
    }

    private void ScheduleSettledArtworkRefresh(
        GlobalSystemMediaTransportControlsSession session,
        SessionEntry entry,
        string artworkIdentity,
        string? previousFingerprint,
        string? publishedFingerprint)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _artworkRefreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshSettledArtworkAsync(
            session,
            entry,
            artworkIdentity,
            previousFingerprint,
            publishedFingerprint,
            cancellation);
    }

    private async Task RefreshSettledArtworkAsync(
        GlobalSystemMediaTransportControlsSession session,
        SessionEntry entry,
        string artworkIdentity,
        string? previousFingerprint,
        string? publishedFingerprint,
        CancellationTokenSource cancellation)
    {
        try
        {
            // 浏览器可能先返回临时缩略图，再用真实封面替换；整个短窗口都观察指纹变化。
            // Browsers may return a provisional thumbnail before the real artwork; observe fingerprint changes for the full short window.
            for (var attempt = 0; attempt < ArtworkRefreshDelays.Length; attempt++)
            {
                await Task.Delay(ArtworkRefreshDelays[attempt], cancellation.Token);
                var mediaProperties = await session.TryGetMediaPropertiesAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, _session))
                {
                    return;
                }

                var title = string.IsNullOrWhiteSpace(mediaProperties.Title)
                    ? entry.DisplayName
                    : mediaProperties.Title;
                var artist = !string.IsNullOrWhiteSpace(mediaProperties.Artist)
                    ? mediaProperties.Artist
                    : mediaProperties.AlbumArtist;
                artist = string.IsNullOrWhiteSpace(artist)
                    ? _localizer.Get("Main.Media.UnknownArtist")
                    : artist;
                var currentIdentity = BuildArtworkIdentity(
                    entry,
                    mediaProperties,
                    title,
                    artist);
                if (!string.Equals(
                        currentIdentity,
                        artworkIdentity,
                        StringComparison.Ordinal))
                {
                    _ = RefreshMediaPropertiesAsync();
                    return;
                }

                ArtworkDecodeResult artwork = default;
                try
                {
                    try
                    {
                        artwork = await LoadArtworkAsync(
                            mediaProperties.Thumbnail,
                            cancellation.Token);
                    }
                    catch when (attempt < ArtworkRefreshDelays.Length - 1)
                    {
                        continue;
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (artwork.Artwork is null || artwork.Fingerprint is null)
                    {
                        continue;
                    }

                    var differsFromPrevious = !string.Equals(
                        artwork.Fingerprint,
                        previousFingerprint,
                        StringComparison.Ordinal);
                    var differsFromPublished = !string.Equals(
                        artwork.Fingerprint,
                        publishedFingerprint,
                        StringComparison.Ordinal);
                    var showSameCoverFallback = attempt > 0;
                    var isFinalAttempt = attempt == ArtworkRefreshDelays.Length - 1;
                    if (differsFromPublished &&
                        (differsFromPrevious || showSameCoverFallback || isFinalAttempt))
                    {
                        _artworkIdentity = artworkIdentity;
                        _artworkFingerprint = artwork.Fingerprint;
                        publishedFingerprint = artwork.Fingerprint;
                        var publishedArtwork = artwork.Artwork;
                        artwork = default;
                        Publish(CreateSnapshot(
                            session,
                            entry,
                            title,
                            artist,
                            publishedArtwork));
                    }
                }
                finally
                {
                    DisposeTransientArtwork(artwork.Artwork);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 新事件或来源已取代本次封面读取。 / A newer event superseded this artwork read.
        }
        catch
        {
            // 封面不可读时仍保留文字快照。 / Keep the text snapshot when artwork is unreadable.
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _artworkRefreshCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }

            if (string.Equals(
                    _pendingArtworkIdentity,
                    artworkIdentity,
                    StringComparison.Ordinal))
            {
                _pendingArtworkIdentity = null;
            }
        }
    }

    private async Task<ArtworkDecodeResult> LoadArtworkAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return default;
        }

        using var randomAccessStream = await thumbnail.OpenReadAsync();
        var sourceLength = randomAccessStream.Size <= (ulong)long.MaxValue
            ? checked((long)randomAccessStream.Size)
            : (long?)null;
        using var sourceStream = randomAccessStream.AsStreamForRead();
        return await _artworkDecoder.DecodeAsync(
            sourceStream,
            sourceLength,
            cancellationToken);
    }

    private void CancelArtworkRefresh()
    {
        var cancellation = Interlocked.Exchange(ref _artworkRefreshCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        _pendingArtworkIdentity = null;
    }

    private void RefreshPlaybackInfo()
    {
        var session = _session;
        if (session is null || !_sessionSnapshot.IsConnected)
        {
            return;
        }

        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            Publish(_sessionSnapshot with
            {
                IsPlaying = playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = controls.IsPlayPauseToggleEnabled ||
                    controls.IsPlayEnabled ||
                    controls.IsPauseEnabled,
                CanSkipPrevious = controls.IsPreviousEnabled,
                CanSkipNext = controls.IsNextEnabled
            });
        }
        catch
        {
            _ = RefreshSessionListAsync();
        }
    }

    private void PublishSessions()
    {
        var options = _entries
            .Select(entry => new MediaSessionOption(
                entry.Key,
                entry.SourceId,
                entry.DisplayName,
                IsPlaying(entry.Session),
                entry.Key == _selectedKey))
            .ToArray();
        SessionsChanged?.Invoke(options);
    }

    private void Publish(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;

        MediaSnapshot activeSnapshot;
        lock (_memoryPlayerGate)
        {
            activeSnapshot = _memorySnapshot ?? snapshot;
        }

        PublishResolved(activeSnapshot);
    }

    private void PublishResolved(MediaSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        lock (_publishGate)
        {
            if (Equals(_lastSnapshot, snapshot))
            {
                return;
            }

            _lastSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeTransientArtwork(IArtworkImage? artwork)
    {
        if (artwork is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string BuildArtworkIdentity(
        SessionEntry entry,
        GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties,
        string title,
        string artist)
    {
        return string.Join(
            '\u001f',
            entry.Key,
            title,
            artist,
            mediaProperties.AlbumTitle ?? string.Empty,
            mediaProperties.TrackNumber.ToString(CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 先取消后台工作，再退订 session/manager，防止关闭后继续发布快照。
        // Cancel background work before detaching session/manager event subscriptions.
        _disposed = true;
        CancelMemoryPlayerPoll();
        CancelSessionReconnectGrace();
        CancelArtworkRefresh();
        SetSession(null);
        foreach (var entry in _entries)
        {
            entry.Session.PlaybackInfoChanged -= OnAnyPlaybackInfoChanged;
        }

        _entries.Clear();
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnManagerSessionsChanged;
            _manager.SessionsChanged -= OnManagerSessionsChanged;
        }
    }

    private sealed record SessionEntry(
        string Key,
        string SourceId,
        string DisplayName,
        GlobalSystemMediaTransportControlsSession Session);

    private readonly record struct MemoryArtworkCacheEntry(IArtworkImage? Artwork);

    private void StartMemoryPlayerPoll()
    {
        if (_disposed || _memoryPlayerCancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _memoryPlayerCancellation = cancellation;
        _ = PollMemoryPlayersAsync(cancellation, cancellation.Token);
    }

    private async Task PollMemoryPlayersAsync(
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
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

                if (_disposed)
                {
                    return;
                }

                if (playerInfo is { } info)
                {
                    if (ShouldUseMemoryPlayerInfo(info))
                    {
                        PublishMemoryPlayerInfo(info, cancellationToken);
                    }
                    else
                    {
                        PublishMemorySnapshot(null);
                    }
                }
                else
                {
                    PublishMemorySnapshot(null);
                }

                await Task.Delay(MemoryPlayerPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or service disposal stops the polling loop.
        }
        finally
        {
            if (ReferenceEquals(_memoryPlayerCancellation, cancellation))
            {
                _memoryPlayerCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private PlayerInfo? ReadMemoryPlayerInfo()
    {
        if (!User32.GetWindowTitle(
                NetEaseWindowClass,
                out _,
                out var processId))
        {
            ResetMemoryPlayer();
            return null;
        }

        lock (_memoryPlayerGate)
        {
            if (_memoryPlayer is null || !_memoryPlayer.Validate(processId))
            {
                _memoryPlayer = new NetEase(processId);
            }

            return _memoryPlayer.GetPlayerInfo();
        }
    }

    private void ResetMemoryPlayer()
    {
        lock (_memoryPlayerGate)
        {
            _memoryPlayer = null;
            _memoryPlayerInfo = null;
            _memoryPlayerVersion++;
        }
    }

    private bool ShouldUseMemoryPlayerInfo(PlayerInfo playerInfo)
    {
        if (!playerInfo.Pause)
        {
            return true;
        }

        var sessionSnapshot = _sessionSnapshot;
        return !sessionSnapshot.IsConnected ||
            MemoryPlayerControlsMatch(sessionSnapshot.SourceId) ||
            string.Equals(
                sessionSnapshot.SourceName,
                MediaSourceNameFormatter.GetDisplayName(
                    MemoryPlayerSourceId,
                    _localizer.Get("Main.Media.UnknownSource")),
                StringComparison.OrdinalIgnoreCase);
    }

    private void PublishMemoryPlayerInfo(
        PlayerInfo playerInfo,
        CancellationToken cancellationToken)
    {
        playerInfo = playerInfo with
        {
            Cover = NormalizeCoverUrl(playerInfo.Cover)
        };

        MemoryArtworkCacheEntry? cachedArtwork = null;
        LyricsResult? lyrics = null;
        bool shouldDownloadArtwork;
        bool shouldLoadLyrics;

        lock (_memoryPlayerGate)
        {
            var version = _memoryPlayerInfo is { } currentPlayerInfo &&
                string.Equals(
                    currentPlayerInfo.Identity,
                    playerInfo.Identity,
                    StringComparison.Ordinal) &&
                string.Equals(
                    currentPlayerInfo.Cover,
                    playerInfo.Cover,
                    StringComparison.Ordinal)
                    ? _memoryPlayerVersion
                    : ++_memoryPlayerVersion;
            _memoryPlayerInfo = playerInfo;

            var coverUrl = playerInfo.Cover;
            if (_memoryArtworkCache.TryGetValue(coverUrl, out var artwork))
            {
                cachedArtwork = artwork;
                shouldDownloadArtwork = false;
            }
            else
            {
                shouldDownloadArtwork =
                    _pendingMemoryArtworkUrls.Add(coverUrl);
            }

            if (shouldDownloadArtwork)
            {
                _ = LoadMemoryArtworkAsync(
                    coverUrl,
                    version,
                    cancellationToken);
            }

            _lyricsCache.TryGetValue(playerInfo.Identity, out lyrics);
            shouldLoadLyrics = !_lyricsCache.ContainsKey(playerInfo.Identity) &&
                _pendingLyricsIdentities.Add(playerInfo.Identity);
        }

        PublishMemorySnapshot(CreateMemorySnapshot(
            playerInfo,
            cachedArtwork?.Artwork) with
        {
            Lyrics = lyrics
        });

        if (shouldLoadLyrics)
        {
            _ = LoadLyricsAsync(playerInfo, cancellationToken);
        }
    }

    private async Task LoadMemoryArtworkAsync(
        string coverUrl,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
            {
                RememberMemoryArtwork(coverUrl, null);
                return;
            }

            var artwork = await _artworkUriLoader.LoadAsync(
                uri,
                cancellationToken);
            RememberMemoryArtwork(coverUrl, artwork.Artwork);
            if (artwork.Artwork is null)
            {
                return;
            }

            PlayerInfo? currentPlayerInfo;
            LyricsResult? lyrics;
            lock (_memoryPlayerGate)
            {
                currentPlayerInfo = _memoryPlayerInfo;
                if (version != _memoryPlayerVersion ||
                    currentPlayerInfo is not { } info ||
                    !string.Equals(info.Cover, coverUrl, StringComparison.Ordinal))
                {
                    return;
                }

                lyrics = _lyricsCache.TryGetValue(info.Identity, out var cached) ? cached : null;
            }

            PublishMemorySnapshot(CreateMemorySnapshot(
                currentPlayerInfo.Value,
                artwork.Artwork) with
            {
                Lyrics = lyrics
            });
        }
        catch (OperationCanceledException)
        {
            // The service switched tracks or is shutting down.
        }
        catch
        {
            // Cache the miss so an unreachable cover URL is not retried on every poll.
            RememberMemoryArtwork(coverUrl, null);
        }
        finally
        {
            lock (_memoryPlayerGate)
            {
                _pendingMemoryArtworkUrls.Remove(coverUrl);
            }
        }
    }

    private void RememberMemoryArtwork(string coverUrl, IArtworkImage? artwork)
    {
        lock (_memoryPlayerGate)
        {
            if (_memoryArtworkCache.Count >= 12 &&
                !_memoryArtworkCache.ContainsKey(coverUrl))
            {
                var oldestUrl = _memoryArtworkCache.Keys.First();
                _memoryArtworkCache.Remove(oldestUrl);
            }

            _memoryArtworkCache[coverUrl] = new MemoryArtworkCacheEntry(artwork);
        }
    }

    private async Task LoadLyricsAsync(
        PlayerInfo playerInfo,
        CancellationToken cancellationToken)
    {
        var identity = playerInfo.Identity;
        LyricsResult? lyrics;
        try
        {
            var request = new LyricsRequest(
                playerInfo.Title,
                playerInfo.Artists,
                playerInfo.Album,
                playerInfo.Duration > 0 ? playerInfo.Duration : null,
                string.IsNullOrWhiteSpace(identity) ? null : identity);
            lyrics = await _lyricsService.GetLyricsAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_memoryPlayerGate)
            {
                _pendingLyricsIdentities.Remove(identity);
            }

            return;
        }
        catch
        {
            // 拉取失败按未命中缓存，避免每次轮询都重试。 / Cache failures as misses so the poll does not retry every tick.
            lyrics = null;
        }

        MediaSnapshot? updated;
        lock (_memoryPlayerGate)
        {
            _pendingLyricsIdentities.Remove(identity);
            _lyricsCache[identity] = lyrics;

            if (_memoryPlayerInfo is not { } info ||
                !string.Equals(info.Identity, identity, StringComparison.Ordinal) ||
                _memorySnapshot is null)
            {
                return;
            }

            updated = _memorySnapshot with { Lyrics = lyrics };
        }

        PublishMemorySnapshot(updated);
    }

    private MediaSnapshot CreateMemorySnapshot(
        PlayerInfo playerInfo,
        IArtworkImage? artwork)
    {
        var sourceName = MediaSourceNameFormatter.GetDisplayName(
            MemoryPlayerSourceId,
            _localizer.Get("Main.Media.UnknownSource"));
        var controlsAvailable = _sessionSnapshot.IsConnected &&
            (MemoryPlayerControlsMatch(_sessionSnapshot.SourceId) ||
                string.Equals(
                    _sessionSnapshot.SourceName,
                    sourceName,
                    StringComparison.OrdinalIgnoreCase));

        return new MediaSnapshot(
            true,
            !playerInfo.Pause,
            controlsAvailable && _sessionSnapshot.CanPlayPause,
            controlsAvailable && _sessionSnapshot.CanSkipPrevious,
            controlsAvailable && _sessionSnapshot.CanSkipNext,
            playerInfo.Title,
            string.IsNullOrWhiteSpace(playerInfo.Artists)
                ? _localizer.Get("Main.Media.UnknownArtist")
                : playerInfo.Artists,
            MemoryPlayerSourceId,
            sourceName,
            artwork,
            null,
            playerInfo.Schedule);
    }

    private static bool MemoryPlayerControlsMatch(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCoverUrl(string coverUrl) =>
        string.IsNullOrWhiteSpace(coverUrl) ? string.Empty : coverUrl;

    private void PublishMemorySnapshot(MediaSnapshot? snapshot)
    {
        lock (_memoryPlayerGate)
        {
            if (snapshot is null)
            {
                _memoryPlayerInfo = null;
                _memoryPlayerVersion++;
            }

            _memorySnapshot = snapshot;
        }

        PublishResolved(snapshot ?? _sessionSnapshot);
    }

    private void CancelMemoryPlayerPoll()
    {
        _memoryPlayerCancellation?.Cancel();
    }

}
