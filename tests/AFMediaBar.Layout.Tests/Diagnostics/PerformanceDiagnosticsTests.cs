// 防止错误识别 GPU 进程和引擎；同时约束 Release 不暴露持续诊断入口。
using AFMediaBar.Classes.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>诊断身份解析与构建入口隔离。</summary>
[TestClass]
public sealed class PerformanceDiagnosticsTests
{
    [TestMethod]
    public void PerformanceCommandsMatchBuildConfiguration()
    {
#if DEBUG
        Assert.IsNotNull(DeveloperActionCatalog.Find("performance status"));
        Assert.IsNotNull(DeveloperActionCatalog.Find("performance stop"));
#else
        Assert.IsFalse(DeveloperActionCatalog.Actions.Any(item => item.Command.StartsWith("performance ", StringComparison.Ordinal)));
        Assert.IsNull(typeof(DeveloperActionCatalog).Assembly.GetType("AFMediaBar.Classes.Services.Diagnostics.DebugPerformanceRecorder"));
#endif
    }

#if DEBUG
    [TestMethod]
    public void OwnedGpuDropsUnrelatedAndZeroEnginesButKeepsUnknownValues()
    {
        var captured = DateTimeOffset.UtcNow;
        DebugProcessResources[] resources = [new(1, captured.AddMinutes(-1), "AF", 0, 0, null, null, null)];
        GpuProcessEngineUsage[] samples = [new(1, "gpu", "idle", 0), new(1, "gpu", "active", 0.1), new(99, "other-gpu", "3D", 95)];
        var owned = GpuProcessEngineUsage.SummarizeOwned(samples, resources, captured, 1);
        Assert.AreEqual(1, owned.Length);
        Assert.AreEqual("active", owned[0].Engine);
        var unknown = GpuProcessEngineUsage.SummarizeOwned([new(1, "gpu", "3D", 10)], [], captured, 1);
        Assert.AreEqual(1, unknown.Length);
        Assert.IsNull(unknown[0].AfPercent);
    }

    [TestMethod]
    public void ReusedPidCannotInheritAnOlderGpuSampleAndEnginesAreNotCombined()
    {
        var captured = DateTimeOffset.UtcNow;
        DebugProcessResources[] resources =
        [
            new(1, captured.AddMinutes(-1), "AF", 0, 0, null, null, null),
            new(2, captured.AddSeconds(1), "WebView2", 0, 0, null, null, null),
            new(3, captured.AddSeconds(-1), "WebView2", 0, 0, null, null, null)
        ];
        GpuProcessEngineUsage[] samples = [new(1, "gpu-a", "3D", 10), new(2, "gpu-a", "3D", 80), new(3, "gpu-a", "Copy", 20)];
        var totals = GpuProcessEngineUsage.SummarizeOwned(samples, resources, captured, 1);
        Assert.AreEqual(2, totals.Length);
        Assert.AreEqual(10d, totals.Single(item => item.Engine == "3D").AfPercent);
        Assert.IsNull(totals.Single(item => item.Engine == "3D").WebViewPercent);
        Assert.AreEqual(20d, totals.Single(item => item.Engine == "Copy").WebViewPercent);
        Assert.IsFalse(GpuProcessEngineUsage.CanAssociateProcess(resources[1].StartedAt, captured));
    }

    [TestMethod]
    public void GpuIdentityKeepsPhysicalAdapterAndEngineSeparate()
    {
        Assert.IsTrue(GpuProcessEngineUsage.TryParse("pid_42_luid_0x00000000_0x00000001_phys_0_eng_2_engtype_3D", 65.5, out var first));
        Assert.AreEqual(42, first!.Pid);
        Assert.AreEqual("luid_0x00000000_0x00000001_phys_0", first.Adapter);
        Assert.AreEqual("eng_2_engtype_3D", first.Engine);
        Assert.AreEqual(65.5, first.Percent);
        Assert.IsTrue(GpuProcessEngineUsage.TryParse("pid_43_luid_0x00000000_0x00000001_phys_0_eng_3_engtype_Copy", 15, out var second));
        Assert.AreEqual(first.Adapter, second!.Adapter);
        Assert.AreNotEqual(first.Engine, second.Engine);
        Assert.IsFalse(GpuProcessEngineUsage.TryParse("pid_unknown_luid_0_phys_0_eng_1", 20, out _));
        Assert.IsFalse(GpuProcessEngineUsage.TryParse("pid_42_luid_0_phys_0_eng_1", double.NaN, out _));
    }
#endif
}
