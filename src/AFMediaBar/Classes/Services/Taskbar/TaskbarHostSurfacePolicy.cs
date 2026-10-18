// 分层宿主"可见但未绘制"的自愈判定与会话预算；不执行探测，也不重建窗口。
namespace AFMediaBar.Classes.Services;

/// <summary>区域采样实际命中的窗口归属。/ Which window the region samples actually hit.</summary>
public enum TaskbarHostSurfaceHit
{
    /// <summary>命中宿主自己（或其子窗口），像素仍然存在。/ The host itself or a child, so pixels are still there.</summary>
    Host = 0,

    /// <summary>采样点均命中任务栏，宿主可能存在裁剪、层级或绘制异常。</summary>
    Taskbar = 1,

    /// <summary>命中其他窗口（浮层、其他应用、全屏窗口、空桌面），不构成失绘证据。/ Another window — a flyout, an application, a fullscreen window, the empty desktop — which is no evidence of a stale surface.</summary>
    Other = 2
}

/// <summary>
/// 媒体栏宿主异常命中的自愈判定。探测归停靠服务，重建归平台宿主；命中结果不能确定失绘根因。
/// </summary>
public static class TaskbarHostSurfacePolicy
{
    /// <summary>判定失绘所需的连续命中任务栏样本数；单次命中可能只是动画的一帧或某一处透明留白。/ Consecutive taskbar hits required to call it stale; a single hit can be one animation frame or one transparent spot.</summary>
    public const int RequiredStaleSamples = 2;

    /// <summary>连续正常样本达到此数后清零退避：重建确实是有效的恢复手段，不能因为历史次数被永久挡住。/ Consecutive healthy samples that clear the backoff: recreation really does recover the host, so past failures must not disarm it forever.</summary>
    public const int HealthySamplesToResetBackoff = 4;

    /// <summary>第一次自愈重建后的最短间隔，避免连续重建把界面刷成闪烁。/ Shortest interval after the first self-heal recreation, so repeated rebuilds cannot flash the bar.</summary>
    public static readonly TimeSpan ReloadCooldown = TimeSpan.FromSeconds(20);

    /// <summary>退避上限：即使失绘反复发生，也保持低频自愈而不是彻底放弃。/ Backoff ceiling: a recurring fault keeps healing at a low rate instead of being abandoned.</summary>
    public static readonly TimeSpan MaximumReloadCooldown = TimeSpan.FromMinutes(5);

    /// <summary>这次命中是否构成一次失绘样本。/ Whether this hit counts as one stale sample.</summary>
    public static bool IsStaleSample(TaskbarHostSurfaceHit hit) => hit == TaskbarHostSurfaceHit.Taskbar;

    /// <summary>连续重建次数对应的冷却：20 秒起，逐次翻倍，最多 5 分钟。/ Cooldown for the number of consecutive recreations: 20 s, doubling, capped at five minutes.</summary>
    /// <param name="consecutiveReloads">自上次正常以来的连续重建次数。/ Consecutive recreations since the host last painted.</param>
    public static TimeSpan GetCooldown(int consecutiveReloads)
    {
        if (consecutiveReloads <= 1)
            return ReloadCooldown;

        var doubling = Math.Min(consecutiveReloads - 1, 4);
        var ticks = ReloadCooldown.Ticks * (1L << doubling);
        return ticks >= MaximumReloadCooldown.Ticks ? MaximumReloadCooldown : TimeSpan.FromTicks(ticks);
    }

    /// <summary>是否应该重建宿主。/ Whether the host should be recreated now.</summary>
    /// <param name="staleSamples">连续失绘样本数。/ Consecutive stale samples.</param>
    /// <param name="consecutiveReloads">自上次正常以来的连续重建次数。/ Consecutive recreations since the host last painted.</param>
    /// <param name="sinceLastReload">距上次重建的时间；从未重建时传 <see cref="TimeSpan.MaxValue"/>。/ Time since the last recreation, or <see cref="TimeSpan.MaxValue"/> when there has never been one.</param>
    public static bool ShouldReload(int staleSamples, int consecutiveReloads, TimeSpan sinceLastReload) =>
        staleSamples >= RequiredStaleSamples && sinceLastReload >= GetCooldown(consecutiveReloads);
}

/// <summary>
/// 自愈预算的会话状态：宿主重建会换窗口实例，退避与冷却必须跟着会话走，否则每次重建都从零开始。
/// Session state for the self-heal budget: recreating the host replaces the window instance, so the backoff and cooldown have to live with the
/// session instead of restarting with every rebuild.
/// </summary>
public sealed class TaskbarHostSurfaceSession
{
    private readonly object _gate = new();
    private int _reloads;
    private int _consecutiveReloads;
    private int _healthySamples;
    private DateTime _lastReloadUtc = DateTime.MinValue;

    /// <summary>本次会话已经发生的自愈重建次数（诊断用）。/ Self-heal recreations in this session, for diagnostics.</summary>
    public int Reloads
    {
        get { lock (_gate) return _reloads; }
    }

    /// <summary>自上次正常以来的连续重建次数，决定下一次冷却。/ Consecutive recreations since the host last painted, which decides the next cooldown.</summary>
    public int ConsecutiveReloads
    {
        get { lock (_gate) return _consecutiveReloads; }
    }

    /// <summary>按本次连续样本数判定是否重建。/ Decides whether to recreate for the given number of consecutive samples.</summary>
    /// <param name="staleSamples">连续失绘样本数。/ Consecutive stale samples.</param>
    /// <param name="nowUtc">当前时间。/ Current time.</param>
    public bool ShouldReload(int staleSamples, DateTime nowUtc)
    {
        lock (_gate)
        {
            var sinceLast = _lastReloadUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - _lastReloadUtc;
            return TaskbarHostSurfacePolicy.ShouldReload(staleSamples, _consecutiveReloads, sinceLast);
        }
    }

    /// <summary>记录一次自愈重建。/ Records one self-heal recreation.</summary>
    /// <param name="nowUtc">当前时间。/ Current time.</param>
    public void RecordReload(DateTime nowUtc)
    {
        lock (_gate)
        {
            _reloads++;
            _consecutiveReloads++;
            _healthySamples = 0;
            _lastReloadUtc = nowUtc;
        }
    }

    /// <summary>记录一次正常样本；连续正常后清零退避，让下一次失绘仍能立即自愈。/ Records one healthy sample; enough of them in a row clear the backoff so the next fault heals immediately again.</summary>
    public void RecordHealthy()
    {
        lock (_gate)
        {
            if (_consecutiveReloads == 0)
                return;
            if (++_healthySamples < TaskbarHostSurfacePolicy.HealthySamplesToResetBackoff)
                return;
            _consecutiveReloads = 0;
            _healthySamples = 0;
            _lastReloadUtc = DateTime.MinValue;
        }
    }

    /// <summary>异常、遮挡或挂起会中断连续健康样本。</summary>
    public void RecordUnhealthy()
    {
        lock (_gate) _healthySamples = 0;
    }

    /// <summary>清空预算与冷却；测试与开发诊断用它回到会话起点。/ Clears the budget and cooldown, returning to the session's starting point for tests and diagnostics.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _reloads = 0;
            _consecutiveReloads = 0;
            _healthySamples = 0;
            _lastReloadUtc = DateTime.MinValue;
        }
    }
}

/// <summary>由容器持有的恢复会话集合；同一目标重建时保留退避，各显示器分别计数。</summary>
public sealed class TaskbarHostSurfaceSessions
{
    private readonly Dictionary<string, TaskbarHostSurfaceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>仅在 UI 线程的宿主构造期间取得目标状态。</summary>
    public TaskbarHostSurfaceSession Get(string target)
    {
        if (!_sessions.TryGetValue(target, out var session))
            _sessions[target] = session = new TaskbarHostSurfaceSession();
        return session;
    }
}
