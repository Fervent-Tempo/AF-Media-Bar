using AFMediaBar.Classes.Services.Media.Smtc;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
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
    private string[] _discoverySourceIds = [];
    private int _discoveryRevision;
    private bool _discoveryInFlight;
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

    // 只用于诊断去重；请求预算与缓存仍由快照构建器持有。
    private string? _fallbackLogKey;
    private RepeatedFailureReporter? _readFailures;
    private readonly AppLogService? _log;

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
        ISystemNotificationService notifications, AppLogService? log = null)
    {
        _sources = sources;
        _catalog = catalog;
        _selection = selection;
        _snapshotBuilder = snapshotBuilder;
        _sourceProviders = sourceProviders.ToArray();
        _sourceActivator = sourceActivator;
        _memoryPrune = memoryPrune;
        _notifications = notifications;
        _log = log;
        _dispatcher = Application.Current.Dispatcher;
        _reconcile = new MediaSessionReconcileCoordinator(_catalog, _selection, _memoryPrune,
            _dispatcher, () => _sessionSnapshot.IsConnected, () => _snapshotBuildInFlight, RefreshSnapshot, log);

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
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action, [CallerMemberName] string operation = "")
    {
        if (_isDisposed) return;
        var sourceId = SelectedSourceId;
        var selectionRevision = _controlSelectionRevision;
        if (!_catalog.TryGetSnapshot(out var sessions))
        {
            ReportControlUnavailable(sourceId, operation);
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
            ReportControlUnavailable(sourceId, operation);
            return;
        }

        try
        {
            var accepted = await Task.Run(() => action(controlSession));
            if (!accepted && selectionRevision == _controlSelectionRevision)
                ReportControlUnavailable(sourceId, operation);
            else if (accepted && selectionRevision == _controlSelectionRevision)
                (_readFailures ??= new()).Recover(_log, "Media", $"control:{operation}:{sourceId}", "媒体控制已恢复");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            if (selectionRevision == _controlSelectionRevision)
                ReportControlUnavailable(sourceId, operation, ex);
        }
    }

    private void ReportControlUnavailable(string sourceId, string operation = "control-channel", Exception? controlException = null)
    {
        if (_isDisposed || !_lastSnapshot.IsConnected ||
            !_sources.IsAllowed(sourceId, SettingsManager.Current.SmtcSourceFilter) ||
            !string.Equals(sourceId, SelectedSourceId, StringComparison.OrdinalIgnoreCase)) return;
        (_readFailures ??= new()).Report(_log, "Media", $"control:{operation}:{sourceId}",
            $"媒体控制失败: source={sourceId} action={operation}", controlException);
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(sourceId, _lastControlNotificationSource, StringComparison.OrdinalIgnoreCase) &&
            now - _lastControlNotificationUtc < TimeSpan.FromSeconds(30)) return;
        _lastControlNotificationSource = sourceId;
        _lastControlNotificationUtc = now;
        try
        {
            _notifications.TryShowNotification(Translations.Get("Media.ControlUnavailable.Title"),
                Translations.Get("Media.ControlUnavailable.Body"), AFMediaBar.Classes.Models.ShellNotificationTarget.None, "Media.ControlUnavailable");
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

        // 提供器会持续报告读取失败；快照构建器负责在途去重、失败冷却、暂停门控和次数限制。
        if (snapshot is null)
        {
            TryRequestFallbackLyrics();
        }
        else if (snapshot.IsConnected && provider.CanHandle(_sessionSnapshot.SourceId) &&
                 _fallbackLogKey?.StartsWith(_sessionSnapshot.SourceId + "\u001f", StringComparison.Ordinal) == true)
        {
            // 恢复日志只观察通道，不借由诊断再次触发取词调度。
            _log?.Info("Media", $"已恢复专用媒体通道: source={_sessionSnapshot.SourceId} title=\"{snapshot.Title}\"");
            _fallbackLogKey = null;
        }
    }

    /// <summary>
    /// 在来源提供器读不出媒体时为当前 SMTC 会话发起一次在线取词兜底。
    ///
    /// 判据是"提供器已经明确报过它没有可读的媒体"（<see cref="MediaEnrichmentFallbackPolicy"/>）：提供器还没发布过任何
    /// 快照时它只是还没跑完第一轮轮询，那时兜底会在每次切歌的头 233 毫秒里白发一次请求。
    /// 重复请求由歌词缓存与在途去重挡住，临时失败在播放中冷却后重试，连续失败最多十轮。
    /// Starts one online-lyric fallback for the current SMTC session when its source provider cannot read any media.
    ///
    /// The condition is that the provider has already reported having no readable media (<see cref="MediaEnrichmentFallbackPolicy"/>): before
    /// it has published anything it has merely not finished its first poll, and a fallback then would waste one request during the first
    /// 233 ms of every track change. Cached and in-flight requests block repeats; failures retry after a cooldown during playback, up to ten consecutive failures.
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
        var fallbackKey = $"{baseline.SourceId}\u001f{baseline.Title}\u001f{baseline.Artist}";
        if (!MediaEnrichmentFallbackPolicy.ShouldRequestOnlineLyrics(
                baseline.IsConnected,
                provider is not null,
                providerPublishedNothing,
                baseline.Title))
        {
            if (_fallbackLogKey?.StartsWith(baseline.SourceId + "\u001f", StringComparison.Ordinal) == true && provider is not null &&
                _sourceSnapshots.GetValueOrDefault(provider) is { IsConnected: true })
                _log?.Info("Media", $"已恢复专用媒体通道: source={baseline.SourceId} title=\"{baseline.Title}\"");
            _fallbackLogKey = null;
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

        if (_fallbackLogKey != fallbackKey)
        {
            _fallbackLogKey = fallbackKey;
            _log?.Info("Media", $"进入在线取词兜底: source={baseline.SourceId} title=\"{baseline.Title}\" reason=source-provider-unavailable");
        }
        _snapshotBuilder.RequestOnlineLyrics(
            sessionKey,
            baseline.SourceId,
            baseline.Title,
            baseline.Artist,
            baseline.Duration > 0 ? baseline.Duration : null,
            baseline.IsPlaying);
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
            var catalogAvailable = _catalog.TryGetSnapshot(out var current);
            var sessions = catalogAvailable ? current : _lastSessions;
            PublishDiscoveredSources(sessions);
            RefreshSnapshot(sessions);
            if (catalogAvailable) (_readFailures ??= new()).Recover(_log, "Media", "catalog", "刷新媒体目录已恢复");
        }
        catch (Exception ex)
        {
            (_readFailures ??= new()).Report(_log, "Media", "catalog", "刷新媒体目录失败", ex);
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
            (_readFailures ??= new()).Recover(_log, "Media", "snapshot", "媒体快照调度已恢复");
            _ = BuildAndPublishSnapshotAsync(pendingSnapshot, session.Id, mediaRevision, selectionRevision);
        }
        catch (Exception ex)
        {
            (_readFailures ??= new()).Report(_log, "Media", "snapshot", "刷新媒体快照失败", ex);
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
                (_readFailures ??= new()).Recover(_log, "Media", "build", "媒体快照构建已恢复");
                TryRequestFallbackLyrics();
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            // Shutdown superseded the pending player read.
        }
        catch (Exception exception)
        {
            (_readFailures ??= new()).Report(_log, "Media", "build", "构建媒体快照失败", exception);
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
                    (_readFailures ??= new()).Recover(_log, "Media", "browser-presentation", "发布浏览器保留画面已恢复");
                }
                catch (Exception ex)
                {
                    // A subscriber failure must not escape a DispatcherTimer tick and crash the host.
                    (_readFailures ??= new()).Report(_log, "Media", "browser-presentation", "发布浏览器保留画面失败", ex);
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
        var sourceIds = _sources.DiscoverSourceIds(sessions.Select(MediaSessionGuard.GetSourceId))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!sourceIds.SequenceEqual(_discoverySourceIds, StringComparer.OrdinalIgnoreCase))
        {
            _discoverySourceIds = sourceIds;
            _discoveryRevision++;
        }
        if (!_discoveryInFlight)
            _ = PublishDiscoveredSourcesAsync(_discoverySourceIds, _discoveryRevision);
    }

    private async Task PublishDiscoveredSourcesAsync(string[] sourceIds, int revision)
    {
        _discoveryInFlight = true;
        try
        {
            // 启动路径解析会读取进程；与媒体快照分开在后台执行，同一时刻只保留一次解析。
            var sources = await Task.Run(() => sourceIds.TakeWhile(_ => !_isDisposed)
                .Select(_sourceActivator.Describe)
                .OrderBy(source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
            if (_isDisposed || revision != _discoveryRevision || sources.SequenceEqual(_lastDiscoveredSources))
                return;
            _lastDiscoveredSources = sources;
            DiscoveredSourcesChanged?.Invoke(sources);
            (_readFailures ??= new()).Recover(_log, "Media", "source-paths", "媒体来源启动路径读取已恢复");
        }
        catch (Exception ex)
        {
            (_readFailures ??= new()).Report(_log, "Media", "source-paths", "读取媒体来源启动路径失败", ex);
        }
        finally
        {
            _discoveryInFlight = false;
            if (!_isDisposed && revision != _discoveryRevision)
                _ = PublishDiscoveredSourcesAsync(_discoverySourceIds, _discoveryRevision);
        }
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

    private MediaSnapshotLogState? _snapshotLog;

    private void LogSnapshot(MediaSnapshot snapshot)
    {
        var log = AppLogService.Current;
        if (log is null || (_snapshotLog ??= new()).DescribeChanges(snapshot) is not { } changes) return;
        log.Info("Media", $"快照变化: changed=[{changes}] {snapshot.SourceName}({snapshot.SourceId}) " +
            $"\"{snapshot.Title}\" — \"{snapshot.Artist}\" connected={snapshot.IsConnected} playing={snapshot.IsPlaying} " +
            $"pos={snapshot.Position:0.0}/{snapshot.Duration:0.0} rate={snapshot.PlaybackRate:0.##} " +
            $"lyrics={snapshot.Lyrics?.Source ?? "none"} controls=(playPause={snapshot.CanPlayPause},seek={snapshot.CanSeek}," +
            $"prev={snapshot.CanSkipPrevious},next={snapshot.CanSkipNext},repeat={snapshot.CanChangeRepeat}) repeat={snapshot.RepeatMode}");
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
