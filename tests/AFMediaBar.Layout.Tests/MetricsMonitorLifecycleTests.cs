// 使用可控采样器验证后台循环与 Dispatcher 发布边界；不访问原生计数器或真实播放器。
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>共享性能监控的租约、暂停和退出回归。</summary>
[TestClass]
public sealed class MetricsMonitorLifecycleTests
{
    [TestMethod]
    public void BusyDispatcherOnlyPublishesTheLatestSampleForEachLease()
    {
        StaTest.Run(async dispatcher =>
        {
            using var sampler = new QueuedSampler();
            using var monitor = new SystemMetricsMonitorService(sampler);
            var publications = 0;
            var lastCpu = 0;
            using var lease = monitor.Subscribe([MetricKind.SystemCpu], TimeSpan.FromMilliseconds(250), snapshot =>
            {
                publications++;
                lastCpu = snapshot.SystemCpuPercent ?? 0;
            });
            try
            {
                // 第四次读取停在采样器内，证明前三次结果已经进入发布路径；不依赖固定 sleep。
                using (dispatcher.DisableProcessing())
                    Assert.IsTrue(sampler.FourthEntered.Wait(TimeSpan.FromSeconds(5)));
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.AreEqual(1, publications);
                Assert.AreEqual(3, lastCpu);
            }
            finally
            {
                monitor.Dispose();
                sampler.Release.Set();
                await monitor.StopAsync(CancellationToken.None);
            }
        });
    }

    [TestMethod]
    public void PausedSubscriptionsDoNotSampleAndResumeWithFreshBaseline()
    {
        StaTest.Run(async dispatcher =>
        {
            var sampler = new Sampler();
            using var monitor = new SystemMetricsMonitorService(sampler);
            monitor.Prune(MemoryPruneLevel.DisplayOff);
            using var lease = monitor.Subscribe([MetricKind.SystemNetwork], TimeSpan.FromSeconds(60), _ => { });
            await sampler.WaitForDemandAsync([]);
            Assert.AreEqual(0, sampler.Samples);
            monitor.Prune(MemoryPruneLevel.None);
            await sampler.Sampled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(sampler.Resets > 0);
            await monitor.StopAsync(CancellationToken.None);
            Assert.AreEqual(0, sampler.Demand.Length);
            monitor.Dispose();
        });
    }

    [TestMethod]
    public void MultipleConsumersShareDemandAndRemovingGpuReleasesItsDemand()
    {
        StaTest.Run(async dispatcher =>
        {
            var sampler = new Sampler();
            using var monitor = new SystemMetricsMonitorService(sampler);
            monitor.Prune(MemoryPruneLevel.DisplayOff);
            using var first = monitor.Subscribe([MetricKind.SystemMemory], TimeSpan.FromSeconds(60), _ => { });
            using var second = monitor.Subscribe([MetricKind.SystemGpu, MetricKind.SystemNetwork], TimeSpan.FromSeconds(60), _ => { });
            monitor.Prune(MemoryPruneLevel.None);
            await sampler.WaitForDemandAsync([MetricKind.SystemMemory, MetricKind.SystemGpu, MetricKind.SystemNetwork]);
            second.Dispose();
            await sampler.WaitForDemandAsync([MetricKind.SystemMemory]);
            first.Dispose();
            await sampler.WaitForDemandAsync([]);
            await monitor.StopAsync(CancellationToken.None);
        });
    }

    [TestMethod]
    public void StopDropsInflightResultsAndDoesNotWaitForUiPublication()
    {
        StaTest.Run(async dispatcher =>
        {
            var sampler = new Sampler { Block = true };
            using var monitor = new SystemMetricsMonitorService(sampler);
            var publications = 0;
            using var lease = monitor.Subscribe([MetricKind.SystemCpu], TimeSpan.FromSeconds(60), _ => publications++);
            await sampler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop = monitor.StopAsync(CancellationToken.None);
            Assert.IsFalse(stop.IsCompleted);
            sampler.Release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(0, publications);
            Assert.AreEqual(0, sampler.Demand.Length);
            await monitor.StopAsync(CancellationToken.None);
        });
    }

    [TestMethod]
    public void PauseDuringSamplingDropsTheOldResultAndRebasesOnResume()
    {
        StaTest.Run(async dispatcher =>
        {
            var sampler = new Sampler { Block = true };
            using var monitor = new SystemMetricsMonitorService(sampler);
            var publications = 0;
            using var lease = monitor.Subscribe([MetricKind.SystemNetwork], TimeSpan.FromSeconds(60), _ => publications++);
            await sampler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Prune(MemoryPruneLevel.DisplayOff);
            sampler.Release.TrySetResult();
            await sampler.WaitForDemandAsync([]);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(0, publications);
            monitor.Prune(MemoryPruneLevel.None);
            await sampler.WaitForDemandAsync([MetricKind.SystemNetwork]);
            Assert.IsTrue(sampler.Resets > 0);
            await monitor.StopAsync(CancellationToken.None);
        });
    }

    private sealed class QueuedSampler : ISystemMetricsSampler, IDisposable
    {
        private int _samples;
        public ManualResetEventSlim FourthEntered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public void SetDemand(IReadOnlyCollection<MetricKind> metrics) { }
        public void Reset() { }
        public SystemMetricsSnapshot Sample()
        {
            var sample = Interlocked.Increment(ref _samples);
            if (sample == 4)
            {
                FourthEntered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test did not release the fourth sample.");
            }
            return new(null, sample, null, null);
        }
        public void Dispose()
        {
            FourthEntered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class Sampler : ISystemMetricsSampler
    {
        private readonly object _gate = new();
        private readonly List<(MetricKind[] Demand, TaskCompletionSource Completion)> _waiters = [];
        private MetricKind[] _demand = [];
        public bool Block { get; init; }
        public int Samples;
        public int Resets;
        public MetricKind[] Demand { get { lock (_gate) return _demand.ToArray(); } }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Sampled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetDemand(IReadOnlyCollection<MetricKind> metrics)
        {
            lock (_gate)
            {
                _demand = metrics.OrderBy(item => item).ToArray();
                foreach (var waiter in _waiters.Where(item => item.Demand.SequenceEqual(_demand)).ToArray())
                {
                    waiter.Completion.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }
        }

        public Task WaitForDemandAsync(MetricKind[] demand)
        {
            lock (_gate)
            {
                demand = demand.OrderBy(item => item).ToArray();
                if (_demand.SequenceEqual(demand))
                    return Task.CompletedTask;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((demand, completion));
                return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public SystemMetricsSnapshot Sample()
        {
            Entered.TrySetResult();
            if (Block)
                Release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Interlocked.Increment(ref Samples);
            Sampled.TrySetResult();
            return new(40, 50, null, null);
        }

        public void Reset() => Interlocked.Increment(ref Resets);
    }
}
