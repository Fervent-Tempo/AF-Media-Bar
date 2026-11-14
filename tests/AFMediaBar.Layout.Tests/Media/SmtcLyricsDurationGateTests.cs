// 验证新曲目首读的时长稳定化：首读等待、稳定后放行，且不会改变普通时长更新去重。
using AFMediaBar.Classes.Services.Media.Smtc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Media;

[TestClass]
public sealed class SmtcLyricsDurationGateTests
{
    [TestMethod]
    public void FirstObservationDefersAndSecondMatchingObservationReleases()
    {
        var clock = new Clock();
        var gate = new SmtcLyricsDurationGate(clock, TimeSpan.FromSeconds(1));

        Assert.IsTrue(gate.ShouldDefer("track", 250));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsFalse(gate.ShouldDefer("track", 250));
    }

    [TestMethod]
    public void ZeroThenPositiveReleasesImmediately()
    {
        var clock = new Clock();
        var gate = new SmtcLyricsDurationGate(clock, TimeSpan.FromSeconds(1));

        Assert.IsTrue(gate.ShouldDefer("track", 0));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsFalse(gate.ShouldDefer("track", 230));
    }

    [TestMethod]
    public void ChangedPositiveDurationNeedsOneConfirmation()
    {
        var clock = new Clock();
        var gate = new SmtcLyricsDurationGate(clock, TimeSpan.FromSeconds(1));

        Assert.IsTrue(gate.ShouldDefer("track", 250));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsTrue(gate.ShouldDefer("track", 230));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsFalse(gate.ShouldDefer("track", 230));
    }

    [TestMethod]
    public void MaximumWaitReleasesEvenWithoutAUsableDuration()
    {
        var clock = new Clock();
        var gate = new SmtcLyricsDurationGate(clock, TimeSpan.FromSeconds(1));

        Assert.IsTrue(gate.ShouldDefer("track", 0));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(gate.ShouldDefer("track", 0));
    }

    [TestMethod]
    public void NewTrackResetsThePreviousStabilization()
    {
        var clock = new Clock();
        var gate = new SmtcLyricsDurationGate(clock, TimeSpan.FromSeconds(1));

        Assert.IsTrue(gate.ShouldDefer("first", 250));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsTrue(gate.ShouldDefer("second", 230));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.IsFalse(gate.ShouldDefer("second", 230));
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
