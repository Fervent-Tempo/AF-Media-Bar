using System.Diagnostics;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 剪枝档位变化的通知参数。
/// The notification payload for a prune level change.
/// </summary>
/// <param name="Level">新的档位。/ The new level.</param>
/// <param name="Reason">这次判定的依据（可直接写进日志或人工验证）。/ The basis of this decision, ready for the log or for manual verification.</param>
public sealed class MemoryPruneLevelChangedEventArgs(MemoryPruneLevel level, string reason) : EventArgs
{
    /// <summary>新的档位。/ The new level.</summary>
    public MemoryPruneLevel Level { get; } = level;

    /// <summary>判定依据。/ The basis of the decision.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// 后台剪枝协调器：把电源、会话、显示器与空闲信号解析成一个档位，通知每个资源所有者回收自己的东西，最后再做一次进程级回收。
/// The background prune coordinator: it resolves the power, session, display, and idle signals into one level, tells every resource owner to
/// reclaim its own things, and only then performs the process-level reclaim.
///
/// 它自己**不认识任何缓存、计时器或窗口**——那些分别属于歌词快照、来源轮询、音频采集、指标订阅与任务栏宿主，由各自的
/// <see cref="IMemoryPrunable"/> 实现释放（符合 `IMPLEMENTATION_CONSTRAINTS.md` 的所有权与依赖方向）。协调器只负责"什么时候"和"多彻底"。
/// It **knows nothing about any specific cache, timer, or window**: those belong to the lyric snapshot, the source poll, the audio capture, the
/// metrics subscriptions, and the taskbar host, each releasing its own through an <see cref="IMemoryPrunable"/> implementation, which keeps
/// ownership and dependency direction as `IMPLEMENTATION_CONSTRAINTS.md` requires. The coordinator decides only *when* and *how deeply*.
///
/// 触发时机：一个自适应周期的计时器（档位越高越慢）、电源/会话/显示器广播，以及媒体事件（播放一开始就立刻恢复到常规运行，不等到下一次轮询）。
/// It is triggered by an adaptive-period timer — slower at deeper levels — the power, session, and display broadcasts, and media events, so
/// playback starting immediately returns the process to normal operation instead of waiting for the next poll.
/// </summary>
public sealed class MemoryPruneCoordinator : IDisposable
{
    private readonly AppLogService? _log;
    private readonly PowerStateMonitor _power;
    private readonly MediaSessionCatalog _catalog;
    private readonly ProcessMemoryTrimmer _trimmer;
    private readonly IMemoryPrunable[] _participants;

    /// <summary>上一次深度回收的时间点，用于同一档位下的冷却。/ When the last deep reclaim ran, used for the cooldown within one level.</summary>
    private readonly Stopwatch _sinceLastDeepAction = Stopwatch.StartNew();

    /// <summary>上一次"按需回收"的时间点与重入标记，二者共同保证同一时刻只跑一次回收。/ When the last on-demand reclaim ran, plus the re-entrancy flag, which together keep only one reclaim running at a time.</summary>
    private readonly object _trimGate = new();
    private DateTime _lastTrimUtc;
    private int _trimInProgress;

    /// <summary>
    /// 启动后的那一次回收是否还在等待时机，以及负责判定的短周期计时器。
    /// Whether the post-startup reclaim is still waiting for its moment, together with the short-period timer that decides it.
    ///
    /// 之所以不复用档位评估计时器：L0 的评估周期是 30 秒，用它会让"启动后回收"落到启动后半分钟到一分钟之间，
    /// 而这段时间正是读数最难看、用户最可能去看任务管理器的时候。
    /// The level-evaluation timer is deliberately not reused: its period at L0 is thirty seconds, which would put the post-startup reclaim somewhere between
    /// half a minute and a minute after launch — exactly the stretch where the figure looks worst and the user is most likely to open Task Manager.
    /// </summary>
    private readonly Stopwatch _sinceStart = new();
    private DispatcherTimer? _startupTrimTimer;
    private bool _startupTrimPending;

    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private DateTime _mediaActiveAtUtc = DateTime.UtcNow;
    private MemoryPruneLevel _level = MemoryPruneLevel.None;
    private bool _evaluationScheduled;
    private bool _started;
    private volatile bool _disposed;

    /// <summary>
    /// 创建后台剪枝协调器；参与者由容器注入，因此新增一个可回收的资源不需要修改本类。
    /// Creates the background prune coordinator. The participants arrive through the container, so adding one more reclaimable resource never
    /// requires touching this class.
    /// </summary>
    /// <param name="power">电源状态监听。/ The power state monitor.</param>
    /// <param name="catalog">媒体会话目录，用于判断"现在还有没有在播的东西"。/ The media session catalog, used to tell whether anything is still playing.</param>
    /// <param name="trimmer">进程级回收器。/ The process-level trimmer.</param>
    /// <param name="participants">所有可回收的参与者。/ Every reclaimable participant.</param>
    /// <param name="log">程序日志。/ The application log.</param>
    public MemoryPruneCoordinator(
        PowerStateMonitor power,
        MediaSessionCatalog catalog,
        ProcessMemoryTrimmer trimmer,
        IEnumerable<IMemoryPrunable> participants,
        AppLogService? log = null)
    {
        _power = power;
        _catalog = catalog;
        _trimmer = trimmer;
        _participants = participants.ToArray();
        _log = log;
    }

    /// <summary>档位变化时触发（已在 UI 线程上）；任务栏宿主等非容器构造的界面据此调整自己的计时器。
    /// Raised on the UI thread when the level changes; surfaces that are not built by the container, such as the taskbar host, adjust their own
    /// timers from it.</summary>
    public event EventHandler<MemoryPruneLevelChangedEventArgs>? LevelChanged;

    /// <summary>当前已应用的档位。/ The level currently in effect.</summary>
    public MemoryPruneLevel CurrentLevel => _level;

    /// <summary>参与者的名称列表，只用于诊断与人工验证。/ The participant names, used for diagnostics and manual verification only.</summary>
    public IReadOnlyList<string> ParticipantNames { get; private set; } = [];

    /// <summary>
    /// 启动协调器：订阅信号、按当前档位设定评估周期并立刻评估一次。必须在 UI 线程上调用。
    /// Starts the coordinator: it subscribes to the signals, sets the evaluation period for the current level, and evaluates once. It must be called
    /// on the UI thread.
    /// </summary>
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ParticipantNames = _participants.Select(participant => participant.PruneParticipantName).ToArray();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = MemoryPrunePolicy.EvaluationIntervalFor(_level)
        };
        _timer.Tick += OnEvaluationTick;
        _timer.Start();

        _power.StateChanged += OnPowerStateChanged;
        _catalog.AnySessionOpened += OnAnySessionOpened;
        _catalog.AnySessionClosed += OnAnySessionClosed;
        _catalog.AnyPlaybackStateChanged += OnAnyPlaybackStateChanged;

        // 起始时间点定为启动时刻：程序刚起来时用户一定刚操作过，"无媒体"的计时从这一刻起算才对。
        // The clock starts at startup: the user has just interacted with something, so counting media idleness from this moment is the honest choice.
        _mediaActiveAtUtc = DateTime.UtcNow;

        // 启动后那一次回收：等启动链安定、用户松手之后再还页面。逻辑在 MemoryTrimPolicy.ShouldTrimAfterStartup 里，这里只负责按秒问它。
        // The post-startup reclaim: pages are handed back once the startup chain has settled and the user has let go. The rule lives in
        // MemoryTrimPolicy.ShouldTrimAfterStartup; this only asks it once a second.
        _sinceStart.Restart();
        _startupTrimPending = true;
        _startupTrimTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _startupTrimTimer.Tick += OnStartupTrimTick;
        _startupTrimTimer.Start();

        _log?.Info(
            "Prune",
            $"后台剪枝已启动 / background pruning started（评估周期 period {MemoryPrunePolicy.EvaluationIntervalFor(_level).TotalSeconds:0} s；" +
            $"参与者 participants: {string.Join(", ", ParticipantNames)}）");
        Evaluate();
    }

    /// <summary>
    /// 请求一次"按需回收"：某个窗口刚关掉，或用户点了"立即压缩物理内存"。
    /// Requests one on-demand reclaim: a window has just closed, or the user clicked "compress physical memory now".
    ///
    /// 与档位评估的分工是：档位回答"系统状态允许省多少"，本方法回答"这件事刚做完，现在值得收一遍吗"。因此它带来三件东西——
    /// 5 秒节流（界面连续开关时不该每次都回收）、重入保护（同一时刻只跑一次）、以及**在后台线程执行**（回收要等终结器，
    /// 不能占着 UI 线程）。手动请求绕过节流：用户点了按钮就必须有反应。
    /// It divides the work with the level evaluation like this: the level answers "how much may be saved given the system state", while this method
    /// answers "something just finished, is a reclaim worth it now". That brings three things with it: a five-second throttle, so that opening and
    /// closing panels in a row does not reclaim every time; a re-entrancy guard, so only one reclaim runs at a time; and execution **on a background
    /// thread**, because a reclaim waits for finalizers and must not tie up the UI thread. A manual request bypasses the throttle, because clicking the
    /// button has to do something.
    /// </summary>
    /// <param name="trigger">触发原因，决定强度与是否受节流限制。/ The trigger, which decides the strength and whether the throttle applies.</param>
    /// <returns>本次请求是否被接受（被节流或已在回收中时为 false）。/ Whether the request was accepted; false while throttled or already reclaiming.</returns>
    public bool RequestTrim(MemoryTrimTrigger trigger)
    {
        if (_disposed)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        lock (_trimGate)
        {
            if (Interlocked.CompareExchange(ref _trimInProgress, 1, 0) != 0)
            {
                return false;
            }

            if (!MemoryTrimPolicy.ShouldTrim(trigger, _lastTrimUtc, now))
            {
                Interlocked.Exchange(ref _trimInProgress, 0);
                return false;
            }

            _lastTrimUtc = now;
        }

        var strength = MemoryTrimPolicy.ResolveStrength(trigger);
        _ = Task.Run(() =>
        {
            try
            {
                var result = _trimmer.TrimWorkingSet(strength);
                WriteTrimLog(trigger, result);
            }
            catch (Exception ex)
            {
                // 按需回收只是"顺手省一点"，它失败绝不能让关窗路径或用户点击抛出。
                // An on-demand reclaim only saves a little on the side, so its failure must never escape into a window-close path or a user click.
                if (!_disposed)
                    _log?.Warn("Prune", $"按需回收失败 / on-demand reclaim failed ({trigger}): {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _trimInProgress, 0);
            }
        });

        return true;
    }

    private void WriteTrimLog(MemoryTrimTrigger trigger, WorkingSetTrimResult result)
    {
        if (_disposed || _log is null)
        {
            return;
        }

        var reason = trigger switch
        {
            MemoryTrimTrigger.SettingsWindowClosed => "设置窗口关闭 settings window closed",
            MemoryTrimTrigger.PanelClosed => "浮层关闭 panel closed",
            MemoryTrimTrigger.ManualRequest => "用户手动请求 manual request",
            MemoryTrimTrigger.StartupSettled => "启动已安定 post-startup",
            MemoryTrimTrigger.TaskbarHidden => "任务栏持续自动隐藏 taskbar stayed auto-hidden",
            _ => "进入空闲档 idle level entered"
        };
        var strength = result.Strength == MemoryTrimStrength.Deep ? "深度 deep" : "温和 gentle";

        _log.Info(
            "Prune",
            $"按需回收 / on-demand reclaim（{reason}，{strength}）：{result.Before.Describe()} → （{result.After.Describe()}；" +
            $"工作集已交还 working set returned={result.ReturnedWorkingSet}）");
    }

    /// <summary>
    /// 立刻按当前信号评估一次档位；档位未变化且还没到冷却时间时什么都不做。
    /// Evaluates the level from the current signals right away, doing nothing while the level is unchanged and the cooldown has not elapsed.
    /// </summary>
    public void Evaluate()
    {
        if (_disposed)
        {
            return;
        }

        if (TryDetectPlayingMedia())
        {
            _mediaActiveAtUtc = DateTime.UtcNow;
        }

        var signals = new MemoryPruneSignals(
            _power.IsSuspended,
            _power.IsSessionLocked,
            _power.IsDisplayOff,
            DateTime.UtcNow - _mediaActiveAtUtc,
            _power.UserIdle);

        var resolved = MemoryPrunePolicy.Resolve(signals);
        if (!MemoryPrunePolicy.ShouldApply(resolved, _level, _sinceLastDeepAction.Elapsed))
        {
            return;
        }

        Apply(resolved, signals);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startupTrimPending = false;
        StopStartupTrimTimer();
        if (_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnEvaluationTick;
            _timer = null;
        }

        _power.StateChanged -= OnPowerStateChanged;
        _catalog.AnySessionOpened -= OnAnySessionOpened;
        _catalog.AnySessionClosed -= OnAnySessionClosed;
        _catalog.AnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
    }

    private void Apply(MemoryPruneLevel level, MemoryPruneSignals signals)
    {
        var reason = DescribeReason(level, signals);

        // 参与者先释放自己的资源，进程级回收后做：顺序反了的话，GC 与交还工作集之后参与者的缓存还活着，
        // 那些页立刻会被重新摸一遍，回收等于白做。
        // Participants release their resources first and the process-level reclaim comes last: reversed, the participants' caches would still be
        // alive when the GC runs and the working set is returned, and those pages would be touched again immediately, undoing the reclaim.
        foreach (var participant in _participants)
        {
            try
            {
                participant.Prune(level);
            }
            catch (Exception ex)
            {
                _log?.Warn("Prune", $"参与者剪枝失败 / participant '{participant.PruneParticipantName}' failed to prune: {ex.Message}");
            }
        }

        MemoryReclaimResult? reclaim = null;
        if (level >= MemoryPruneLevel.DisplayOff)
        {
            reclaim = _trimmer.Reclaim(level);
            _sinceLastDeepAction.Restart();

            // 档位路径刚做过一次深度回收，按需路径的节流也要跟着走：否则紧接着关掉一个浮层就会再收一遍。
            // The level path has just done a deep reclaim, so the on-demand throttle has to move with it; otherwise closing a panel right afterwards
            // would collect all over again.
            lock (_trimGate)
            {
                _lastTrimUtc = DateTime.UtcNow;
            }
        }
        else if (level == MemoryPruneLevel.Idle)
        {
            _trimmer.ApplyIdleThrottling();

            // 空闲档只加一遍温和回收（后台 GC，不动工作集）：工作集交给"显示器关闭 / 睡眠"两档，
            // 因为空闲档之后用户可能马上回来。
            // The idle level adds one gentle reclaim — a background collection that leaves the working set alone — because the working set belongs to the
            // display-off and suspend levels, and after the idle level the user may come straight back.
            RequestTrim(MemoryTrimTrigger.IdleLevelEntered);
        }
        else
        {
            _trimmer.Restore();
        }

        _level = level;
        UpdateEvaluationInterval(level);
        WriteLog(level, reason, reclaim);
        RaiseLevelChanged(level, reason);
    }

    private void UpdateEvaluationInterval(MemoryPruneLevel level)
    {
        if (_timer is { } timer)
        {
            timer.Interval = MemoryPrunePolicy.EvaluationIntervalFor(level);
        }
    }

    private void WriteLog(MemoryPruneLevel level, string reason, MemoryReclaimResult? reclaim)
    {
        if (_log is null)
        {
            return;
        }

        if (level == MemoryPruneLevel.None)
        {
            _log.Info("Prune", $"恢复到常规运行 / back to normal operation（{reason}）");
            return;
        }

        var message =
            $"{DescribeLevel(level)} / {reason}（{reclaim?.Before.Describe() ?? _trimmer.Capture().Describe()}）";

        if (reclaim is { } result)
        {
            message +=
                $" → （{result.After.Describe()}；" +
                $"工作集已交还 working set returned={result.EmptiedWorkingSet}，" +
                $"内存优先级 memory priority={result.MemoryPriority}，EcoQoS={result.EcoQosEnabled}）";
        }

        _log.Info("Prune", message);
    }

    private void RaiseLevelChanged(MemoryPruneLevel level, string reason)
    {
        var handler = LevelChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new MemoryPruneLevelChangedEventArgs(level, reason));
        }
        catch (Exception ex)
        {
            // 界面只是跟着降频，它失败不该让剪枝链断掉。
            // The interface only follows along by slowing down, so its failure must not break the prune chain.
            _log?.Warn("Prune", $"通知界面剪枝档位失败 / notifying the interface about the prune level failed: {ex.Message}");
        }
    }

    private static string DescribeLevel(MemoryPruneLevel level) => level switch
    {
        MemoryPruneLevel.Idle => "空闲剪枝 L1 / idle prune L1",
        MemoryPruneLevel.DisplayOff => "显示关闭剪枝 L2 / display-off prune L2",
        _ => "睡眠剪枝 L3 / suspend prune L3"
    };

    private static string DescribeReason(MemoryPruneLevel level, MemoryPruneSignals signals)
    {
        var causes = new List<string>();
        if (signals.IsSuspended)
        {
            causes.Add("系统睡眠 suspending");
        }

        if (signals.IsSessionLocked)
        {
            causes.Add("会话锁定 session locked");
        }

        if (signals.IsDisplayOff)
        {
            causes.Add("显示器关闭 display off");
        }

        if (level == MemoryPruneLevel.Idle)
        {
            causes.Add(
                $"无媒体 media idle {signals.SinceLastMedia.TotalMinutes:0.0} min ≥ " +
                $"{MemoryPrunePolicy.MediaIdleThreshold.TotalMinutes:0} min");
            causes.Add(
                $"用户空闲 user idle {signals.UserIdle.TotalMinutes:0.0} min ≥ " +
                $"{MemoryPrunePolicy.UserIdleThreshold.TotalMinutes:0} min");
        }

        return causes.Count == 0 ? "有媒体或用户操作 media or user activity present" : string.Join("；", causes);
    }

    private bool TryDetectPlayingMedia()
    {
        if (!_catalog.TryGetSnapshot(out var sessions))
        {
            // 目录不可用（启动瞬间或正在重建）时按"有媒体"处理：宁可少剪一次，也不要误判成空闲把缓存全丢掉。
            // While the catalog is unavailable — at startup or while it rebuilds — media is assumed to be present: skipping one prune is far
            // better than mistaking it for idle and dropping every cache.
            return true;
        }

        foreach (var session in sessions)
        {
            if (!MediaSessionGuard.IsUsable(session))
            {
                continue;
            }

            try
            {
                if (session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // 会话在读取过程中关闭是正常竞态，忽略即可；只有其他异常值得记一笔。
                // A session closing mid-read is a normal race and is ignored; anything else is worth a line.
                _log?.Warn("Prune", $"读取播放状态失败 / reading the playback status failed: {ex.Message}");
            }
        }

        return false;
    }

    private void OnEvaluationTick(object? sender, EventArgs e) => Evaluate();

    /// <summary>
    /// 每秒问一次"启动后那一次回收到时候了吗"，到点就执行并自停。
    /// Asks once a second whether the post-startup reclaim is due, then runs it and stops itself.
    ///
    /// 判定为假时唯一要做的事就是继续等，因此这个计时器只在启动后的一小段时间内存在；一旦执行（或协调器被释放）它立刻停止，
    /// 不会变成一个常驻的每秒唤醒源。
    /// While the decision is false the only thing to do is keep waiting, so this timer exists only for a short stretch after startup; it stops the moment the
    /// reclaim runs — or the coordinator is disposed — and never turns into a resident once-a-second wakeup.
    /// </summary>
    private void OnStartupTrimTick(object? sender, EventArgs e)
    {
        if (_disposed || !_startupTrimPending)
        {
            StopStartupTrimTimer();
            return;
        }

        if (!MemoryTrimPolicy.ShouldTrimAfterStartup(_sinceStart.Elapsed, _power.UserIdle, _level == MemoryPruneLevel.None))
        {
            return;
        }

        _startupTrimPending = false;
        StopStartupTrimTimer();
        RequestTrim(MemoryTrimTrigger.StartupSettled);
    }

    private void StopStartupTrimTimer()
    {
        if (_startupTrimTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnStartupTrimTick;
            _startupTrimTimer = null;
        }
    }

    private void OnPowerStateChanged(object? sender, EventArgs e) => Evaluate();

    private void OnAnySessionOpened(MediaSession session) => RequestMediaEvaluation();

    private void OnAnySessionClosed(MediaSession session) => RequestMediaEvaluation();

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) => RequestMediaEvaluation();

    /// <summary>
    /// 请求一次由媒体事件触发的评估。播放状态变化可能很密集，因此先合并：同一时刻只排一次回调，之后的请求直接丢弃。
    /// Requests one media-triggered evaluation. Playback state changes can be dense, so they are coalesced: only one callback is queued at a time
    /// and later requests are dropped.
    /// </summary>
    private void RequestMediaEvaluation()
    {
        if (_disposed || _evaluationScheduled || _dispatcher is not { } dispatcher)
        {
            return;
        }

        _evaluationScheduled = true;
        try
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _evaluationScheduled = false;
                Evaluate();
            }));
        }
        catch (Exception ex)
        {
            _evaluationScheduled = false;
            _log?.Warn("Prune", $"投递媒体事件评估失败 / dispatching the media-triggered evaluation failed: {ex.Message}");
        }
    }
}
