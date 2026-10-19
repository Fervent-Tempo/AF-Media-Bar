// 验证启动评估有界自停和电源监听的幂等释放；不执行进程级回收或改变系统电源状态。
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Smtc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>启动剪枝期限和电源监听释放边界的回归验证。</summary>
[TestClass]
public sealed class MemoryPruneLifecycleTests
{
    [DataTestMethod]
    [DataRow(9, 3, true, false, false)]
    [DataRow(10, 3, true, true, true)]
    [DataRow(59, 0, true, false, false)]
    [DataRow(60, 0, true, true, true)]
    [DataRow(59, 10, false, false, false)]
    [DataRow(60, 10, false, true, false)]
    public void StartupDeadlineEndsEvaluationWithoutForcingPrunedLevels(int elapsed, int idle, bool normal, bool end, bool trim)
    {
        Assert.AreEqual(end, MemoryTrimPolicy.ShouldEndStartupEvaluation(TimeSpan.FromSeconds(elapsed), TimeSpan.FromSeconds(idle), normal));
        Assert.AreEqual(trim, MemoryTrimPolicy.ShouldTrimAfterStartup(TimeSpan.FromSeconds(elapsed), TimeSpan.FromSeconds(idle), normal));
    }

    [TestMethod]
    public void ExpiredNonNormalStartupTimerStopsAndDoesNotRunAfterRecovery()
    {
        StaTest.Run(_ =>
        {
            using var power = new PowerStateMonitor();
            // 不启动 SMTC；仅使用空事件字段验证协调器停止计时器和退订，避免查询真实播放器。
            var catalog = (MediaSessionCatalog)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionCatalog));
            var trimmer = new ProcessMemoryTrimmer();
            try
            {
                using var coordinator = new MemoryPruneCoordinator(power, catalog, trimmer, []);
                var clock = (Stopwatch)Field(coordinator, "_sinceStart").GetValue(coordinator)!;
                typeof(Stopwatch).GetField("_elapsed", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(clock, 61 * Stopwatch.Frequency);
                Field(coordinator, "_level").SetValue(coordinator, MemoryPruneLevel.DisplayOff);
                Field(coordinator, "_startupTrimPending").SetValue(coordinator, true);
                var timer = new DispatcherTimer(); timer.Start();
                Field(coordinator, "_startupTrimTimer").SetValue(coordinator, timer);
                var tick = typeof(MemoryPruneCoordinator).GetMethod("OnStartupTrimTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
                tick.Invoke(coordinator, [null, EventArgs.Empty]);
                Assert.IsFalse(timer.IsEnabled);
                Assert.IsNull(Field(coordinator, "_startupTrimTimer").GetValue(coordinator));
                Assert.AreEqual(false, Field(coordinator, "_startupTrimPending").GetValue(coordinator));
                Field(coordinator, "_level").SetValue(coordinator, MemoryPruneLevel.None);
                tick.Invoke(coordinator, [null, EventArgs.Empty]);
                coordinator.Dispose(); coordinator.Dispose();
                tick.Invoke(coordinator, [null, EventArgs.Empty]);
                return Task.CompletedTask;
            }
            finally { trimmer.Dispose(); }
        });
    }

    [TestMethod]
    public void DisposingMonitorDropsQueuedCallbacksAndReleasesMessageSink()
    {
        StaTest.Run(async dispatcher =>
        {
            using var power = new PowerStateMonitor();
            power.Start();
            var sink = (HwndSource?)Field(power, "_messageSink").GetValue(power);
            Assert.IsNotNull(sink);
            power.Start();
            Assert.AreSame(sink, Field(power, "_messageSink").GetValue(power));
            var calls = 0;
            power.StateChanged += (_, _) => calls++;
            // UI 暂不让出；后台完成投递后立刻释放，续体必须丢弃捕获的旧订阅。
            Task.Run(() => typeof(PowerStateMonitor).GetMethod("SetSuspended", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(power, [true])).GetAwaiter().GetResult();
            power.Dispose(); power.Dispose();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(0, calls);
            Assert.IsNull(Field(power, "_messageSink").GetValue(power));
            Assert.AreEqual(IntPtr.Zero, Field(power, "_displayStateNotification").GetValue(power));
        });
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
