// 拥有 SMTC 重连看门狗、退避状态和后台请求；借用目录，不释放目录或选择器。
using System.Diagnostics;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Media.Smtc;

internal sealed class MediaSessionReconcileCoordinator : IDisposable
{
    private readonly AppLogService? _log;
    private readonly RepeatedFailureReporter _failures = new();
    private readonly MediaSessionCatalog _catalog;
    private readonly MediaSessionSelectionService _selection;
    private readonly MemoryPruneCoordinator _memoryPrune;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isConnected;
    private readonly Func<bool> _isSnapshotBuilding;
    private readonly Action _refreshSnapshot;
    private volatile bool _isDisposed;
    private bool _started;

    internal MediaSessionReconcileCoordinator(MediaSessionCatalog catalog, MediaSessionSelectionService selection,
        MemoryPruneCoordinator memoryPrune, Dispatcher dispatcher, Func<bool> isConnected,
        Func<bool> isSnapshotBuilding, Action refreshSnapshot, AppLogService? log = null)
    {
        _catalog = catalog;
        _log = log;
        _selection = selection;
        _memoryPrune = memoryPrune;
        _dispatcher = dispatcher;
        _isConnected = isConnected;
        _isSnapshotBuilding = isSnapshotBuilding;
        _refreshSnapshot = refreshSnapshot;
        _reconcileWatchdog = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromSeconds(1) };
        _reconcileWatchdog.Tick += OnReconcileWatchdogTick;
    }

    internal void Start()
    {
        if (_isDisposed || _started) return;
        _started = true;
        _reconcileWatchdog.Start();
    }

    internal void Arm() => _reconcileArmedUntilUtc = DateTime.UtcNow + MediaSessionReconcilePolicy.ArmedWindow;

    internal Task ReconnectAsync()
    {
        if (_isDisposed) return Task.CompletedTask;
        StartReconcile();
        return _reconcileTask ?? Task.CompletedTask;
    }

    internal void SnapshotConnected()
    {
        _reconcileArmedUntilUtc = DateTime.MinValue;
        _consecutiveSlowReconciles = 0;
        _consecutiveFailedReconciles = 0;
        _consecutiveCatalogRestarts = 0;
        _lastCatalogRestartTimestamp = 0;
    }

    internal void SnapshotSettled()
    {
        if (_isDisposed || _isSnapshotBuilding() || _pendingReconcileStatus is not { } status) return;
        _pendingReconcileStatus = null;
        FinalizeReconcileStatus(status.OsSessionCount, status.Elapsed);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _reconcileWatchdog.Stop();
        _reconcileWatchdog.Tick -= OnReconcileWatchdogTick;
        _reconcileCancellation.Cancel();
        _reconcileCancellation.Dispose();
        _reconcileGeneration++;
        _pendingReconcileStatus = null;
    }
    private (int OsSessionCount, TimeSpan Elapsed)? _pendingReconcileStatus;

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
    private readonly CancellationTokenSource _reconcileCancellation = new();
    private Task? _reconcileTask;
    private DateTime _lastReconcileUtc = DateTime.MinValue;
    private DateTime _reconcileArmedUntilUtc = DateTime.MinValue;
    private int _consecutiveSlowReconciles;
    private int _consecutiveFailedReconciles;
    private int _consecutiveCatalogRestarts;
    private long _lastCatalogRestartTimestamp;
    private volatile int _reconcileGeneration;
    private int _reconcileInFlight;

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
            IsConnected: _isConnected(),
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
        var catalogRestarts = _consecutiveCatalogRestarts;
        var sinceLastCatalogRestart = _lastCatalogRestartTimestamp == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(_lastCatalogRestartTimestamp);
        var token = _reconcileCancellation.Token;
        _reconcileTask = Task.Run(() => ReconcileInBackground(generation, failures, catalogRestarts, sinceLastCatalogRestart, token));
    }

    /// <summary>
    /// 后台探测与恢复：第三方库的会话枚举（ForceUpdate）与目录重建都在这里执行，绝不占用 UI 线程。
    /// 完成后只把"发布"经 Dispatcher 送回 UI；取消、Dispose 或 generation 变化都会丢弃过期结果。
    /// 库调用本身无法被取消，取消只保证过期结果不会被发布。
    /// Background probe and recovery: the third-party library's enumeration (ForceUpdate) and the catalog rebuild run here and never
    /// occupy the UI thread. Only the publication is marshalled back; cancellation, disposal, or a changed generation discard stale
    /// results. The library call itself cannot be cancelled — cancellation only guarantees nothing stale is published.
    /// </summary>
    private void ReconcileInBackground(int generation, int consecutiveFailures, int consecutiveCatalogRestarts, TimeSpan sinceLastCatalogRestart, CancellationToken token)
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
            action = MediaSessionReconcilePolicy.DecideAction(osSessionCount, consecutiveFailures, consecutiveCatalogRestarts, sinceLastCatalogRestart);
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
            if (!_isDisposed && !token.IsCancellationRequested) _failures.Report(_log, "Media", "reconcile", "后台媒体恢复失败", ex);
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
            _consecutiveCatalogRestarts = Math.Min(_consecutiveCatalogRestarts + 1, MediaSessionReconcilePolicy.CatalogRestartLimit);
            _lastCatalogRestartTimestamp = Stopwatch.GetTimestamp();
            // 系统有会话而目录怎么都读不到：这是 ForceUpdate 救不回的库失效状态（字典里残留了失效条目），整只重建。
            // 连续重建到上限后策略会退回 ForceUpdate，不再每几秒重造一次目录（全屏游戏挡住系统查询时曾无限循环）。
            // The OS has sessions while the catalog stays unreadable: a library broken state ForceUpdate cannot fix (a dead
            // dictionary entry), so the whole catalog was rebuilt. Past the limit the policy falls back to ForceUpdate instead
            // of rebuilding the catalog every few seconds (this looped forever while a fullscreen game blocked the OS query).
            AppLogService.Current?.Info(
                "Media",
                $"[看门狗] 系统有会话但目录读不到，已重建媒体目录（连续第 {_consecutiveCatalogRestarts} 次，上限 {MediaSessionReconcilePolicy.CatalogRestartLimit}）");
        }

        try
        {
            _refreshSnapshot();
            _failures.Recover(_log, "Media", "reconcile-publish", "发布媒体恢复结果已恢复");
        }
        catch (Exception ex)
        {
            _failures.Report(_log, "Media", "reconcile-publish", "发布媒体恢复结果失败", ex);
        }

        // The snapshot now finishes asynchronously. Counting a failure against the previous snapshot here would
        // spuriously escalate to a catalog restart while the recovered session is still being read.
        if (_isSnapshotBuilding())
        {
            _pendingReconcileStatus = (osSessionCount, elapsed);
            return;
        }

        FinalizeReconcileStatus(osSessionCount, elapsed);
    }

    private void FinalizeReconcileStatus(int osSessionCount, TimeSpan elapsed)
    {
        if (_isConnected())
        {
            _failures.Recover(_log, "Media", "reconcile", "后台媒体恢复已恢复");
            _consecutiveFailedReconciles = 0;
            _consecutiveCatalogRestarts = 0;
            _lastCatalogRestartTimestamp = 0;
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
            _consecutiveCatalogRestarts = 0;
            _lastCatalogRestartTimestamp = 0;
        }

        // 慢调用统计覆盖"探测到恢复"的整段耗时（含系统查询）；恢复正常耗时就清零，因此退避不会累积成永久性的慢节奏。
        // Slow-call accounting covers the whole probe-and-recover attempt (system query included); one normal-duration attempt clears
        // it, so the backoff can never grow into a permanently slow cadence.
        _consecutiveSlowReconciles = MediaSessionReconcilePolicy.IsSlowCall(elapsed)
            ? Math.Min(_consecutiveSlowReconciles + 1, MediaSessionReconcilePolicy.MaximumBackoffShift)
            : 0;
    }

}
