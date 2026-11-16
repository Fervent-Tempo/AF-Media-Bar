// 只验证渲染通道失效的识别与重启门禁；不重启应用，也不创建窗口。
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Services.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>合成通道失效的识别范围与自动重启门禁回归。</summary>
[TestClass]
public sealed class RenderChannelFailureTests
{
    [TestMethod]
    public void OnlyRenderThreadFailureCountsAsRenderChannelFailure()
    {
        Assert.IsTrue(RenderChannelFailurePolicy.IsRenderChannelFailure(
            new COMException("UCEERR_RENDERTHREADFAILURE", unchecked((int)0x88980406))));
        // 同一个 DLL 的其它 COM 失败、普通异常和空值都不能触发重启。
        Assert.IsFalse(RenderChannelFailurePolicy.IsRenderChannelFailure(
            new COMException("UCEERR_UNKNOWN", unchecked((int)0x88980003))));
        Assert.IsFalse(RenderChannelFailurePolicy.IsRenderChannelFailure(new InvalidOperationException()));
        Assert.IsFalse(RenderChannelFailurePolicy.IsRenderChannelFailure(null));
    }

    [TestMethod]
    public void AutomaticRestartRequiresRunningLongEnoughToNotLoop()
    {
        // 重启实例刚起来就又失效时只退出，避免 DWM 持续失效时反复拉起进程。
        Assert.IsFalse(RenderChannelFailurePolicy.CanRestart(
            RenderChannelFailurePolicy.MinimumUptimeBeforeRestart - TimeSpan.FromSeconds(1)));
        Assert.IsTrue(RenderChannelFailurePolicy.CanRestart(RenderChannelFailurePolicy.MinimumUptimeBeforeRestart));
        Assert.IsTrue(RenderChannelFailurePolicy.CanRestart(TimeSpan.FromMinutes(5)));
    }
}
