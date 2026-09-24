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
public sealed class MediaSessionService : IDisposable, IMediaSessionSourceScanner
{
    private readonly MediaSessionCatalog _catalog;
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
    // 释放标记与 generation 会被后台流程读取（取消/过期判定），因此声明为 volatile：写入在 UI 线程，读取在后台线程。
    // The disposal flag and the generation are read by the background flow (cancellation and staleness checks), so both are volatile:
    // written on the UI thread and read on the background thread.
    private volatile bool _isDisposed;

    /// <summary>
    /// 自动重连看门狗：第三方库的会话字典只在 WinRT 事件触发时才同步（见库自己的 issue #6），换歌瞬间丢一次事件就会永久失明。
    /// 看门狗按 <see cref="MediaSessionReconcilePolicy"/> 判定节奏，在"断连"时才把探测与恢复交给一个后台 single-flight 流程；
    /// UI 侧的 tick 只读状态、做时间门控，不执行任何系统调用或库调用。
    /// Auto-reconcile watchdog: the third-party library only syncs its session dictionary when WinRT events fire (see its own issue #6),
    /// so one lost event at a track change leaves it blind forever. The watchdog follows
    /// <see cref="MediaSessionReconcilePolicy"/> and hands probing and recovery to one background single-flight flow while
    /// disconnected; the UI-side tick only reads state and gates time, making no system or library call.
    /// </summary>
    private readonly DispatcherTimer _reconcileWatchdog;
    private readonly MemoryPruneCoordinator _memoryPrune;
    private readonly CancellationTokenSource _reconcileCancellation = new();
    private Task? _reconcileTask;
    private DateTime _lastReconcileUtc = DateTime.MinValue;
    private DateTime _reconcileArmedUntilUtc = DateTime.MinValue;
    private int _consecutiveSlowReconciles;
    private int _consecutiveFailedReconciles;
    private volatile int _reconcileGeneration;
    private int _reconcileInFlight;

    /// <summary>
    /// 已经请求过在线取词兜底的那一曲（来源 + 曲名 + 歌手的指纹），只保留最后一个。
    /// 来源提供器每 233 毫秒都会报一次"没有可读的媒体"，没有这个门闩就会每 233 毫秒发起一次兜底取词；
    /// 只留一个是刻意的：它只需要挡住"同一首歌的重复请求"，换歌就该重新允许，而无界集合会随播放一直增长。
    /// The track (a fingerprint of source, title, and artist) whose online-lyric fallback has already been requested, keeping only the last one.
    /// A source provider reports "no readable media" every 233 ms, so without this latch a fallback would be requested every 233 ms; keeping
    /// exactly one is deliberate: it only has to block repeats for the same track, a track change must allow a new one, and an unbounded set
    /// would grow for as long as playback lasts.
    /// </summary>
    private string? _fallbackLyricsKey;

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
        MediaSessionCatalog catalog,
        MediaSessionSelectionService selection,
        MediaSnapshotBuilder snapshotBuilder,
        IEnumerable<IMediaSourceProvider> sourceProviders,
        MediaSourceActivationService sourceActivator,
        MemoryPruneCoordinator memoryPrune)
    {
        _catalog = catalog;
        _selection = selection;
        _snapshotBuilder = snapshotBuilder;
        _sourceProviders = sourceProviders.ToArray();
        _sourceActivator = sourceActivator;
        _memoryPrune = memoryPrune;
        _dispatcher = Application.Current.Dispatcher;

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
        _reconcileWatchdog = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _reconcileWatchdog.Tick += OnReconcileWatchdogTick;
        _reconcileWatchdog.Start();

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
        StartReconcile();
        return _reconcileTask ?? Task.CompletedTask;
    }

    /// <summary>
    /// 将有效会话键设为手动选择，并基于同一目录快照重新发布列表和媒体状态。
    /// Selects a valid session key manually and republishes the list and media state from the same catalog snapshot.
    /// </summary>
    public void SelectSession(string key)
    {
        if (string.IsNullOrEmpty(key) || !_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        var eligible = FilterSessions(sessions);
        if (!_selection.Select(key, eligible))
        {
            return;
        }

        PublishSessions(eligible);
        RefreshSnapshot(eligible);
    }

    /// <summary>
    /// 将播放/暂停请求转发给当前选中的 SMTC 会话；会话已消失时安全忽略。
    /// Forwards play/pause to the selected SMTC session and safely ignores the request if that session has disappeared.
    /// </summary>
    public Task TogglePlayPauseAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        await controlSession.TryTogglePlayPauseAsync();
    });

    /// <summary>
    /// 将上一首请求转发给当前仍有效的选中会话。
    /// Forwards the previous-track request to the selected session while it remains valid.
    /// </summary>
    public Task SkipPreviousAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        await controlSession.TrySkipPreviousAsync();
    });

    /// <summary>
    /// 将下一首请求转发给当前仍有效的选中会话。
    /// Forwards the next-track request to the selected session while it remains valid.
    /// </summary>
    public Task SkipNextAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        await controlSession.TrySkipNextAsync();
    });

    /// <summary>跳转到当前媒体的相对播放位置。 / Seeks to a relative position in the selected media item.</summary>
    public Task SeekAsync(double positionSeconds) => ExecuteOnSelectedAsync(async controlSession =>
    {
        var controls = controlSession.GetPlaybackInfo().Controls;
        var timeline = controlSession.GetTimelineProperties();
        var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        if (duration <= 0 || controls?.IsPlaybackPositionEnabled != true)
        {
            return;
        }

        var target = timeline.StartTime + TimeSpan.FromSeconds(Math.Clamp(positionSeconds, 0, duration));
        await controlSession.TryChangePlaybackPositionAsync(target.Ticks);
    });

    /// <summary>在关闭、列表和单曲循环之间切换。 / Cycles repeat between off, list, and track.</summary>
    public Task CycleRepeatModeAsync() => ExecuteOnSelectedAsync(async controlSession =>
    {
        var playback = controlSession.GetPlaybackInfo();
        if (playback.Controls?.IsRepeatEnabled != true)
        {
            return;
        }

        var next = playback.AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.None,
            _ => MediaPlaybackAutoRepeatMode.List
        };
        await controlSession.TryChangeAutoRepeatModeAsync(next);
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
        _reconcileWatchdog.Stop();
        _reconcileWatchdog.Tick -= OnReconcileWatchdogTick;
        // 取消在飞的后台流程并让 generation 失效：库调用本身无法中止，但它完成后不会再发布任何东西。
        // Cancels the in-flight background flow and invalidates the generation: the library call itself cannot be aborted, but nothing
        // it produces may be published afterwards.
        _reconcileCancellation.Cancel();
        _reconcileCancellation.Dispose();
        _reconcileGeneration++;
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
        Func<GlobalSystemMediaTransportControlsSession, Task> action)
    {
        if (!_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        var selected = FilterSessions(sessions).FirstOrDefault(session =>
            string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));

        // 会话可能刚被第三方库关闭；此处只取一次引用，后续命令交给该引用，避免读取过程中属性被置空。
        // The session may have just been closed by the third-party library; capture the reference once and issue commands
        // through that local so a concurrently cleared property cannot turn the call into a null reference.
        var controlSession = selected?.ControlSession;
        if (controlSession is null)
        {
            return;
        }

        try
        {
            await action(controlSession);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine($"[MediaSessionService] Media command failed: {ex}");
        }
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties) => ScheduleRefresh();

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => ScheduleSessionsRefresh();

    private void OnAnySessionOpened(MediaSession session) => ScheduleSessionsRefresh();

    private void OnAnySessionClosed(MediaSession session)
    {
        // 会话关闭是"换歌重建会话"的信号，也正是第三方库漏事件的高发点：武装快速窗口，让看门狗用 1 秒的节奏把新会话捞回来。
        // A session closing signals a track change and is exactly where the third-party library loses events: arm the fast window so the
        // watchdog catches the recreated session on a one-second cadence.
        _reconcileArmedUntilUtc = DateTime.UtcNow + MediaSessionReconcilePolicy.ArmedWindow;
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
        if (e.PropertyName != nameof(AppSettings.SmtcSourceFilter) &&
            e.ResetScope is not SettingsResetScope.ExtraFeatures and not SettingsResetScope.All)
            return;
        _selection.ClearSelection();
        ScheduleSessionsRefresh();
    }

    /// <summary>
    /// 看门狗 tick：只做零系统调用的门控。通过后立即消耗探测时隙（因此"没有媒体"的常见状态不会每秒查询一次），
    /// 再把探测与恢复交给后台 single-flight 流程。UI 线程在这里不做任何系统调用或库调用。
    /// Watchdog tick: gating only, with zero system calls. Once it passes, the probe slot is consumed immediately (so the common
    /// "no media at all" state cannot query once per second) and probing plus recovery are handed to the background single-flight
    /// flow. The UI thread makes no system or library call here.
    /// </summary>
    private void OnReconcileWatchdogTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var signals = new MediaSessionReconcileProbeSignals(
            IsConnected: _sessionSnapshot.IsConnected,
            IsGraceActive: _selection.IsMissingSessionGraceActive,
            IsArmed: now < _reconcileArmedUntilUtc,
            SinceLastAttempt: _lastReconcileUtc == DateTime.MinValue
                ? TimeSpan.MaxValue
                : now - _lastReconcileUtc,
            PruneLevel: _memoryPrune.CurrentLevel,
            ConsecutiveSlowReconciles: _consecutiveSlowReconciles);

        if (!MediaSessionReconcilePolicy.ShouldProbe(signals))
        {
            return;
        }

        // 时隙在系统查询之前消耗：无论系统有没有会话，下一次探测都要等一个完整间隔。
        // The slot is consumed before any system query: whether or not the OS has sessions, the next probe waits a full interval.
        _lastReconcileUtc = now;
        StartReconcile();
    }

    /// <summary>
    /// 启动一次后台探测与恢复（single-flight）。调用方必须已在 UI 线程消耗探测时隙；
    /// 已有流程在飞时本方法直接返回，而不会排队第二次。
    /// Starts one background probe-and-recover (single-flight). The caller must have consumed a probe slot on the UI thread; while a
    /// flow is in flight this returns immediately instead of queueing a second one.
    /// </summary>
    private void StartReconcile()
    {
        if (Interlocked.CompareExchange(ref _reconcileInFlight, 1, 0) != 0)
        {
            return;
        }

        var generation = _reconcileGeneration;
        var failures = _consecutiveFailedReconciles;
        var token = _reconcileCancellation.Token;
        _reconcileTask = Task.Run(() => ReconcileInBackground(generation, failures, token));
    }

    /// <summary>
    /// 后台探测与恢复：第三方库的会话枚举（ForceUpdate）与目录重建都在这里执行，绝不占用 UI 线程。
    /// 完成后只把"发布"经 Dispatcher 送回 UI；取消、Dispose 或 generation 变化都会丢弃过期结果。
    /// 库调用本身无法被取消，取消只保证过期结果不会被发布。
    /// Background probe and recovery: the third-party library's enumeration (ForceUpdate) and the catalog rebuild run here and never
    /// occupy the UI thread. Only the publication is marshalled back; cancellation, disposal, or a changed generation discard stale
    /// results. The library call itself cannot be cancelled — cancellation only guarantees nothing stale is published.
    /// </summary>
    private void ReconcileInBackground(int generation, int consecutiveFailures, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var osSessionCount = -1;
        var action = MediaSessionReconcileAction.None;
        try
        {
            if (token.IsCancellationRequested || _isDisposed)
            {
                return;
            }

            osSessionCount = _catalog.GetOsSessionCount();
            action = MediaSessionReconcilePolicy.DecideAction(osSessionCount, consecutiveFailures);
            if (action == MediaSessionReconcileAction.RestartCatalog)
            {
                _catalog.Restart();
            }
            else if (action == MediaSessionReconcileAction.ForceUpdate)
            {
                _catalog.ForceUpdate();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Background reconcile failed: {ex}");
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var stale = token.IsCancellationRequested || _isDisposed || generation != _reconcileGeneration ||
                        _dispatcher.HasShutdownStarted;
            if (stale)
            {
                // 取消 / 已释放 / generation 过期 / Dispatcher 关停：结果直接丢弃，单飞标志照常释放。
                // Cancelled, disposed, stale generation, or a shutting-down dispatcher: the result is dropped and the single-flight
                // flag is released either way.
                Interlocked.Exchange(ref _reconcileInFlight, 0);
            }
            else
            {
                try
                {
                    _dispatcher.BeginInvoke(() => CompleteReconcile(generation, osSessionCount, action, elapsed));
                }
                catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
                {
                    // Dispatcher 在发布前关停：没有订阅者需要这次结果了。
                    // The dispatcher shut down before the publication: nobody is left to receive it.
                    Interlocked.Exchange(ref _reconcileInFlight, 0);
                }
            }
        }
    }

    /// <summary>
    /// 在 UI 线程收尾一次后台重同步：先重建会话列表与快照，再按结果更新失败/慢调用计数与日志。
    /// Finishes one background reconcile on the UI thread: rebuilds the session list and snapshot first, then updates the failure and
    /// slow-call counters and the log from the result.
    /// </summary>
    private void CompleteReconcile(
        int generation,
        int osSessionCount,
        MediaSessionReconcileAction action,
        TimeSpan elapsed)
    {
        Interlocked.Exchange(ref _reconcileInFlight, 0);
        if (_isDisposed || generation != _reconcileGeneration)
        {
            return;
        }

        if (action == MediaSessionReconcileAction.RestartCatalog)
        {
            // 系统有会话而目录怎么都读不到：这是 ForceUpdate 救不回的库失效状态（字典里残留了失效条目），整只重建。
            // The OS has sessions while the catalog stays unreadable: a library broken state ForceUpdate cannot fix (a dead
            // dictionary entry), so the whole catalog was rebuilt.
            AppLogService.Current?.Info("Media", "[看门狗] 系统有会话但目录读不到，已重建媒体目录");
        }

        try
        {
            RefreshSessionList();
            RefreshSnapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to publish reconcile result: {ex}");
        }

        if (_sessionSnapshot.IsConnected)
        {
            _consecutiveFailedReconciles = 0;
            // 只有真正把会话捞回来时才写一行：空闲时看门狗会持续以低频重试，逐次记录会把日志刷满。
            // Only a recovered session earns a line: while idle the watchdog keeps retrying at a low cadence, and logging every
            // attempt would fill the file.
            AppLogService.Current?.Info(
                "Media",
                $"[看门狗] 事件丢失后恢复媒体会话：耗时 {elapsed.TotalMilliseconds:0.0}ms");
        }
        else if (osSessionCount > 0)
        {
            // 系统有会话却没读回来：累计失败次数，下一次到达阈值时改为重建目录。
            // The OS has sessions yet none came back: count the failure so the next attempt past the threshold rebuilds the catalog.
            _consecutiveFailedReconciles++;
        }
        else
        {
            _consecutiveFailedReconciles = 0;
        }

        // 慢调用统计覆盖"探测到恢复"的整段耗时（含系统查询）；恢复正常耗时就清零，因此退避不会累积成永久性的慢节奏。
        // Slow-call accounting covers the whole probe-and-recover attempt (system query included); one normal-duration attempt clears
        // it, so the backoff can never grow into a permanently slow cadence.
        _consecutiveSlowReconciles = MediaSessionReconcilePolicy.IsSlowCall(elapsed)
            ? Math.Min(_consecutiveSlowReconciles + 1, MediaSessionReconcilePolicy.MaximumBackoffShift)
            : 0;
    }

    private void OnSourceSnapshotChanged(IMediaSourceProvider provider, MediaSnapshot? snapshot)
    {
        _sourceSnapshots[provider] = snapshot;
        PublishResolved(ResolveSnapshot(_sessionSnapshot));

        // 提供器报"没有可读的媒体"时，它是每 233 毫秒报一次，因此这里必须用"同一个键只请求一次"挡住重复兜底取词。
        // When a provider reports "no readable media" it does so every 233 ms, so a repeated fallback retrieval is blocked here by
        // requesting once per key.
        if (snapshot is null)
        {
            TryRequestFallbackLyrics();
        }
    }

    /// <summary>
    /// 在来源提供器读不出媒体时为当前 SMTC 会话发起一次在线取词兜底。
    ///
    /// 判据是"提供器已经明确报过它没有可读的媒体"（<see cref="MediaEnrichmentFallbackPolicy"/>）：提供器还没发布过任何
    /// 快照时它只是还没跑完第一轮轮询，那时兜底会在每次切歌的头 233 毫秒里白发一次请求。同一个"来源 + 曲名 + 歌手"只请求
    /// 一次，重复的请求由歌词缓存与键门闩一起挡住。
    /// Starts one online-lyric fallback for the current SMTC session when its source provider cannot read any media.
    ///
    /// The condition is that the provider has already reported having no readable media (<see cref="MediaEnrichmentFallbackPolicy"/>): before
    /// it has published anything it has merely not finished its first poll, and a fallback then would waste one request during the first
    /// 233 ms of every track change. One "source + title + artist" is requested once, and the lyric cache plus the key latch block repeats.
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

        var key = $"{baseline.SourceId}\u001f{baseline.Title}\u001f{baseline.Artist}";
        if (string.Equals(key, _fallbackLyricsKey, StringComparison.Ordinal))
        {
            return;
        }

        // 会话标识必须是选中的那一个：歌词缓存键由它构成，兜底取回的结果要靠同一个键才会被下一次 Build 认领。
        // The session identifier has to be the selected one: it makes up the lyric cache key, and only the same key lets the next Build
        // claim the result the fallback fetched.
        var sessionKey = _selection.SelectedKey;
        if (string.IsNullOrEmpty(sessionKey))
        {
            return;
        }

        _fallbackLyricsKey = key;
        _snapshotBuilder.RequestOnlineLyrics(
            sessionKey,
            baseline.SourceId,
            baseline.Title,
            baseline.Artist,
            baseline.Duration > 0 ? baseline.Duration : null);
    }

    private void ScheduleRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(RefreshSnapshot, DispatcherPriority.Normal);
    }

    private void ScheduleSessionsRefresh()
    {
        if (_dispatcher.HasShutdownStarted || _isDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(RefreshSessionList, DispatcherPriority.Normal);
    }

    private void RefreshSessionList()
    {
        if (_isDisposed || !_catalog.TryGetSnapshot(out var sessions))
        {
            return;
        }

        try
        {
            PublishDiscoveredSources(sessions);
            var eligible = FilterSessions(sessions);
            if (eligible.Count == 0)
            {
                // 浏览器可能是唯一的 SMTC 会话；其重建期间空数组也必须启动恢复缓冲。
                // The browser may be the only SMTC session; an empty array must also start the recovery grace period.
                if (sessions.Length == 0 && _selection.TryHoldMissingSession())
                {
                    return;
                }

                _selection.ClearSelection();
                PublishSessions(eligible);
                Publish(MediaSnapshot.Disconnected);
                return;
            }

            var selected = _selection.Resolve(eligible);
            if (selected is null && _selection.IsMissingSessionGraceActive)
            {
                return;
            }

            PublishSessions(eligible);
            RefreshSnapshot(eligible);
        }
        catch (Exception ex)
        {
            // 本方法是 Dispatcher 回调：第三方会话状态在刷新过程中被改写时只记录并等待下一次刷新，不得让异常终止应用。
            // This method is a Dispatcher callback: a third-party session changing state mid-refresh is logged and left to
            // the next refresh instead of tearing down the application.
            Debug.WriteLine($"[MediaSessionService] Failed to refresh session list: {ex}");
        }
    }

    private void RefreshSnapshot()
    {
        if (_catalog.TryGetSnapshot(out var sessions))
        {
            PublishDiscoveredSources(sessions);
            RefreshSnapshot(FilterSessions(sessions));
        }
    }

    private void RefreshSnapshot(IReadOnlyList<MediaSession> sessions)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var selected = _selection.Resolve(sessions);
            if (selected is null && _selection.IsMissingSessionGraceActive)
            {
                return;
            }

            if (_selection.TryAutoSwitchToPlaying(sessions))
            {
                selected = sessions.FirstOrDefault(session =>
                    string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));
            }

            var snapshot = _snapshotBuilder.Build(selected, _catalog.IsStarted);
            if (snapshot is null)
            {
                return;
            }

            Publish(snapshot);
            TryRequestFallbackLyrics();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaSessionService] Failed to refresh snapshot: {ex}");
        }
    }

    private void PublishSessions(IReadOnlyList<MediaSession> sessions)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = sessions.Select(session =>
        {
            var sourceId = MediaSessionGuard.GetSourceId(session);
            occurrences.TryGetValue(sourceId, out var occurrence);
            occurrence++;
            occurrences[sourceId] = occurrence;
            var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, Translations.Get("Service.MediaSource.Unknown"));
            if (occurrence > 1)
            {
                displayName = $"{displayName} ({occurrence})";
            }

            return new MediaSessionOption(
                session.Id,
                sourceId,
                displayName,
                IsPlaying(session),
                string.Equals(session.Id, _selection.SelectedKey, StringComparison.Ordinal));
        }).ToArray();

        if (options.SequenceEqual(_lastSessionOptions))
        {
            return;
        }

        _lastSessionOptions = options;
        SessionsChanged?.Invoke(options);
    }

    private IReadOnlyList<MediaSession> FilterSessions(IReadOnlyList<MediaSession> sessions)
    {
        // 会话列表的唯一消费入口：第三方库可能在目录拷贝之后关闭会话，这里再次剔除已失效实例，
        // 使选择、快照、菜单和命令只面对仍可读取的会话。
        // The single consumption point for the session list: the third-party library can close a session after the catalog
        // copied it, so unusable instances are dropped here and selection, snapshots, menus, and commands only ever see
        // readable sessions.
        var usable = sessions.Where(MediaSessionGuard.IsUsable);
        var settings = SettingsManager.Current.SmtcSourceFilter;
        if (!settings.Enabled)
            return usable.ToArray();
        return usable.Where(session => MediaSourceFilterPolicy.IsAllowed(
            MediaSessionGuard.GetSourceId(session),
            settings)).ToArray();
    }

    private void PublishDiscoveredSources(IReadOnlyList<MediaSession> sessions)
    {
        var sources = sessions
            .Select(MediaSessionGuard.GetSourceId)
            .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
        _sessionSnapshot = snapshot;
        if (snapshot.IsConnected)
        {
            // 恢复连接即视为"事件链路已自愈"：解除快速窗口并清退避，让下一次失明从最灵敏的节奏重新开始。
            // A connected snapshot means the event path healed: disarm the fast window and clear the backoff so the next blindness
            // starts from the most responsive cadence again.
            _reconcileArmedUntilUtc = DateTime.MinValue;
            _consecutiveSlowReconciles = 0;
            _consecutiveFailedReconciles = 0;
        }

        foreach (var provider in _sourceProviders)
        {
            provider.UpdateSessionSnapshot(snapshot);
        }
        PublishResolved(ResolveSnapshot(snapshot));
    }

    private MediaSnapshot ResolveSnapshot(MediaSnapshot snapshot)
    {
        if (snapshot.IsConnected && !MediaSourceFilterPolicy.IsAllowed(
                snapshot.SourceId,
                SettingsManager.Current.SmtcSourceFilter))
            return MediaSnapshot.Disconnected;

        MediaSnapshot? providerSnapshot;
        if (snapshot.IsConnected)
        {
            var provider = _sourceProviders.FirstOrDefault(candidate => candidate.CanHandle(snapshot.SourceId));
            providerSnapshot = provider is not null && _sourceSnapshots.TryGetValue(provider, out var matched)
                ? matched
                : null;
        }
        else
        {
            providerSnapshot = _sourceProviders
                .Select(provider => _sourceSnapshots.GetValueOrDefault(provider))
                .Where(candidate => candidate is not null && MediaSourceFilterPolicy.IsAllowed(
                    candidate.SourceId,
                    SettingsManager.Current.SmtcSourceFilter))
                .OrderByDescending(candidate => candidate!.IsPlaying)
                .FirstOrDefault();
        }

        if (providerSnapshot is null)
        {
            return snapshot;
        }

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

        lock (_publishGate)
        {
            if (Equals(_lastSnapshot, snapshot))
            {
                return;
            }

            _lastSnapshot = snapshot;
            CurrentSnapshot = snapshot;
            LogSnapshot(snapshot);
            SnapshotChanged?.Invoke(this, snapshot);
        }
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
                $"快照变化 / snapshot changed: {snapshot.SourceName}({snapshot.SourceId}) " +
                $"\"{snapshot.Title}\" — \"{snapshot.Artist}\" " +
                $"connected={snapshot.IsConnected} playing={snapshot.IsPlaying} " +
                $"pos={snapshot.Position:0.0}/{snapshot.Duration:0.0} rate={snapshot.PlaybackRate:0.##} " +
                $"lyrics={(snapshot.Lyrics is null ? "none" : snapshot.Lyrics.Source)}");
            log.Verbose(
                "Media",
                $"快照细节 / detail: seek={snapshot.CanSeek} prev={snapshot.CanSkipPrevious} next={snapshot.CanSkipNext} " +
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
        log.Verbose("Media", $"进度 / position: {snapshot.Position:0.0}/{snapshot.Duration:0.0}");
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
