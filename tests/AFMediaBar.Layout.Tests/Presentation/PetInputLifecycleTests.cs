// 防止多屏宠物重复采样、隐藏后保留订阅，以及 UI 队列中的旧点击被新租约重放。
// 使用可控屏幕坐标与时间，不安装 Hook 或发送全局输入。
using System.Diagnostics;
using System.Windows;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>共享宠物输入的租约与迟到点击回归。</summary>
[TestClass]
public sealed class PetInputLifecycleTests
{
    [TestMethod]
    public void VisibleConsumersShareSamplingAndLastReleaseStopsIt()
    {
        StaTest.Run(dispatcher =>
        {
            var position = new Point(-1200, 600);
            var reads = 0;
            using var input = new PetInputService(dispatcher, () => { reads++; return position; });
            var firstFrames = new List<PetInputSnapshot>();
            var secondFrames = new List<PetInputSnapshot>();
            var first = input.Subscribe(firstFrames.Add);
            var second = input.Subscribe(secondFrames.Add);
            Assert.IsTrue(input.IsSampling);
            reads = 0;
            position = new Point(-800, 300);
            input.SamplePointer();
            Assert.AreEqual(1, reads, "多个宠物应共用一次原生读取。");
            Assert.AreEqual(position, firstFrames[^1].ScreenPosition);
            Assert.AreEqual(position, secondFrames[^1].ScreenPosition);
            input.SamplePointer();
            Assert.AreEqual(2, firstFrames.Count, "静止指针不重复发布。");
            first.Dispose();
            first.Dispose();
            input.PublishPress(position, Stopwatch.GetTimestamp());
            Assert.IsFalse(firstFrames[^1].Blink);
            Assert.IsTrue(secondFrames[^1].Blink);
            Assert.IsTrue(input.IsSampling);
            second.Dispose();
            Assert.IsFalse(input.IsSampling);
            var previousReads = reads;
            input.SamplePointer();
            Assert.AreEqual(previousReads, reads, "最后租约释放后不再读取鼠标。");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ResubscribingDropsQueuedClicksAndDisposalRejectsFurtherPublication()
    {
        StaTest.Run(dispatcher =>
        {
            var position = new Point(100, 200);
            using var input = new PetInputService(dispatcher, () => position);
            using var oldLease = input.Subscribe(_ => { });
            var queuedAt = Stopwatch.GetTimestamp();
            oldLease.Dispose();
            position = new Point(500, 900);
            var frames = new List<PetInputSnapshot>();
            using var resumed = input.Subscribe(frames.Add);
            Assert.AreEqual(position, frames.Single().ScreenPosition);
            input.PublishPress(position, queuedAt);
            Assert.AreEqual(1, frames.Count, "恢复后不能补播隐藏前排队的点击。");
            input.PublishPress(position, Stopwatch.GetTimestamp());
            Assert.IsTrue(frames[^1].Blink);
            input.Dispose();
            input.Dispose();
            var before = frames.Count;
            input.PublishPress(position, Stopwatch.GetTimestamp());
            input.SamplePointer();
            Assert.AreEqual(before, frames.Count);
            Assert.IsFalse(input.IsSampling);
            return Task.CompletedTask;
        });
    }
}
