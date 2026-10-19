// 验证拥塞时保留最新日志、连续入队仍落盘和并发释放；测试独占临时日志目录并清理。
using System.Collections;
using System.Reflection;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>日志队列溢出、周期落盘和并发释放的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AppLogQueueTests
{
    [TestMethod]
    public void FullQueueDropsOldestAndRetainsLatestErrorWithoutWaitingForWriter()
    {
        var directory = TempDirectory();
        try
        {
            using var log = new AppLogService(directory);
            var gate = typeof(AppLogService).GetField("_ringGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(log)!;
            var pending = typeof(AppLogService).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(log)!;
            Monitor.Enter(gate);
            try
            {
                log.Info("Test", "writer-blocked");
                Assert.IsTrue(SpinWait.SpinUntil(() => (int)pending.GetType().GetProperty("Count")!.GetValue(pending)! == 0, 5000));
                for (var i = 0; i < 4096; i++) log.Info("Test", $"queued-{i:D4}");
                log.Error("Test", "latest-error");
                var snapshot = ((IEnumerable)pending).Cast<object>().ToArray();
                Assert.AreEqual(4096, snapshot.Length);
                Assert.IsTrue(Line(snapshot[0]).Contains("queued-0001"));
                Assert.IsTrue(Line(snapshot[^1]).Contains("latest-error"));
            }
            finally { Monitor.Exit(gate); }
            log.Flush(TimeSpan.FromSeconds(5));
            StringAssert.Contains(File.ReadAllText(log.FilePath), "latest-error");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ContinuousTrafficStillRewritesBeforeProducerStops()
    {
        var directory = TempDirectory();
        try
        {
            using var log = new AppLogService(directory);
            using var stop = new CancellationTokenSource();
            var producer = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested) log.Info("Load", "ordinary continuous entry");
            });
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(7);
                while (!File.Exists(log.FilePath) && DateTime.UtcNow < deadline) await Task.Delay(50);
                Assert.IsTrue(File.Exists(log.FilePath), "Ordinary logs must land while traffic is still arriving.");
                Assert.IsFalse(producer.IsCompleted);
            }
            finally { stop.Cancel(); await producer; }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ConcurrentWritersAndRepeatedDisposeDoNotThrow()
    {
        var directory = TempDirectory();
        try
        {
            using var log = new AppLogService(directory);
            var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 3000; i++) log.Info("Race", "entry");
            })).ToArray();
            log.Dispose(); log.Dispose();
            await Task.WhenAll(writers);
            log.Error("Race", "late-error");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Line(object entry) => (string)entry.GetType().GetProperty("Line")!.GetValue(entry)!;
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
