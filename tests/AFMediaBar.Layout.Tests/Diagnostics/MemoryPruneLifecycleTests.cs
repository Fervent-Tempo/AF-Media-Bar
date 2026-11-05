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
            // STA 等待可能泵送消息；显式暂停调度，确保回调仍在排队时释放监听器。
            using (dispatcher.DisableProcessing())
            {
                Task.Run(() => typeof(PowerStateMonitor).GetMethod("SetSuspended", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(power, [true])).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                Assert.IsTrue(power.IsSuspended);
                Assert.AreEqual(0, calls, "投递准备期间不能执行 UI 回调。");
                power.Dispose(); power.Dispose();
            }
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(0, calls);
            Assert.IsNull(Field(power, "_messageSink").GetValue(power));
            Assert.AreEqual(IntPtr.Zero, Field(power, "_displayStateNotification").GetValue(power));
        });
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
