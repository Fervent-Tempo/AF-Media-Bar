using AFMediaBar.Classes.Services.Media.Smtc;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using Windows.Media.Control;
using Windows.Media;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 协调媒体会话目录、来源选择、快照构建和来源扩展，并向 ViewModel 发布统一状态。
/// Coordinates the session catalog, source selection, snapshot building, and source enrichers,
/// publishing a unified state to ViewModels.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly MediaSourceRegistry _sources;
    private readonly MediaSessionCatalog _catalog;
    private readonly ISystemNotificationService _notifications;
    private string? _lastControlNotificationSource;
    private DateTimeOffset _lastControlNotificationUtc;
    private string? _missingControlSourceId;
    private readonly MediaSessionSelectionService _selection;
    private readonly MediaSnapshotBuilder _snapshotBuilder;
    private readonly IReadOnlyList<IMediaSourceProvider> _sourceProviders;
    private readonly MediaSourceActivationService _sourceActivator;
    private readonly Dispatcher _dispatcher;
    private readonly object _publishGate = new();
    private IReadOnlyList<MediaSessionOption> _lastSessionOptions = Array.Empty<MediaSessionOption>();
    private IReadOnlyList<MediaSourceDescriptor> _lastDiscoveredSources = Array.Empty<MediaSourceDescriptor>();
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private readonly Dictionary<IMediaSourceProvider, MediaSnapshot?> _sourceSnapshots = new();
    private MediaSnapshot _lastSnapshot = MediaSnapshot.Disconnected;
    private IReadOnlyList<MediaSession> _lastSessions = Array.Empty<MediaSession>();
    private IReadOnlyList<MediaSourceCandidate> _smtcCandidates = Array.Empty<MediaSourceCandidate>();
    private IReadOnlyList<MediaSourceCandidate> _candidates = Array.Empty<MediaSourceCandidate>();
    private string? _selectedSessionKey;
    private string? _controlSelectionKey;
    private int _controlSelectionRevision;
    private bool _snapshotBuildInFlight;
    private bool _snapshotBuildPending;
    private int _refreshQueued;
    private readonly CancellationTokenSource _snapshotBuildCancellation = new();
    private readonly BrowserMissingPresentationState _browserPresentation = new();
    private DispatcherTimer? _browserPresentationTimer;
    private EventHandler? _browserPresentationTimerTick;
    private int _mediaRevision;
    // 释放标记与 generation 会被后台流程读取（取消/过期判定），因此声明为 volatile：写入在 UI 线程，读取在后台线程。
    // The disposal flag and the generation are read by the background flow (cancellation and staleness checks), so both are volatile:
    // written on the UI thread and read on the background thread.
    private volatile bool _isDisposed;
    private readonly MemoryPruneCoordinator _memoryPrune;
    private readonly MediaSessionReconcileCoordinator _reconcile;

    /// <summary>
    /// 已经请求过在线取词兜底的那一曲（来源 + 曲名 + 歌手的指纹），只保留最后一个。
    /// 来源提供器每 233 毫秒都会报一次"没有可读的媒体"，没有这个门闩就会每 233 毫秒发起一次兜底取词；
    /// 只留一个是刻意的：它只需要挡住"同一首歌的重复请求"，换歌就该重新允许，而无界集合会随播放一直增长。
    /// The track (a fingerprint of source, title, and artist) whose online-lyric fallback has already been requested, keeping only the last one.
    /// A source provider reports "no readable media" every 233 ms, so without this latch a fallback would be requested every 233 ms; keeping
    /// exactly one is deliberate: it only has to block repeats for the same track, a track change must allow a new one, and an unbounded set
    /// would grow for as long as playback lasts.
    /// </summary>

    /// <summary>最近一次写进日志的快照指纹；相同则不再重复记录（时间戳每次轮询都变，否则会刷屏）。/ Signature of the last logged snapshot; an identical one is not logged again, since the timestamp changes on every poll.</summary>
    private string? _loggedSnapshotSignature;

    /// <summary>最近一次写进日志的播放位置（秒），用于播放中每秒最多记一条进度。/ Playback position of the last logged line, in seconds, used to write at most one progress line per second while playing.</summary>
    private double _loggedPositionSeconds;

    /// <summary>播放中记录进度所需的最小位置变化（秒）。/ Smallest position change, in seconds, that earns a progress line while playing.</summary>
    private const double PositionLogStepSeconds = 1;

    /// <summary>最新的媒体快照。 / Latest published media snapshot.</summary>
    public MediaSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>在 UI 线程上触发。 / Raised on the UI thread.</summary>
    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    /// <summary>会话列表变化时在 UI 线程触发。 / Raised on the UI thread when the session list changes.</summary>
    public event Action<IReadOnlyList<MediaSessionOption>>? SessionsChanged;
    public event Action<IReadOnlyList<MediaSourceDescriptor>>? DiscoveredSourcesChanged;

    public string SelectedSourceId => _lastSnapshot.SourceId;
    public string SelectedSourceName => _lastSnapshot.SourceName;
    public IReadOnlyList<MediaSessionOption> CurrentSessionOptions => _lastSessionOptions;
    public IReadOnlyList<MediaSourceDescriptor> CurrentDiscoveredSources => _lastDiscoveredSources;

    /// <summary>
    /// 创建媒体协调器并接管目录、选择器和来源提供器的事件订阅；释放本服务时会按相反顺序解除订阅。
    /// Creates the media coordinator and owns subscriptions to the catalog, selector, and source providers; disposal removes them in reverse ownership order.
    /// </summary>
    public MediaSessionService(
        MediaSourceRegistry sources,
        MediaSessionCatalog catalog,
        MediaSessionSelectionService selection,
        MediaSnapshotBuilder snapshotBuilder,
        IEnumerable<IMediaSourceProvider> sourceProviders,
        MediaSourceActivationService sourceActivator,
        MemoryPruneCoordinator memoryPrune,
        ISystemNotificationService notifications)
    {
        _sources = sources;
        _catalog = catalog;
        _selection = selection;
        _snapshotBuilder = snapshotBuilder;
        _sourceProviders = sourceProviders.ToArray();
        _sourceActivator = sourceActivator;
        _memoryPrune = memoryPrune;
        _notifications = notifications;
        _dispatcher = Application.Current.Dispatcher;
        _reconcile = new MediaSessionReconcileCoordinator(_catalog, _selection, _memoryPrune,
            _dispatcher, () => _sessionSnapshot.IsConnected, () => _snapshotBuildInFlight, RefreshSnapshot);

        _catalog.AnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        _catalog.AnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        _catalog.AnySessionOpened += OnAnySessionOpened;
        _catalog.AnySessionClosed += OnAnySessionClosed;
        _catalog.FocusedSessionChanged += OnFocusedSessionChanged;
        _catalog.AnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
        _selection.RefreshRequested += OnSelectionRefreshRequested;
        _snapshotBuilder.EnrichmentCompleted += OnSnapshotEnrichmentCompleted;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        foreach (var provider in _sourceProviders)
        {
            provider.SnapshotChanged += OnSourceSnapshotChanged;
            provider.Start();
        }

        // 看门狗挂在 UI 线程上，但优先级取 Background：它的活是"补一次系统查询"，永远不该和输入或渲染抢时序。
        // The watchdog lives on the UI thread at Background priority: its job is one extra system query and must never race input or rendering.
        _reconcile.Start();

        ScheduleSessionsRefresh();
    }

    /// <summary>
    /// 使用当前稳定会话目录立即重建并发布快照。
    /// Immediately rebuilds and publishes a snapshot from the current stable session catalog.
    /// </summary>
    public void RefreshNow() => RefreshSnapshot();

    /// <summary>
    /// 立即请求一次重同步（跳过看门狗的时间门控），并在结果发布完成后返回。
    /// 探测与恢复在后台 single-flight 流程执行，UI 线程不再被第三方库调用阻塞；已有流程在飞时返回该流程的完成。
    /// Requests a reconcile immediately (bypassing the watchdog's time gate) and completes after the result has been published.
    /// Probing and recovery run on the background single-flight flow, so the UI thread is never blocked by the third-party library;
    /// when a flow is already in flight its completion is returned.
    /// </summary>
    /// <remarks>必须在 UI 线程调用（命令入口即如此）。/ Must be called on the UI thread, which is how the command path uses it.</remarks>
    public Task ReconnectAsync()
    {
        return _reconcile.ReconnectAsync();
    }

    /// <summary>
    /// 将有效会话键设为手动选择，并基于同一目录快照重新发布列表和媒体状态。
    /// Selects a valid session key manually and republishes the list and media state from the same catalog snapshot.
    /// </summary>
    public void SelectSession(string key)
    {
        if (_isDisposed || string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!_selection.Select(key, _candidates))
        {
            return;
        }

        RefreshSnapshot();
    }

    /// <summary>
    /// 将播放/暂停请求转发给当前选中的 SMTC 会话；会话已消失时安全忽略。
    /// Forwards play/pause to the selected SMTC session and safely ignores the request if that session has disappeared.
    /// </summary>
    public Task TogglePlayPauseAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        return controlSession.GetPlaybackInfo().Controls?.IsPlayPauseToggleEnabled == true &&
            await controlSession.TryTogglePlayPauseAsync();
    });

    /// <summary>
    /// 将上一首请求转发给当前仍有效的选中会话。
    /// Forwards the previous-track request to the selected session while it remains valid.
    /// </summary>
    public Task SkipPreviousAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        return await controlSession.TrySkipPreviousAsync();
    });

    /// <summary>
    /// 将下一首请求转发给当前仍有效的选中会话。
    /// Forwards the next-track request to the selected session while it remains valid.
    /// </summary>
    public Task SkipNextAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        return await controlSession.TrySkipNextAsync();
    });

    /// <summary>跳转到当前媒体的相对播放位置。 / Seeks to a relative position in the selected media item.</summary>
    public Task SeekAsync(double positionSeconds) => ExecuteOnSelectedAsync(async controlSession =>
    {
        var controls = controlSession.GetPlaybackInfo().Controls;
        var timeline = controlSession.GetTimelineProperties();
        var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        if (duration <= 0 || controls?.IsPlaybackPositionEnabled != true)
        {
            return false;
        }

        var target = timeline.StartTime + TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, duration));
        return await controlSession.TryChangePlaybackPositionAsync(target.Ticks);
    });

    /// <summary>在关闭、列表和单曲循环之间切换。 / Cycles repeat between off, list, and track.</summary>
    public Task CycleRepeatModeAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        var playback = controlSession.GetPlaybackInfo();
        if (playback.Controls?.IsRepeatEnabled != true)
        {
            return false;
        }

        var next = playback.AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.None,
            _ => MediaPlaybackAutoRepeatMode.List
        };
        return await controlSession.TryChangeAutoRepeatModeAsync(next);
    });

    /// <summary>
    /// 尝试激活当前媒体来源对应的前台窗口，不存在可用进程时保持无操作。
    /// Attempts to activate the foreground window for the selected media source and becomes a no-op when no process is available.
    /// </summary>
    public void ActivateSelectedSource() => _sourceActivator.Activate(SelectedSourceId);

    /// <summary>
    /// 幂等停止来源提供器并解除所有目录、选择和补全事件，阻止释放后继续发布状态。
    /// Idempotently stops source providers and removes catalog, selection, and enrichment subscriptions so no state is published after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _snapshotBuildCancellation.Cancel();
        _snapshotBuildCancellation.Dispose();
        CancelBrowserPresentationHold();
        _reconcile.Dispose();
        _catalog.AnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        _catalog.AnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        _catalog.AnySessionOpened -= OnAnySessionOpened;
        _catalog.AnySessionClosed -= OnAnySessionClosed;
        _catalog.FocusedSessionChanged -= OnFocusedSessionChanged;
        _catalog.AnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
        _selection.RefreshRequested -= OnSelectionRefreshRequested;
        _snapshotBuilder.EnrichmentCompleted -= OnSnapshotEnrichmentCompleted;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        foreach (var provider in _sourceProviders)
        {
            provider.SnapshotChanged -= OnSourceSnapshotChanged;
            provider.Dispose();
        }
        _selection.Dispose();
        _catalog.Dispose();
    }

    private async Task ExecuteOnSelectedAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        if (_isDisposed) return;
        var sourceId = SelectedSourceId;
        var selectionRevision = _controlSelectionRevision;
        if (!_catalog.TryGetSnapshot(out var sessions))
        {
            ReportControlUnavailable(sourceId);
            return;
        }

        var selected = sessions.FirstOrDefault(session =>
            MediaSessionGuard.IsUsable(session) &&
            _sources.IsAllowed(MediaSessionGuard.GetSourceId(session), SettingsManager.Current.SmtcSourceFilter) &&
            string.Equals(session.Id, _selectedSessionKey, StringComparison.Ordinal) &&
            string.Equals(_sources.NormalizeSourceId(MediaSessionGuard.GetSourceId(session)),
                _sources.NormalizeSourceId(_selection.SelectedSourceId ?? string.Empty), StringComparison.OrdinalIgnoreCase));

        // 会话可能刚被第三方库关闭；此处只取一次引用，后续命令交给该引用，避免读取过程中属性被置空。
        // The session may have just been closed by the third-party library; capture the reference once and issue commands
        // through that local so a concurrently cleared property cannot turn the call into a null reference.
        var controlSession = selected?.ControlSession;
        if (controlSession is null)
        {
            ReportControlUnavailable(sourceId);
            return;
        }

        try
        {
            var accepted = await Task.Run(() => action(controlSession));
            if (!accepted && selectionRevision == _controlSelectionRevision)
                ReportControlUnavailable(sourceId);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine($"[MediaSessionService] Media command failed: {ex}");
            if (selectionRevision == _controlSelectionRevision)
                ReportControlUnavailable(sourceId);
        }
    }

    private void ReportControlUnavailable(string sourceId)
    {
        if (_isDisposed || !_lastSnapshot.IsConnected ||
            !_sources.IsAllowed(sourceId, SettingsManager.Current.SmtcSourceFilter) ||
            !string.Equals(sourceId, SelectedSourceId, StringComparison.OrdinalIgnoreCase)) return;
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(sourceId, _lastControlNotificationSource, StringComparison.OrdinalIgnoreCase) &&
            now - _lastControlNotificationUtc < TimeSpan.FromSeconds(30)) return;
        _lastControlNotificationSource = sourceId;
        _lastControlNotificationUtc = now;
        try
        {
            _notifications.TryShowNotification(Translations.Get("Media.ControlUnavailable.Title"),
                Translations.Get("Media.ControlUnavailable.Body"));
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Media", "媒体控制失败通知发送失败", exception);
        }
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        Interlocked.Increment(ref _mediaRevision);
        ScheduleRefresh();
    }

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => ScheduleSessionsRefresh();

    private void OnAnySessionOpened(MediaSession session)
    {
        Interlocked.Increment(ref _mediaRevision);
        ScheduleSessionsRefresh();
    }

    private void OnAnySessionClosed(MediaSession session)
    {
        Interlocked.Increment(ref _mediaRevision);
        // 会话关闭是"换歌重建会话"的信号，也正是第三方库漏事件的高发点：武装快速窗口，让看门狗用 1 秒的节奏把新会话捞回来。
        // A session closing signals a track change and is exactly where the third-party library loses events: arm the fast window so the
        // watchdog catches the recreated session on a one-second cadence.
        _reconcile.Arm();
        ScheduleSessionsRefresh();
    }

    private void OnFocusedSessionChanged(MediaSession session) => ScheduleSessionsRefresh();

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties properties) => ScheduleRefresh();

    private void OnSelectionRefreshRequested() => ScheduleSessionsRefresh();

    private void OnSnapshotEnrichmentCompleted() => ScheduleRefresh();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.SmtcSourceFilter) && e.PropertyName != nameof(AppSettings.MediaSelection) &&
            e.ResetScope is not SettingsResetScope.ExtraFeatures and not SettingsResetScope.All)
            return;
        _selection.ClearSelection();
        _controlSelectionRevision++;
        CancelBrowserPresentationHold();
        ScheduleSessionsRefresh();
    }

    private void OnSourceSnapshotChanged(IMediaSourceProvider provider, MediaSnapshot? snapshot)
    {
        if (_isDisposed)
            return;
        _sourceSnapshots[provider] = snapshot;
        // 当前选择的播放状态先发布，不能等候选刷新或在途 SMTC 补全结束。
        PublishResolved(ResolveSnapshot(_sessionSnapshot));
        if (!_candidates.SequenceEqual(BuildCandidates()))
            RefreshSnapshot(_lastSessions);

        // 提供器可能每 233 毫秒报告读取失败；快照构建器负责在途去重和失败冷却，允许网络恢复后重试。
        if (snapshot is null)
        {
            TryRequestFallbackLyrics();
        }
    }

    /// <summary>
    /// 在来源提供器读不出媒体时为当前 SMTC 会话发起一次在线取词兜底。
    ///
    /// 判据是"提供器已经明确报过它没有可读的媒体"（<see cref="MediaEnrichmentFallbackPolicy"/>）：提供器还没发布过任何
    /// 快照时它只是还没跑完第一轮轮询，那时兜底会在每次切歌的头 233 毫秒里白发一次请求。
    /// 重复请求由歌词缓存与在途去重挡住，临时失败在冷却后允许重试。
    /// Starts one online-lyric fallback for the current SMTC session when its source provider cannot read any media.
    ///
    /// The condition is that the provider has already reported having no readable media (<see cref="MediaEnrichmentFallbackPolicy"/>): before
    /// it has published anything it has merely not finished its first poll, and a fallback then would waste one request during the first
    /// 233 ms of every track change. The lyric cache and in-flight lookup set block repeats while allowing failed requests to retry after a cooldown.
    /// </summary>
    private void TryRequestFallbackLyrics()
    {
        var baseline = _sessionSnapshot;
        var provider = baseline.IsConnected
            ? _sourceProviders.FirstOrDefault(candidate => candidate.CanHandle(baseline.SourceId))
            : null;
        var providerPublishedNothing = provider is not null &&
                                       _sourceSnapshots.TryGetValue(provider, out var published) &&
                                       published is null;
        if (!MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(
                baseline.IsConnected,
                provider is not null,
                providerPublishedNothing,
                baseline.Title))
        {
            return;
        }

        // 会话标识必须是选中的那一个：歌词缓存键由它构成，兜底取回的结果要靠同一个键才会被下一次 Build 认领。
        // The session identifier has to be the selected one: it makes up the lyric cache key, and only the same key lets the next Build
        // claim the result the fallback fetched.
        var sessionKey = _selectedSessionKey;
        if (string.IsNullOrEmpty(sessionKey))
        {
            return;
        }

        _snapshotBuilder.RequestOnlineLyrics(
            sessionKey,
            baseline.SourceId,
            baseline.Title,
            baseline.Artist,
            baseline.Duration > 0 ? baseline.Duration : null);
    }

    private void ScheduleRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed ||
            Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            RefreshSnapshot();
        }, DispatcherPriority.Normal);
    }

    private void ScheduleSessionsRefresh() => ScheduleRefresh();

    private void RefreshSnapshot()
    {
        if (_isDisposed)
            return;
        // SMTC 暂时不可用不能阻断独立内存来源；旧会话仍需通过有效性检查。
        // An unavailable SMTC catalog must not block the independent memory source.
        try
        {
            var sessions = _catalog.TryGetSnapshot(out var current) ? current : _lastSessions;
            PublishDiscoveredSources(sessions);
            RefreshSnapshot(sessions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh source catalog: {ex}");
        }
    }

    private void RefreshSnapshot(IReadOnlyList<MediaSession> sessions)
    {
        if (_isDisposed)
            return;
        try
        {
            _lastSessions = sessions.Where(MediaSessionGuard.IsUsable).ToArray();
            _smtcCandidates = _lastSessions.Select(session => new MediaSourceCandidate(
                session.Id, MediaSessionGuard.GetSourceId(session), IsPlaying(session), session.Id)).ToArray();
            _candidates = BuildCandidates();
            var selected = _selection.Resolve(_candidates, SettingsManager.Current.MediaSelection);
            if (selected is null && _selection.IsMissingSessionGraceActive)
            {
                BeginBrowserPresentationHold();
                return;
            }

            CancelBrowserPresentationHold();

            if (!string.Equals(_controlSelectionKey, selected?.Key, StringComparison.Ordinal) ||
                !string.Equals(_selectedSessionKey, selected?.SessionKey, StringComparison.Ordinal))
            {
                _controlSelectionKey = selected?.Key;
                _controlSelectionRevision++;
            }
            _selectedSessionKey = selected?.SessionKey;
            var session = _lastSessions.FirstOrDefault(item => item.Id == _selectedSessionKey);
            PublishSessions(_candidates);
            if (session is null || !_catalog.IsStarted)
            {
                Publish(MediaSnapshot.Disconnected);
                TryRequestFallbackLyrics();
                return;
            }

            if (_snapshotBuildInFlight)
            {
                _snapshotBuildPending = true;
                return;
            }

            _snapshotBuildInFlight = true;
            var selectionRevision = _controlSelectionRevision;
            var mediaRevision = Volatile.Read(ref _mediaRevision);
            var pendingSnapshot = _snapshotBuilder.BuildAsync(session, isStarted: true, _snapshotBuildCancellation.Token);
            _ = BuildAndPublishSnapshotAsync(pendingSnapshot, session.Id, mediaRevision, selectionRevision);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh snapshot: {ex}");
        }
    }

    private async Task BuildAndPublishSnapshotAsync(Task<MediaSnapshot?> pendingSnapshot, string sessionKey, int mediaRevision, int selectionRevision)
    {
        try
        {
            var snapshot = await pendingSnapshot;
            if (_isDisposed || selectionRevision != _controlSelectionRevision || mediaRevision != Volatile.Read(ref _mediaRevision) ||
                !string.Equals(sessionKey, _selectedSessionKey, StringComparison.Ordinal) ||
                _selection.IsMissingSessionGraceActive)
                return;

            if (snapshot is not null)
            {
                Publish(snapshot);
                TryRequestFallbackLyrics();
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            // Shutdown superseded the pending player read.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to build snapshot: {exception}");
        }
        finally
        {
            _snapshotBuildInFlight = false;
            if (!_isDisposed && _snapshotBuildPending)
            {
                _snapshotBuildPending = false;
                RefreshSnapshot(_lastSessions);
            }
            if (!_isDisposed) _reconcile.SnapshotSettled();
        }
    }

    private void PublishSessions(IReadOnlyList<MediaSourceCandidate> sessions)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = sessions.Select(session =>
        {
            var sourceId = session.SourceId;
            occurrences.TryGetValue(sourceId, out var occurrence);
            occurrence++;
            occurrences[sourceId] = occurrence;
            var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, Translations.Get("Service.MediaSource.Unknown"));
            if (occurrence > 1)
            {
                displayName = $"{displayName} ({occurrence})";
            }

            return new MediaSessionOption(
                session.Key,
                sourceId,
                displayName,
                session.IsPlaying,
                string.Equals(session.Key, _selection.SelectedKey, StringComparison.Ordinal));
        }).ToArray();

        if (options.SequenceEqual(_lastSessionOptions))
        {
            return;
        }

        _lastSessionOptions = options;
        SessionsChanged?.Invoke(options);
    }

    private IReadOnlyList<MediaSourceCandidate> BuildCandidates() =>
        _sources.BuildCandidates(_smtcCandidates, _sourceSnapshots)
            .Where(source => _sources.IsAllowed(source.SourceId, SettingsManager.Current.SmtcSourceFilter))
            .ToArray();

    private void BeginBrowserPresentationHold()
    {
        if (_browserPresentationTimer is not null || _browserPresentation.IsHidden ||
            _selection.SelectedKey is not { } selectedKey)
            return;

        // A read started for the now-missing session must not publish its old frame during the hold.
        Interlocked.Increment(ref _mediaRevision);
        var generation = _browserPresentation.Begin();
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = BrowserMissingPresentationState.HoldDuration
        };
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            if (ReferenceEquals(_browserPresentationTimer, timer))
            {
                _browserPresentationTimer = null;
                _browserPresentationTimerTick = null;
            }

            var stillMissing = !_isDisposed && _selection.IsMissingSessionGraceActive &&
                string.Equals(selectedKey, _selection.SelectedKey, StringComparison.Ordinal) &&
                !_candidates.Any(candidate => string.Equals(candidate.Key, selectedKey, StringComparison.Ordinal));
            if (_browserPresentation.TryHide(generation, stillMissing))
            {
                try
                {
                    PublishResolved(MediaSnapshot.Disconnected);
                }
                catch (Exception ex)
                {
                    // A subscriber failure must not escape a DispatcherTimer tick and crash the host.
                    Debug.WriteLine($"[MediaSessionService] Failed to publish missing browser presentation: {ex}");
                }
            }
        };
        _browserPresentationTimer = timer;
        _browserPresentationTimerTick = tick;
        timer.Tick += tick;
        timer.Start();
    }

    private void CancelBrowserPresentationHold()
    {
        if (_browserPresentationTimer is not { } timer)
            return;

        _browserPresentation.CancelPending();
        timer.Stop();
        if (_browserPresentationTimerTick is { } tick)
            timer.Tick -= tick;
        _browserPresentationTimer = null;
        _browserPresentationTimerTick = null;
    }

    private void PublishDiscoveredSources(IReadOnlyList<MediaSession> sessions)
    {
        var sources = _sources.DiscoverSourceIds(sessions.Select(MediaSessionGuard.GetSourceId))
            .Select(_sourceActivator.Describe)
            .OrderBy(source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (sources.SequenceEqual(_lastDiscoveredSources))
            return;
        _lastDiscoveredSources = sources;
        DiscoveredSourcesChanged?.Invoke(sources);
    }

    private void Publish(MediaSnapshot snapshot)
    {
        // Only a newly resolved selection (or the final disconnect) may end the presentation hold.
        // Provider updates still pass through PublishResolved and cannot revive the old browser frame.
        _browserPresentation.Complete();
        _sessionSnapshot = snapshot;
        if (snapshot.IsConnected)
        {
            // 恢复连接即视为"事件链路已自愈"：解除快速窗口并清退避，让下一次失明从最灵敏的节奏重新开始。
            // A connected snapshot means the event path healed: disarm the fast window and clear the backoff so the next blindness
            // starts from the most responsive cadence again.
            _reconcile.SnapshotConnected();
        }

        foreach (var provider in _sourceProviders)
        {
            provider.UpdateSessionSnapshot(snapshot);
        }
        PublishResolved(ResolveSnapshot(snapshot));
    }

    private MediaSnapshot ResolveSnapshot(MediaSnapshot snapshot)
    {
        var independent = _sources.ResolveSnapshot(
            _selection.SelectedKey, snapshot, _sourceSnapshots, SettingsManager.Current.SmtcSourceFilter);
        if (independent is not null)
            return independent;
        if (snapshot.IsConnected && !_sources.IsAllowed(
                snapshot.SourceId, SettingsManager.Current.SmtcSourceFilter))
            return MediaSnapshot.Disconnected;

        // 普通提供器只补全选中的 SMTC 来源；独立来源已通过统一来源选择。
        // Ordinary providers enrich only their selected SMTC source; independent sources resolve through selection.
        var provider = snapshot.IsConnected
            ? _sourceProviders.FirstOrDefault(candidate => candidate.CanHandle(snapshot.SourceId))
            : null;
        var providerSnapshot = provider is not null ? _sourceSnapshots.GetValueOrDefault(provider) : null;
        if (providerSnapshot is null)
            return snapshot;

        return providerSnapshot with
        {
            CanPlayPause = snapshot.CanPlayPause,
            CanSkipPrevious = snapshot.CanSkipPrevious,
            CanSkipNext = snapshot.CanSkipNext,
            CanSeek = snapshot.CanSeek && providerSnapshot.Duration > 0,
            CanChangeRepeat = snapshot.CanChangeRepeat,
            RepeatMode = snapshot.RepeatMode,
            PlaybackRate = snapshot.PlaybackRate
        };
    }

    private void PublishResolved(MediaSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        snapshot = _browserPresentation.Project(snapshot);
        // 单项控制能力不代表 SMTC 会话缺失；同一来源连续缺少控制通道时只主动提示一次。
        var missingControlSourceId = snapshot.IsConnected && string.IsNullOrEmpty(_selectedSessionKey)
            ? snapshot.SourceId
            : null;
        var notifyUnavailable = missingControlSourceId is not null &&
            !string.Equals(missingControlSourceId, _missingControlSourceId, StringComparison.OrdinalIgnoreCase);
        _missingControlSourceId = missingControlSourceId;

        lock (_publishGate)
        {
            if (!Equals(_lastSnapshot, snapshot))
            {
                _lastSnapshot = snapshot;
                CurrentSnapshot = snapshot;
                LogSnapshot(snapshot);
                SnapshotChanged?.Invoke(this, snapshot);
            }
        }
        if (notifyUnavailable)
            ReportControlUnavailable(snapshot.SourceId);
    }

    /// <summary>
    /// 快照的"有意义变化"指纹：不含时间戳与播放位置。
    ///
    /// 播放器每次轮询都会刷新时间轴时间戳（`TimelineUpdatedAt`），因此暂停时快照也会每 240 毫秒重新发布一次；
    /// 若按发布记录日志，文件与调试输出会被同一行"刷屏"，反而看不清真正发生了什么。轨道、播放状态、控制能力、
    /// 时长、循环、倍速与歌词来源变化才记一行；播放中另外按每秒最多一行记录进度（只在调试输出里）。
    /// Fingerprint of a snapshot's meaningful change, without the timestamp or the playback position.
    ///
    /// A player refreshes the timeline timestamp (`TimelineUpdatedAt`) on every poll, so even a paused track republishes a snapshot every
    /// 240 ms; logging per publication would flood the file and the debug output with the same line and hide what actually happened. Only a
    /// change of track, playback state, control availability, duration, repeat mode, playback rate, or lyric source gets a line, and while
    /// playing at most one progress line per second, which goes to the debug output only.
    /// </summary>
    /// <param name="snapshot">快照。/ The snapshot.</param>
    private static string BuildLogSignature(MediaSnapshot snapshot) =>
        $"{snapshot.SourceId}\u001f{snapshot.SourceName}\u001f{snapshot.Title}\u001f{snapshot.Artist}" +
        $"\u001f{snapshot.IsConnected}\u001f{snapshot.IsPlaying}\u001f{snapshot.Duration:0}" +
        $"\u001f{snapshot.CanSeek}\u001f{snapshot.CanSkipPrevious}\u001f{snapshot.CanSkipNext}" +
        $"\u001f{snapshot.RepeatMode}\u001f{snapshot.PlaybackRate:0.##}\u001f{snapshot.Lyrics?.Source}";

    /// <summary>按上面的规则记录一次快照（真正变化才写，播放中每秒最多一条进度）。/ Logs a snapshot by the rule above: only a real change is written, plus at most one progress line per second while playing.</summary>
    /// <param name="snapshot">快照。/ The snapshot.</param>
    private void LogSnapshot(MediaSnapshot snapshot)
    {
        var log = AppLogService.Current;
        if (log is null)
        {
            return;
        }

        var signature = BuildLogSignature(snapshot);
        if (!string.Equals(signature, _loggedSnapshotSignature, StringComparison.Ordinal))
        {
            _loggedSnapshotSignature = signature;
            _loggedPositionSeconds = snapshot.Position;
            log.Info(
                "Media",
                $"快照变化: {snapshot.SourceName}({snapshot.SourceId}) " +
                $"\"{snapshot.Title}\" — \"{snapshot.Artist}\" " +
                $"connected={snapshot.IsConnected} playing={snapshot.IsPlaying} " +
                $"pos={snapshot.Position:0.0}/{snapshot.Duration:0.0} rate={snapshot.PlaybackRate:0.##} " +
                $"lyrics={(snapshot.Lyrics is null ? "none" : snapshot.Lyrics.Source)}");
            log.Verbose(
                "Media",
                $"快照细节: seek={snapshot.CanSeek} prev={snapshot.CanSkipPrevious} next={snapshot.CanSkipNext} " +
                $"repeat={snapshot.RepeatMode} timelineAt={snapshot.TimelineUpdatedAt:HH:mm:ss.fff}");
            return;
        }

        // 播放中按每秒最多一条记录进度：暂停或卡住时位置不动，因此这里什么都不写——那正是"日志一直刷同一行"的来源。
        // While playing at most one progress line per second; a paused or stalled track never moves the position, so nothing is written here,
        // which is exactly what used to flood the log with the same line.
        if (!snapshot.IsPlaying || Math.Abs(snapshot.Position - _loggedPositionSeconds) < PositionLogStepSeconds)
        {
            return;
        }

        _loggedPositionSeconds = snapshot.Position;
        // log.Verbose("Media", $"进度 / position: {snapshot.Position:0.0}/{snapshot.Duration:0.0}");
    }

    private static bool IsPlaying(MediaSession session)
    {
        // 会话可能在本方法执行期间被第三方库关闭，读取失败一律按“未播放”处理。
        // The third-party library may close the session while this method runs, so every read failure means "not playing".
        if (!MediaSessionGuard.IsUsable(session))
        {
            return false;
        }

        try
        {
            return session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }
}
