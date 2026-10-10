// 一个后台循环拥有采样和资源重置；租约状态受锁保护，UI 回调另行检查代际和释放状态。
using System.Diagnostics;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services;

/// <summary>在多个可见表面之间共享按需采样，停止时等待后台资源清理。</summary>
public sealed class SystemMetricsMonitorService : IDisposable, IMemoryPrunable, IHostedService
{
    private const double IdleIntervalScale = 4d;
    private readonly ISystemMetricsSampler _sampler;
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly Dictionary<Subscription, (int Generation, SystemMetricsSnapshot Snapshot)> _pending = [];
    private bool _publicationQueued;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Task? _worker;
    private double _intervalScale = 1d;
    private bool _paused;
    private bool _disposed;
    private int _generation;
    private bool _resetRequired;

    public SystemMetricsMonitorService(ISystemMetricsSampler sampler)
    {
        _sampler = sampler;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public string PruneParticipantName => "metrics-monitor";

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        Task? worker;
        lock (_gate)
            worker = _worker;
        if (worker is not null)
            await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Prune(MemoryPruneLevel level)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var paused = level >= MemoryPruneLevel.DisplayOff;
            if (paused != _paused)
            {
                _resetRequired = true;
                foreach (var item in _subscriptions)
                    item.LastSampleTimestamp = null;
            }
            _paused = paused;
            _intervalScale = level == MemoryPruneLevel.Idle ? IdleIntervalScale : 1d;
            Changed();
        }
    }

    public IDisposable Subscribe(IReadOnlyCollection<MetricKind> metrics, TimeSpan interval, Action<SystemMetricsSnapshot> callback)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var subscription = new Subscription(this, metrics.Where(Enum.IsDefined).Distinct().ToArray(),
                TimeSpan.FromMilliseconds(Math.Clamp(interval.TotalMilliseconds, 250, 60000)), callback);
            _subscriptions.Add(subscription);
            _worker ??= Task.Run(WorkerAsync);
            Changed();
            return subscription;
        }
    }

    // 仅在 _gate 内调用；信号合并后仍由代际保证循环能看见最新状态。
    private void Changed()
    {
        _generation++;
        _pending.Clear();
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
            if (_subscriptions.Count == 0)
                _resetRequired = true;
            Changed();
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                MetricKind[] demand;
                Subscription[] due;
                int generation;
                bool reset;
                TimeSpan delay;
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    generation = _generation;
                    reset = _resetRequired;
                    _resetRequired = false;
                    demand = _paused ? [] : _subscriptions.SelectMany(item => item.Metrics).Distinct().ToArray();
                    var now = Stopwatch.GetTimestamp();
                    due = _paused ? [] : _subscriptions.Where(item => Remaining(item, now) <= TimeSpan.Zero).ToArray();
                    delay = _paused || _subscriptions.Count == 0 ? Timeout.InfiniteTimeSpan :
                        _subscriptions.Min(item => Remaining(item, now));
                }

                if (reset)
                    _sampler.Reset();
                _sampler.SetDemand(demand);
                if (due.Length > 0)
                {
                    SystemMetricsSnapshot? snapshot = null;
                    try
                    {
                        snapshot = _sampler.Sample();
                    }
                    catch (Exception exception)
                    {
                        AppLogService.Current?.Warn("Metrics", $"性能采样失败: {exception.Message}");
                    }

                    lock (_gate)
                    {
                        if (_disposed || _paused || generation != _generation)
                            continue;
                        var timestamp = Stopwatch.GetTimestamp();
                        foreach (var item in due)
                            item.LastSampleTimestamp = timestamp;
                    }
                    if (snapshot is { } value)
                        QueuePublication(generation, due, value);
                    continue;
                }

                await _wake.WaitAsync(delay < TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan ? TimeSpan.Zero : delay)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Metrics", $"性能监视器停止: {exception.Message}");
        }
        finally
        {
            // 不等待 Dispatcher；Host.StopAsync 即使由退出边界调用也能完成。
            try
            {
                _sampler.SetDemand([]);
                _sampler.Reset();
            }
            catch (Exception exception)
            {
                AppLogService.Current?.Warn("Metrics", $"性能资源清理失败: {exception.Message}");
            }
        }
    }

    private TimeSpan Remaining(Subscription item, long now) => item.LastSampleTimestamp is long last
        ? TimeSpan.FromMilliseconds(item.Interval.TotalMilliseconds * _intervalScale) - Stopwatch.GetElapsedTime(last, now)
        : TimeSpan.Zero;

    private void QueuePublication(int generation, Subscription[] due, SystemMetricsSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || _paused || generation != _generation || _dispatcher.HasShutdownStarted)
                return;
            foreach (var item in due.Where(item => !item.IsDisposed))
                _pending[item] = (generation, snapshot);
            if (_publicationQueued)
                return;
            _publicationQueued = true;
        }
        // Dispatcher 堵塞时每个租约只保留最新结果，避免恢复后回放大量过期读数。
        _ = _dispatcher.BeginInvoke(PublishPending);
    }

    private void PublishPending()
    {
        KeyValuePair<Subscription, (int Generation, SystemMetricsSnapshot Snapshot)>[] pending;
        lock (_gate)
        {
            pending = _pending.ToArray();
            _pending.Clear();
            _publicationQueued = false;
        }
        foreach (var entry in pending)
        {
            var item = entry.Key;
            lock (_gate)
            {
                if (_disposed || _paused || entry.Value.Generation != _generation)
                    return;
                if (item.IsDisposed)
                    continue;
            }
            try
            {
                item.Callback(entry.Value.Snapshot);
            }
            catch (Exception exception)
            {
                AppLogService.Current?.Warn("Metrics", $"性能采样订阅回调失败: {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _subscriptions.Clear();
            Changed();
        }
        // 未创建 WaitHandle；晚到的租约释放仍可唤醒循环，不依赖已停止的 Dispatcher。
    }

    private sealed class Subscription(SystemMetricsMonitorService owner, MetricKind[] metrics,
        TimeSpan interval, Action<SystemMetricsSnapshot> callback) : IDisposable
    {
        public MetricKind[] Metrics { get; } = metrics;
        public TimeSpan Interval { get; } = interval;
        public Action<SystemMetricsSnapshot> Callback { get; } = callback;
        public long? LastSampleTimestamp { get; set; }
        private int _disposed;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Remove(this);
        }
    }
}
