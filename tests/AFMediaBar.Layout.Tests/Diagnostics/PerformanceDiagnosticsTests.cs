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
