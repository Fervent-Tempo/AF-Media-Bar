using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>在多个可见表面之间合并性能采样计时和 GPU 需求。 / Coalesces metric sampling and GPU demand across visible surfaces.</summary>
public sealed class SystemMetricsMonitorService : IDisposable, IMemoryPrunable
{
    /// <summary>
    /// 空闲档位的采样间隔倍数。性能文字是给人看的，而空闲意味着没有媒体、也没有人操作：把采样放慢四倍仍然看得出趋势，
    /// 而 PDH 查询（含 GPU 计数器）的开销直接降到四分之一。
    /// The sampling-interval multiplier at the idle level. Performance text is meant for a person, and idle means no media and no user input: sampling
    /// four times slower still shows the trend, while the cost of the PDH queries, GPU counters included, drops to a quarter.
    /// </summary>
    private const double IdleIntervalScale = 4d;

    private readonly SystemMetricsService _sampler;
    private readonly DispatcherTimer _timer = new();
    private readonly List<Subscription> _subscriptions = [];
    private double _intervalScale = 1d;
    private bool _disposed;

    public SystemMetricsMonitorService(SystemMetricsService sampler)
    {
        _sampler = sampler;
        _timer.Tick += OnTick;
    }

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "metrics-monitor";

    /// <summary>
    /// 按档位放慢采样或停掉整个采样计时器并释放 GPU 计数器。
    /// Slows sampling down for the level, or stops the whole sampling timer and releases the GPU counters.
    ///
    /// 订阅关系本身不动：消费者手里那个 <see cref="IDisposable"/> 仍然有效，恢复后继续收到回调。剪枝期间只是没人报警，
    /// 而不是"订阅被悄悄取消"。
    /// The subscriptions themselves stay untouched: the <see cref="IDisposable"/> a consumer holds keeps working and receives callbacks again after the
    /// restore. While pruned it simply goes quiet, rather than having its subscription silently cancelled.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_disposed)
        {
            return;
        }

        if (level >= MemoryPruneLevel.DisplayOff)
        {
            _timer.Stop();
            _sampler.ReleaseGpu();
            return;
        }

        _intervalScale = level == MemoryPruneLevel.Idle ? IdleIntervalScale : 1d;
        ReconfigureTimer();
    }

    public IDisposable Subscribe(IReadOnlyCollection<MetricKind> metrics, TimeSpan interval, Action<SystemMetricsSnapshot> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var subscription = new Subscription(this, metrics.Distinct().ToArray(),
            TimeSpan.FromMilliseconds(Math.Clamp(interval.TotalMilliseconds, 250, 60000)), callback);
        _subscriptions.Add(subscription);
        ReconfigureTimer();
        SampleDue(force: subscription);
        return subscription;
    }

    private void OnTick(object? sender, EventArgs e) => SampleDue();

    private void SampleDue(Subscription? force = null)
    {
        if (_disposed || _subscriptions.Count == 0) return;
        var now = DateTime.UtcNow;
        var due = _subscriptions.Where(item => ReferenceEquals(item, force) || item.NextDueUtc <= now).ToArray();
        if (due.Length == 0) return;
        var includeGpu = _subscriptions.Any(item => !item.IsDisposed && item.Metrics.Contains(MetricKind.SystemGpu));
        var snapshot = _sampler.Sample(includeGpu);
        foreach (var item in due)
        {
            // 前一个回调可能同步关闭另一个显示器宿主；快照里的已释放订阅绝不能再回调旧控件。
            // A preceding callback may synchronously close another monitor host; a disposed subscription captured in this snapshot must not reach stale UI.
            if (item.IsDisposed)
                continue;

            item.NextDueUtc = now + item.Interval;
            try
            {
                item.Callback(snapshot);
            }
            catch (Exception exception)
            {
                // 一个显示器上的控件失效不能中断其余订阅者或 DispatcherTimer；记录后让下一项继续。
                // A stale control on one monitor must not interrupt other subscribers or the DispatcherTimer; log it and continue.
                AppLogService.Current?.Warn(
                    "Metrics",
                    $"性能采样订阅回调失败 / metrics subscriber callback failed: {exception.Message}");
            }
        }
    }

    private void Remove(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        ReconfigureTimer();
        if (_subscriptions.All(item => !item.Metrics.Contains(MetricKind.SystemGpu)))
            _sampler.ReleaseGpu();
    }

    private void ReconfigureTimer()
    {
        _timer.Stop();
        if (_subscriptions.Count == 0) return;
        var baseInterval = _subscriptions.Min(item => item.Interval.TotalMilliseconds);
        _timer.Interval = TimeSpan.FromMilliseconds(baseInterval * _intervalScale);
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _subscriptions.Clear();
        _sampler.ReleaseGpu();
    }

    private sealed class Subscription(
        SystemMetricsMonitorService owner,
        MetricKind[] metrics,
        TimeSpan interval,
        Action<SystemMetricsSnapshot> callback) : IDisposable
    {
        public MetricKind[] Metrics { get; } = metrics;
        public TimeSpan Interval { get; } = interval;
        public Action<SystemMetricsSnapshot> Callback { get; } = callback;
        public DateTime NextDueUtc { get; set; }
        private bool _disposed;
        public bool IsDisposed => _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Remove(this);
        }
    }
}
