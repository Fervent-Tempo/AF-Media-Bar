using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 自动重连看门狗一次动作的选择。
/// The action one auto-reconcile watchdog tick chooses.
/// </summary>
public enum MediaSessionReconcileAction
{
    /// <summary>不动作。/ Do nothing.</summary>
    None = 0,

    /// <summary>调用库的 ForceUpdate 重新枚举会话，然后重建会话列表与快照。/ Call the library's ForceUpdate, then rebuild the session list and snapshot.</summary>
    ForceUpdate = 1,

    /// <summary>整只重建媒体目录：ForceUpdate 救不回来的库失效状态最后手段。/ Rebuild the whole media catalog: the last resort for a library broken state that ForceUpdate cannot fix.</summary>
    RestartCatalog = 2
}

/// <summary>
/// 自动重连看门狗的决策输入。
///
/// 第三方库 <c>WindowsMediaController</c> 的会话字典只随 WinRT 事件增量同步，而它自己的 issue #6 记录了
/// "运行期新会话检测不到、关闭后再开也检测不到"；一旦换歌瞬间丢掉事件，快照就会一直停在断连状态。
/// 看门狗用本策略决定何时 ForceUpdate、何时连目录一起重建。
/// Signals the auto-reconcile watchdog decides from.
///
/// The third-party <c>WindowsMediaController</c> library only syncs its session dictionary from WinRT events, while its own issue #6
/// records "new sessions are not detected at runtime, and a closed source cannot be re-detected"; once the event around a track change
/// is lost, the snapshot stays disconnected forever. The watchdog uses this policy to decide when to ForceUpdate and when to rebuild
/// the whole catalog.
/// </summary>
/// <param name="IsConnected">已发布快照是否连接（断连才需要重同步）。/ Whether the published snapshot is connected (only a disconnected one needs reconciling).</param>
/// <param name="IsGraceActive">浏览器会话重建的宽限期；宽限期内由选择器自己恢复，看门狗不得插手。/ The browser's session-recreation grace period; the selector restores it on its own, so the watchdog must not interfere.</param>
/// <param name="IsArmed">是否处于"刚收到会话关闭事件"的快速窗口。/ Whether the fast window armed by a session-closed event is active.</param>
/// <param name="SinceLastAttempt">距上一次重同步尝试的真实时间。/ Real time since the last reconcile attempt.</param>
/// <param name="PruneLevel">当前内存剪枝档位。/ Current memory-prune level.</param>
/// <param name="ConsecutiveSlowReconciles">连续"重同步耗时异常"的次数，用于指数退避。/ Consecutive slow reconciles, used for exponential backoff.</param>
/// <param name="OsSessionCount">操作系统当前发布的会话数；0 表示确实没有媒体，&lt;0 表示读不到（未知）。/ Sessions the OS publishes; 0 means genuinely no media, &lt;0 unknown.</param>
/// <param name="ConsecutiveFailedReconciles">连续"ForceUpdate 后仍断连且系统有会话"的次数，用于决定是否重建目录。/ Consecutive reconciles that stayed disconnected while the OS had sessions, used to decide on a catalog rebuild.</param>
public readonly record struct MediaSessionReconcileSignals(
    bool IsConnected,
    bool IsGraceActive,
    bool IsArmed,
    TimeSpan SinceLastAttempt,
    MemoryPruneLevel PruneLevel,
    int ConsecutiveSlowReconciles,
    int OsSessionCount,
    int ConsecutiveFailedReconciles);

/// <summary>
/// 自动重连看门狗的纯判定策略：保证"事件丢失后能自愈"，同时把额外开销限制在故障态。
/// Pure decision policy for the auto-reconcile watchdog: it guarantees self-healing after lost events while keeping the extra cost
/// bounded to the broken state.
/// </summary>
public static class MediaSessionReconcilePolicy
{
    /// <summary>会话关闭事件后的快速重试间隔：换歌重建会话正是丢事件的高发点，早一秒捞回来就少一秒钟的失明。
    /// Fast retry interval after a session-closed event: a track change is exactly where events go missing, so catching it one second
    /// earlier is one second less of blindness.</summary>
    public static readonly TimeSpan ArmedInterval = TimeSpan.FromSeconds(1);

    /// <summary>平时断连的重试间隔：低频兜底"启动时就没看见、运行期也没事件"的会话，同时又远低于任何可感知的开销。
    /// Ordinary retry interval while disconnected: a low-frequency backstop for sessions that were missed at startup or by events, far
    /// below any perceptible cost.</summary>
    public static readonly TimeSpan DisconnectedInterval = TimeSpan.FromSeconds(5);

    /// <summary>空闲剪枝档位下的重试间隔；此时用户已离开，重同步再慢一点没有代价。
    /// Retry interval under the idle prune level: the user is gone, so a slower reconcile costs nothing.</summary>
    public static readonly TimeSpan PrunedInterval = TimeSpan.FromSeconds(10);

    /// <summary>会话关闭后快速窗口的长度；超过它仍无媒体，说明不是换歌而是来源真的退出了。
    /// Length of the fast window after a session closes; staying empty past it means the source really exited rather than changed tracks.</summary>
    public static readonly TimeSpan ArmedWindow = TimeSpan.FromSeconds(30);

    /// <summary>单次重同步超过该耗时即视为异常（RPC 卡顿），随后指数退避，避免持续占用 UI 线程。
    /// A reconcile longer than this counts as abnormal (a stalled RPC) and triggers exponential backoff so the UI thread is not hammered.</summary>
    public static readonly TimeSpan SlowCallThreshold = TimeSpan.FromMilliseconds(250);

    /// <summary>退避的最大倍数（2 的幂次），防止长时间卡顿时把间隔吹得过大。</summary>
    /// <summary>Maximum backoff shift (a power of two), so a long stall cannot blow the interval up without bound.</summary>
    public const int MaximumBackoffShift = 4;

    /// <summary>连续多少次"ForceUpdate 后仍断连且系统有会话"后改为重建整个目录。/ How many consecutive ForceUpdate attempts may stay broken before the whole catalog is rebuilt.</summary>
    public const int CatalogRestartFailureThreshold = 2;

    /// <summary>
    /// 决定这次 tick 的动作。判据顺序体现优先级：先排除"有理由不动作"的状态，再看系统是否真的有会话，
    /// 最后按节奏与退避比较时间并选择 ForceUpdate 或重建目录。
    /// Decides the action for this tick. The order encodes priority: states that forbid acting come first, then whether the OS genuinely
    /// has sessions, then cadence and backoff decide the timing and whether to ForceUpdate or rebuild the catalog.
    /// </summary>
    /// <param name="signals">当前输入。/ The current signals.</param>
    public static MediaSessionReconcileAction Decide(in MediaSessionReconcileSignals signals)
    {
        // 显示关闭/睡眠档位下不做任何重同步：屏幕已经黑了，媒体栏没人看。
        // No reconcile while the display is off or the system suspends: nobody is looking at the bar.
        if (signals.PruneLevel >= MemoryPruneLevel.DisplayOff)
        {
            return MediaSessionReconcileAction.None;
        }

        // 已连接说明事件链路正常；宽限期由选择器负责恢复。两者都不需要看门狗。
        // Connected means the event path works; the grace period belongs to the selector. Neither needs the watchdog.
        if (signals.IsConnected || signals.IsGraceActive)
        {
            return MediaSessionReconcileAction.None;
        }

        // 系统确实没有发布任何会话：那是"没有媒体"，不是"库坏了"。ForceUpdate 也变不出会话，继续按断连节奏检查即可。
        // The OS genuinely publishes no session: that is "no media", not "the library is broken". ForceUpdate cannot conjure one, so
        // keep watching at the disconnected cadence without doing any work.
        if (signals.OsSessionCount == 0)
        {
            return MediaSessionReconcileAction.None;
        }

        var interval = signals.PruneLevel == MemoryPruneLevel.Idle
            ? PrunedInterval
            : signals.IsArmed ? ArmedInterval : DisconnectedInterval;

        var shift = Math.Clamp(signals.ConsecutiveSlowReconciles, 0, MaximumBackoffShift);
        interval *= 1 << shift;

        if (signals.SinceLastAttempt < interval)
        {
            return MediaSessionReconcileAction.None;
        }

        // 系统有会话、ForceUpdate 却连续失败：库的字典多半残留了失效条目，此时只能重建目录。
        // The OS has sessions while ForceUpdate keeps failing: the library's dictionary almost certainly holds a dead entry, and only
        // a catalog rebuild can clear it.
        return signals.ConsecutiveFailedReconciles >= CatalogRestartFailureThreshold
            ? MediaSessionReconcileAction.RestartCatalog
            : MediaSessionReconcileAction.ForceUpdate;
    }

    /// <summary>单次重同步是否慢到需要退避。/ Whether one reconcile was slow enough to warrant backoff.</summary>
    /// <param name="duration">本次重同步耗时。/ Duration of the reconcile.</param>
    public static bool IsSlowCall(TimeSpan duration) => duration >= SlowCallThreshold;
}
