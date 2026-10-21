// 验证故障合并和高频状态降噪；只使用独立临时日志。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Models.Updates;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>普通日志降噪与故障上下文的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DiagnosticOutputTests
{
    [TestMethod]
    public void RepeatedFailureIsSummarizedAndRecoveryStartsANewEpisode()
    {
        WithLog(log =>
        {
            var clock = new Clock();
            var reporter = new RepeatedFailureReporter(clock);
            var error = new InvalidOperationException("unavailable");
            reporter.Report(log, "Probe", "read", "failed", error);
            for (var i = 0; i < 20; i++) reporter.Report(log, "Probe", "read", "failed", error);
            clock.Advance(TimeSpan.FromSeconds(60));
            reporter.Report(log, "Probe", "read", "failed", error);
            Assert.IsTrue(reporter.Recover(log, "Probe", "read", "recovered"));
            Assert.IsFalse(reporter.Recover(log, "Probe", "read", "recovered"));
            reporter.Report(log, "Probe", "read", "failed", error);
            log.Dispose();
            var entries = File.ReadAllLines(log.FilePath).Where(line => line.Contains("[Probe]")).ToArray();
            Assert.AreEqual(4, entries.Length);
            Assert.AreEqual(1, entries.Count(line => line.Contains("[INF]")));
        });
    }

    [TestMethod]
    public void MediaProgressIsSilentButControlsAndMeaningfulDurationChangesAreVisible()
    {
        var state = new MediaSnapshotLogState();
        var snapshot = MediaSnapshot.Disconnected with { IsConnected = true, Title = "Track", Duration = 100, IsPlaying = true };
        Assert.IsNotNull(state.DescribeChanges(snapshot));
        Assert.IsNull(state.DescribeChanges(snapshot with { Position = 20, Duration = 100.4 }));
        snapshot = snapshot with { CanPlayPause = true, Duration = 100.6 };
        Assert.IsNotNull(state.DescribeChanges(snapshot));
        Assert.IsNull(state.DescribeChanges(snapshot with { Duration = 101.1 }), "其它状态行已记录的时长也应成为阈值基准。");
        Assert.IsNotNull(state.DescribeChanges(snapshot with { Duration = 101.7 }));
        Assert.IsNotNull(state.DescribeChanges(snapshot with { Duration = 0, IsPlaying = false }));
    }

    [TestMethod]
    public void UpdateProgressDoesNotRepeatPhaseLogs()
    {
        StaTest.Run(_ =>
        {
            WithLog(log =>
            {
                using var service = new UpdateService(null!, null!, null!, null!, null!, log);
                var publish = typeof(UpdateService).GetMethod("PublishState", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var state = service.CurrentState with { Phase = UpdatePhase.Downloading };
                for (var percent = 0; percent <= 100; percent++)
                    publish.Invoke(service, [state with { ProgressPercent = percent }]);
                publish.Invoke(service, [state with { Phase = UpdatePhase.Ready }]);
                publish.Invoke(service, [state with { Phase = UpdatePhase.Failed, FailureReason = "verification failed" }]);
                log.Dispose();
                var entries = File.ReadAllLines(log.FilePath).Where(line => line.Contains("[Update]")).ToArray();
                Assert.AreEqual(3, entries.Length);
                StringAssert.Contains(entries[2], "[WRN]");
            });
            return Task.CompletedTask;
        });
    }

    private static void WithLog(Action<AppLogService> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AFMediaBar-log-{Guid.NewGuid():N}");
        try { using var log = new AppLogService(directory); action(log); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
