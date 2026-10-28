using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace AFMediaBar.Layout.Tests;

/// <summary>仅替换原生状态和停止请求；防止启动线程覆盖停止后，清理流程再次进入无限 Join。</summary>
[TestClass]
public sealed class SpectrumCaptureShutdownTests
{
    [TestMethod]
    public async Task LateStartCannotLoseStopRequestOrCompleteCleanupWhileStillRunning()
    {
        var state = CaptureState.Starting;
        var requests = 0;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AudioMonitorService.StopCaptureAsync(() => state, () =>
        {
            requests++;
            // 模拟已确认的底层时序：第一个 Stopping 被迟到的 Start 覆盖。
            state = requests == 1 ? CaptureState.Capturing : CaptureState.Stopped;
            if (state == CaptureState.Stopped) stopped.SetResult();
        }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(stopped.Task.IsCompletedSuccessfully);
        Assert.AreEqual(CaptureState.Stopped, state);

        await AudioMonitorService.StopCaptureAsync(() => state,
            () => Assert.Fail("已经停止的录音线程无需再次发出停止请求。"));
    }
}
