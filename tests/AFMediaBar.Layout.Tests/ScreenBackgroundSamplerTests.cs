// 检查取色请求的边界、取消和释放后行为。
using System.Windows;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ScreenBackgroundSamplerTests
{
    [TestMethod]
    public async Task CanceledAndInvalidRequestsReturnNoPixelsWithoutThrowing()
    {
        using var sampler = new ScreenBackgroundSampler();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.AreEqual(0, (await sampler.SampleAsync(new Int32Rect(0, 0, 100, 100), canceled.Token)).Count);
        Assert.AreEqual(0, (await sampler.SampleAsync(Int32Rect.Empty, default)).Count);
        Assert.AreEqual(0, (await sampler.SampleAsync(new Int32Rect(int.MaxValue - 1, 0, 1, 1), default)).Count);
    }

    [TestMethod]
    public async Task DisposeIsIdempotentAndRejectsLateRequests()
    {
        var sampler = new ScreenBackgroundSampler();
        sampler.Dispose();
        sampler.Dispose();
        Assert.AreEqual(0, (await sampler.SampleAsync(new Int32Rect(0, 0, 100, 100), default)).Count);
    }
}
