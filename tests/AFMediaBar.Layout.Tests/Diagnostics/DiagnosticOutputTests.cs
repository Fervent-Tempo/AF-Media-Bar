// 验证故障合并、媒体变更可解释性及并行取词摘要隔离；只使用临时日志和本地提供器。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Models.Updates;
using System.Reflection;
using AFMediaBar.Classes.Settings;
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
            StringAssert.Contains(entries[1], "occurrences=21");
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
        StringAssert.Contains(state.DescribeChanges(snapshot)!, "playPause=True");
        Assert.IsNull(state.DescribeChanges(snapshot with { Duration = 101.1 }), "其它状态行已记录的时长也应成为阈值基准。");
        StringAssert.Contains(state.DescribeChanges(snapshot with { Duration = 101.7 })!, "duration=");
        StringAssert.Contains(state.DescribeChanges(snapshot with { Duration = 0, IsPlaying = false })!, "playing=False");
    }

    [TestMethod]
    public async Task ConcurrentRetrievalsProduceOneIndependentSummaryEach()
    {
        var previous = SettingsManager.Current;
        var directory = Path.Combine(Path.GetTempPath(), $"AFMediaBar-log-{Guid.NewGuid():N}");
        var gates = new Dictionary<string, TaskCompletionSource<LyricsResult?>>
        {
            ["Track-A"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["Track-B"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        try
        {
            SettingsManager.Current = new AppSettings
            {
                LyricsSource = LyricsSourceSettings.Default with
                {
                    Rule = LyricsSourceRule.Manual, AttemptMode = LyricsSourceAttemptMode.Parallel,
                    ResultMode = LyricsSourceResultMode.Ordered
                }
            };
            using var log = new AppLogService(directory);
            var service = new LyricsService(new Provider("QQMusic", request => gates[request.Title].Task),
                new Provider("LRCLIB", request => Task.FromResult<LyricsResult?>(new(request.Title,
                    new LyricDocument([new LyricLine(0, 1, "lyric")], LyricsSyncType.LineSynced, "LRC")))));
            var first = service.GetLyricsAsync(new("Track-A", "artist", "album", 100, null), CancellationToken.None);
            var second = service.GetLyricsAsync(new("Track-B", "artist", "album", 100, null), CancellationToken.None);
            gates["Track-B"].SetResult(LyricsResult.NoLyrics("QQMusic"));
            await second.WaitAsync(TimeSpan.FromSeconds(5));
            gates["Track-A"].SetResult(null);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            log.Dispose();
            var entries = File.ReadAllLines(log.FilePath).Where(line => line.Contains("[Lyrics]") && line.Contains("[INF]")).ToArray();
            Assert.AreEqual(2, entries.Length);
            var firstEntry = entries.Single(line => line.Contains("title=\"Track-A\""));
            var secondEntry = entries.Single(line => line.Contains("title=\"Track-B\""));
            StringAssert.Contains(firstEntry, "source=Track-A");
            StringAssert.Contains(firstEntry, "QQMusic:FailedOrUnmatched");
            StringAssert.Contains(secondEntry, "source=Track-B");
            StringAssert.Contains(secondEntry, "QQMusic:NoLyrics");
        }
        finally
        {
            foreach (var gate in gates.Values) gate.TrySetResult(null);
            SettingsManager.Current = previous;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task InconclusiveLyricsDoNotClaimRecoveryOrResetFailureSuppression()
    {
        var previous = SettingsManager.Current;
        var directory = Path.Combine(Path.GetTempPath(), $"AFMediaBar-log-{Guid.NewGuid():N}");
        try
        {
            SettingsManager.Current = new AppSettings { LyricsSource = LyricsSourceSettings.Default };
            using var log = new AppLogService(directory);
            var calls = 0;
            var service = new LyricsService(new Provider("QQMusic", _ => ++calls == 2
                ? Task.FromResult<LyricsResult?>(null)
                : Task.FromException<LyricsResult?>(new IOException("unavailable"))));
            for (var i = 0; i < 3; i++)
                await service.GetLyricsAsync(new("Track", "artist", "album", 100, null), CancellationToken.None);
            log.Dispose();
            var entries = File.ReadAllLines(log.FilePath).Where(line => line.Contains("[Lyrics]")).ToArray();
            Assert.AreEqual(1, entries.Count(line => line.Contains("[WRN]")));
            Assert.IsFalse(entries.Any(line => line.Contains("已恢复")), "null 不能证明歌词来源恢复。");
        }
        finally
        {
            SettingsManager.Current = previous;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
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
                StringAssert.Contains(entries[0], "phase=Downloading");
                StringAssert.Contains(entries[1], "phase=Ready");
                StringAssert.Contains(entries[2], "[WRN]");
                StringAssert.Contains(entries[2], "verification failed");
            });
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task SupersededDeviceResultCannotLogSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AFMediaBar-log-{Guid.NewGuid():N}");
        var platform = new DeviceOperations();
        Task<AudioDeviceOperationResult>? first = null;
        try
        {
            using var log = new AppLogService(directory);
            using var coordinator = new AudioDeviceSwitchCoordinator(platform, log);
            var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
            var owner = new object();
            first = coordinator.SelectAsync(target, new("A", "A", "A", false), owner);
            await coordinator.SelectAsync(target, new("B", "B", "B", false), owner);
            platform.First.TrySetResult(new(AudioDeviceOperationStatus.Applied));
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            platform.Result = new(AudioDeviceOperationStatus.Partial);
            await coordinator.SelectAsync(target, new("C", "C", "C", false), owner);
            platform.Result = new(AudioDeviceOperationStatus.Unchanged);
            await coordinator.SelectAsync(target, new("C", "C", "C", false), owner);
            log.Dispose();
            var entries = File.ReadAllLines(log.FilePath).Where(line => line.Contains("设备切换结果")).ToArray();
            Assert.AreEqual(2, entries.Length);
            StringAssert.Contains(entries[0], "device=B");
            StringAssert.Contains(entries[0], "[INF]");
            StringAssert.Contains(entries[1], "status=Partial");
            StringAssert.Contains(entries[1], "[WRN]");
        }
        finally
        {
            platform.First.TrySetResult(new(AudioDeviceOperationStatus.Cancelled));
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class DeviceOperations : IAudioDeviceSwitchOperations
    {
        private int _calls;
        internal TaskCompletionSource<AudioDeviceOperationResult> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal AudioDeviceOperationResult Result { get; set; } = new(AudioDeviceOperationStatus.Applied);
        public int Version => 0;
        public int PolicyVersion => 0;
        public bool IsAllowed(AudioDeviceOption device) => true;
        public event EventHandler? Changed { add { } remove { } }
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AudioDeviceOption>>([]);
        public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult(new ApplicationAudioRoute(null, false, true));
        public Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent) =>
            Interlocked.Increment(ref _calls) == 1 ? First.Task : Task.FromResult(Result);
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

    private sealed class Provider(string name, Func<LyricsRequest, Task<LyricsResult?>> query) : ILyricsProvider
    {
        public string SourceName => name;
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken) => query(request);
    }
}
