#if DEBUG
// 防止受限 GPU 进程反复触发首次异常，同时保留冷却后的恢复与 PID 复用保护。
using System.ComponentModel;
using AFMediaBar.Classes.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>GPU 身份读取的权限冷却、容量及采样身份边界。</summary>
[TestClass]
public sealed class GpuProcessIdentityCooldownTests
{
    [TestMethod]
    public void DeniedProcessRetainsEnginesAndRecoversOnlyAfterCooldown()
    {
        var clock = new Clock();
        var captured = DateTimeOffset.UtcNow;
        var attempts = 0;
        var resolver = new DebugGpuProcessIdentityResolver(_ =>
        {
            if (++attempts == 1) throw new Win32Exception(5);
            return ("recovered", captured.AddMinutes(-1));
        }, clock);
        GpuProcessEngineUsage[] engines = [new(42, "gpu", "3D", 20), new(42, "gpu", "Copy", 10)];
        var first = resolver.DescribeTopGpu(engines, captured);
        Assert.AreEqual(2, first.Length);
        Assert.IsTrue(first.All(item => item.Name is null && item.StartedAt is null));
        Assert.AreEqual(20d, first[0].Percent);
        clock.Advance(TimeSpan.FromSeconds(59));
        resolver.DescribeTopGpu(engines, captured);
        Assert.AreEqual(1, attempts);
        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = resolver.DescribeTopGpu(engines, captured);
        Assert.AreEqual(2, attempts);
        Assert.IsTrue(recovered.All(item => item.Name == "recovered"));
    }

    [TestMethod]
    public void SuccessfulIdentityIsRecheckedAndNewerProcessCannotInheritSample()
    {
        var captured = DateTimeOffset.UtcNow;
        var attempts = 0;
        var resolver = new DebugGpuProcessIdentityResolver(_ =>
            ++attempts == 1 ? ("old", captured.AddMinutes(-1)) : ("new", captured.AddSeconds(1)));
        GpuProcessEngineUsage[] engines = [new(42, "gpu", "3D", 20)];
        Assert.AreEqual("old", resolver.DescribeTopGpu(engines, captured)[0].Name);
        var reused = resolver.DescribeTopGpu(engines, captured)[0];
        Assert.AreEqual(2, attempts);
        Assert.IsNull(reused.Name);
        Assert.IsNull(reused.StartedAt);
    }

    [TestMethod]
    public void DenialCacheIsBoundedAndDoesNotSurviveRecordingSession()
    {
        var clock = new Clock();
        var attempts = new Dictionary<int, int>();
        (string, DateTimeOffset) Denied(int pid)
        {
            attempts[pid] = attempts.GetValueOrDefault(pid) + 1;
            throw new Win32Exception(5);
        }
        var resolver = new DebugGpuProcessIdentityResolver(Denied, clock);
        for (var pid = 1; pid <= 129; pid++)
        {
            resolver.DescribeTopGpu([new(pid, "gpu", "3D", 10)], DateTimeOffset.UtcNow);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        resolver.DescribeTopGpu([new(1, "gpu", "3D", 10)], DateTimeOffset.UtcNow);
        Assert.AreEqual(2, attempts[1]);
        resolver.DescribeTopGpu([new(129, "gpu", "3D", 10)], DateTimeOffset.UtcNow);
        Assert.AreEqual(1, attempts[129]);
        new DebugGpuProcessIdentityResolver(Denied, clock)
            .DescribeTopGpu([new(129, "gpu", "3D", 10)], DateTimeOffset.UtcNow);
        Assert.AreEqual(2, attempts[129]);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
#endif
