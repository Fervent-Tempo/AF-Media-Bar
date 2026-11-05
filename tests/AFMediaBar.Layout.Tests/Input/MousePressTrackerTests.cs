// 防止异步确认期间串用按压、重复消费或误吞下一次点击；不发送全局输入。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>Raw Input 与本地消息时序交错时的组合滚轮抑制。</summary>
[TestClass]
public sealed class MousePressTrackerTests
{
    [TestMethod]
    public void ChordWaitsForReleaseAndIsConsumedOnce()
    {
        var tracker = new MousePressTracker();
        tracker.SetDown(true, 100);
        tracker.Wheel(150);
        Assert.IsFalse(tracker.Consume(160));
        tracker.SetDown(false, 200);
        Assert.IsTrue(tracker.Consume(200));
        Assert.IsFalse(tracker.Consume(200));
    }

    [TestMethod]
    public void UnconsumedOldChordDoesNotSwallowNextRealClick()
    {
        var tracker = Chord();
        tracker.SetDown(true, 210);
        tracker.SetDown(false, 220);
        Assert.IsFalse(tracker.Consume(220));
        Assert.IsTrue(tracker.Consume(200), "迟到的旧点击仍按自己的事件时间消费。");
        Assert.IsFalse(tracker.Consume(220));
    }

    [TestMethod]
    public void LateWheelBelongsToItsOriginalPress()
    {
        var tracker = new MousePressTracker();
        tracker.SetDown(true, 100);
        tracker.SetDown(false, 200);
        tracker.SetDown(true, 210);
        tracker.Wheel(150);
        tracker.SetDown(false, 220);
        Assert.IsTrue(tracker.Consume(200));
        Assert.IsFalse(tracker.Consume(220));
    }

    [TestMethod]
    public void NewLocalPressCannotInheritAnOlderRawChord()
    {
        var tracker = Chord();
        Assert.IsFalse(tracker.Consume(210, localPressStart: 205));
        Assert.IsTrue(tracker.Consume(200, localPressStart: 100));
    }

    [TestMethod]
    public void ExpiryAndResetCannotAffectLaterInput()
    {
        var tracker = Chord();
        Assert.IsFalse(tracker.Consume(951));
        tracker.Clear();
        Assert.IsFalse(tracker.Consume(200));
        tracker.SetDown(true, 1000);
        tracker.SetDown(false, 1001);
        Assert.IsFalse(tracker.Consume(1001));
    }

    [TestMethod]
    public void MessageTimeWrapAndDuplicateTransitionsKeepTheSamePress()
    {
        var tracker = new MousePressTracker();
        tracker.SetDown(true, uint.MaxValue - 10);
        tracker.Wheel(uint.MaxValue - 5);
        tracker.SetDown(true, uint.MaxValue - 3);
        tracker.SetDown(false, 3);
        tracker.SetDown(false, 4);
        Assert.IsTrue(tracker.Consume(5));
        Assert.IsFalse(tracker.Consume(5));
    }

    private static MousePressTracker Chord()
    {
        var tracker = new MousePressTracker();
        tracker.SetDown(true, 100); tracker.Wheel(150); tracker.SetDown(false, 200);
        return tracker;
    }
}
