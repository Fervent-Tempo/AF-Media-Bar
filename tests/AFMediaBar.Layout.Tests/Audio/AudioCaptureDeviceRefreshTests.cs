using System.Threading.Channels;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设备事件可能打断原生扫描；验证旧代际不会覆盖新端点，失败保留旧目标。</summary>
[TestClass]
public sealed class AudioCaptureDeviceRefreshTests
{
    [TestMethod]
    public async Task RefreshInterruptsPollingAndDiscardsLateResults()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var results = Channel.CreateUnbounded<AudioCaptureDeviceResolver.Resolution>();
        var attempt = 0;
        using var resolver = new AudioCaptureDeviceResolver((_, _) =>
        {
            var scan = Interlocked.Increment(ref attempt);
            if (scan == 2)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return (true, "stale");
            }
            return scan == 3 ? (false, null) : (true, scan == 1 ? "old" : "new");
        });
        resolver.Resolved += result => results.Writer.TryWrite(result);
        try
        {
            resolver.RequestScan();
            Assert.AreEqual("old", (await Next(results)).DeviceId);
            resolver.Refresh("blocked-scan");
            Assert.IsTrue(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))));
            for (var index = 0; index < 10; index++) resolver.Refresh("latest-device-notification");
            Assert.AreEqual("old", resolver.TargetDeviceId);
            release.Set();
            var next = await Next(results);
            Assert.AreEqual("new", next.DeviceId);
            Assert.AreEqual("latest-device-notification", next.Reason);
            Assert.IsTrue(next.TargetChanged);
            Assert.AreEqual(4, Volatile.Read(ref attempt), "连续通知合并，失败后补扫；旧结果不发布。");
        }
        finally
        {
            release.Set();
            resolver.Dispose();
            await resolver.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task NoDemandOrDisplayOffDefersRefreshUntilSamplingResumes()
    {
        var results = Channel.CreateUnbounded<AudioCaptureDeviceResolver.Resolution>();
        var attempt = 0;
        using var resolver = new AudioCaptureDeviceResolver((_, _) =>
        {
            Interlocked.Increment(ref attempt);
            return (true, "endpoint");
        });
        resolver.Resolved += result => results.Writer.TryWrite(result);
        try
        {
            resolver.Refresh();
            resolver.Prune(MemoryPruneLevel.DisplayOff);
            resolver.RequestScan();
            Assert.AreEqual(0, Volatile.Read(ref attempt));
            resolver.Refresh("while-paused");
            resolver.Prune(MemoryPruneLevel.None);
            var first = await Next(results);
            Assert.AreEqual("endpoint", first.DeviceId);
            resolver.Refresh("same-device");
            var second = await Next(results);
            Assert.IsFalse(second.TargetChanged);
            Assert.IsTrue(second.EnvironmentRefresh);
            Assert.IsTrue(second.Generation > first.Generation);
        }
        finally
        {
            resolver.Dispose();
            await resolver.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<AudioCaptureDeviceResolver.Resolution> Next(Channel<AudioCaptureDeviceResolver.Resolution> results) =>
        await results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
}
