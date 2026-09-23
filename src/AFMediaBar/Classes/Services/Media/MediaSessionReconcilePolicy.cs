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
/// 自动重连看门狗的探测门控输入。
///
/// 门控**刻意不接收系统会话数**：探测节奏必须发生在任何系统查询之前，否则"没有媒体"的常见状态下会每秒查询一次。
/// Probe gating signals for the auto-reconcile watchdog.
///
/// The gating deliberately takes no OS session count: the probe cadence has to be decided before any system query, otherwise the
/// common "no media at all" state would query once per second.
/// </summary>
/// <param name="IsConnected">已发布快照是否连接（断连才需要探测）。/ Whether the published snapshot is connected (only a disconnected one needs probing).</param>
/// <param name="IsGraceActive">浏览器会话重建的宽限期；宽限期内由选择器自己恢复，看门狗不得插手。/ The browser's session-recreation grace period; the selector restores it on its own, so the watchdog must not interfere.</param>
/// <param name="IsArmed">是否处于"刚收到会话关闭事件"的快速窗口。/ Whether the fast window armed by a session-closed event is active.</param>
/// <param name="SinceLastAttempt">距上一次探测时隙的真实时间。/ Real time since the previous probe slot.</param>
/// <param name="PruneLevel">当前内存剪枝档位。/ Current memory-prune level.</param>
/// <param name="ConsecutiveSlowReconciles">连续"探测到恢复整段耗时异常"的次数，用于指数退避。/ Consecutive slow probe-and-recover attempts, used for exponential backoff.</param>
public readonly record struct MediaSessionReconcileProbeSignals(
    bool IsConnected,
    bool IsGraceActive,
    bool IsArmed,
    TimeSpan SinceLastAttempt,
    MemoryPruneLevel PruneLevel,
    int ConsecutiveSlowReconciles);

/// <summary>
/// 自动重连看门狗的纯判定策略：保证"事件丢失后能自愈"，同时把额外开销限制在故障态。
///
/// 判定分成两步：<see cref="ShouldProbe"/> 只做零系统调用的时间与档位门控（因此探测时隙先于系统查询被消耗），
/// <see cref="DecideAction"/> 才根据系统会话数与失败次数决定动作。两步都是纯函数，可独立单测。
/// Pure decision policy for the auto-reconcile watchdog: it guarantees self-healing after lost events while keeping the extra cost
/// bounded to the broken state.
///
/// The decision is split in two: <see cref="ShouldProbe"/> only gates time and prune level with zero system calls (so a probe slot is
/// consumed before any system query), and <see cref="DecideAction"/> picks the action from the OS session count and failure count.
/// Both are pure functions and independently unit-testable.
/// </summary>
public static class MediaSessionReconcilePolicy
{
    /// <summary>会话关闭事件后的快速探测间隔：换歌重建会话正是丢事件的高发点，早一秒捞回来就少一秒钟的失明。
    /// Fast probe interval after a session-closed event: a track change is exactly where events go missing, so catching it one second
    /// earlier is one second less of blindness.</summary>
    public static readonly TimeSpan ArmedInterval = TimeSpan.FromSeconds(1);

    /// <summary>平时断连的探测间隔：低频兜底"启动时就没看见、运行期也没事件"的会话，同时又远低于任何可感知的开销。
    /// Ordinary probe interval while disconnected: a low-frequency backstop for sessions that were missed at startup or by events, far
    /// below any perceptible cost.</summary>
    public static readonly TimeSpan DisconnectedInterval = TimeSpan.FromSeconds(5);

    /// <summary>空闲剪枝档位下的探测间隔；此时用户已离开，探测再慢一点没有代价。
    /// Probe interval under the idle prune level: the user is gone, so a slower probe costs nothing.</summary>
    public static readonly TimeSpan PrunedInterval = TimeSpan.FromSeconds(10);

    /// <summary>会话关闭后快速窗口的长度；超过它仍无媒体，说明不是换歌而是来源真的退出了。
    /// Length of the fast window after a session closes; staying empty past it means the source really exited rather than changed tracks.</summary>
    public static readonly TimeSpan ArmedWindow = TimeSpan.FromSeconds(30);

    /// <summary>单次"探测到恢复"超过该耗时即视为异常（RPC 卡顿），随后指数退避，避免持续占用后台线程。
    /// A probe-and-recover attempt longer than this counts as abnormal (a stalled RPC) and triggers exponential backoff so the
    /// background thread is not hammered.</summary>
    public static readonly TimeSpan SlowCallThreshold = TimeSpan.FromMilliseconds(250);

    /// <summary>退避的最大倍数（2 的幂次），防止长时间卡顿时把间隔吹得过大。
    /// Maximum backoff shift (a power of two), so a long stall cannot blow the interval up without bound.</summary>
    public const int MaximumBackoffShift = 4;

    /// <summary>连续多少次"ForceUpdate 后仍断连且系统有会话"后改为重建整个目录。/ How many consecutive ForceUpdate attempts may stay broken before the whole catalog is rebuilt.</summary>
    public const int CatalogRestartFailureThreshold = 2;

    /// <summary>
    /// 这次 tick 是否到了探测时隙。只读门控信号，不做任何系统调用；返回 true 表示调用方应立即消耗该时隙（更新时间戳）并开始一次后台探测。
    /// Whether this tick has reached a probe slot. It only reads gating signals and makes no system call; true means the caller should
    /// consume the slot immediately (update the timestamp) and start one background probe.
    /// </summary>
    /// <param name="signals">当前门控信号。/ The current gating signals.</param>
    public static bool ShouldProbe(in MediaSessionReconcileProbeSignals signals)
    {
        // 显示关闭/睡眠档位下不探测：屏幕已经黑了，媒体栏没人看。
        // No probing while the display is off or the system suspends: nobody is looking at the bar.
        if (signals.PruneLevel >= MemoryPruneLevel.DisplayOff)
        {
            return false;
        }

        // 已连接说明事件链路正常；宽限期由选择器负责恢复。两者都不需要看门狗。
        // Connected means the event path works; the grace period belongs to the selector. Neither needs the watchdog.
        if (signals.IsConnected || signals.IsGraceActive)
        {
            return false;
        }

        var interval = signals.PruneLevel == MemoryPruneLevel.Idle
            ? PrunedInterval
            : signals.IsArmed ? ArmedInterval : DisconnectedInterval;

        var shift = Math.Clamp(signals.ConsecutiveSlowReconciles, 0, MaximumBackoffShift);
        interval *= 1 << shift;

        return signals.SinceLastAttempt >= interval;
    }

    /// <summary>
    /// 根据一次系统探测的结果决定动作。
    /// 系统确实没有会话时不动（那是"没有媒体"，不是"库坏了"）；查询失败（负值）时做一次保守的 ForceUpdate；
    /// 系统有会话而 ForceUpdate 连续失败时升级为重建整个目录。
    /// Picks the action from the result of one system probe.
    /// Genuinely zero sessions means "no media" rather than "the library is broken", so nothing happens; a failed query (negative)
    /// does one conservative ForceUpdate; and sessions that keep failing after ForceUpdate escalate to a full catalog rebuild.
    /// </summary>
    /// <param name="osSessionCount">操作系统当前发布的会话数；0 为确实没有媒体，负值为查询失败。/ Sessions the OS publishes; zero is genuinely no media, negative is a failed query.</param>
    /// <param name="consecutiveFailedReconciles">连续"ForceUpdate 后仍断连且系统有会话"的次数。/ Consecutive reconciles that stayed disconnected while the OS had sessions.</param>
    public static MediaSessionReconcileAction DecideAction(int osSessionCount, int consecutiveFailedReconciles)
    {
        if (osSessionCount == 0)
        {
            return MediaSessionReconcileAction.None;
        }

        if (osSessionCount < 0)
        {
            return MediaSessionReconcileAction.ForceUpdate;
        }

        return consecutiveFailedReconciles >= CatalogRestartFailureThreshold
            ? MediaSessionReconcileAction.RestartCatalog
            : MediaSessionReconcileAction.ForceUpdate;
    }

    /// <summary>单次"探测到恢复"是否慢到需要退避。/ Whether one probe-and-recover attempt was slow enough to warrant backoff.</summary>
    /// <param name="duration">本次尝试的耗时。/ Duration of the attempt.</param>
    public static bool IsSlowCall(TimeSpan duration) => duration >= SlowCallThreshold;
}
