using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class DynamicIslandMotionTests
{
    [TestMethod]
    public void UnevenFramePartitionsProduceTheSamePositionAndVelocity()
    {
        var initial = new IslandSpringFrame(0.15, -1.2);
        var whole = DynamicIslandMotion.Advance(initial, 1, 0.217);
        var partitioned = initial;
        double[] intervals = [0.013, 0.027, 0.008, 0.061, 0.108];
        foreach (var elapsed in intervals)
            partitioned = DynamicIslandMotion.Advance(partitioned, 1, elapsed);

        Assert.AreEqual(whole.Value, partitioned.Value, 1e-11);
        Assert.AreEqual(whole.Velocity, partitioned.Velocity, 1e-11);
    }

    [TestMethod]
    public void RetargetingPreservesIncomingPositionAndMomentum()
    {
        var incoming = DynamicIslandMotion.Advance(new IslandSpringFrame(0, 0), 1, 0.055);
        var retargeted = DynamicIslandMotion.Advance(incoming, 0, 0);
        const double elapsed = 1e-7;
        var continuing = DynamicIslandMotion.Advance(retargeted, 0, elapsed);

        Assert.AreEqual(incoming, retargeted);
        Assert.IsTrue(continuing.Value > incoming.Value, "Reversing the target must not instantly reverse momentum.");
        Assert.AreEqual(incoming.Velocity, (continuing.Value - incoming.Value) / elapsed, 0.001);
        Assert.AreEqual(incoming.Velocity, continuing.Velocity, 0.001);

        var reversed = DynamicIslandMotion.Advance(incoming, 0, 0.08);
        Assert.IsTrue(reversed.Value < incoming.Value);
        Assert.IsTrue(reversed.Velocity < 0);
    }

    [TestMethod]
    public void SpringOvershootsSlightlyThenSettlesWithinTheInteractionWindow()
    {
        var frame = new IslandSpringFrame(0, 0);
        var peak = frame.Value;
        double? settledAt = null;
        for (var index = 1; index <= 120; index++)
        {
            frame = DynamicIslandMotion.Advance(frame, 1, 1.0 / 120);
            peak = Math.Max(peak, frame.Value);
            if (settledAt is null && DynamicIslandMotion.IsSettled(frame, 1))
                settledAt = index / 120.0;
        }

        Assert.IsTrue(peak > 1 && peak < 1.08, "The island should have a restrained, visible spring overshoot.");
        Assert.IsTrue(settledAt is >= 0.35 and <= 0.45, "A resting-to-resting transition should settle in roughly 350–450 ms.");
        Assert.IsTrue(DynamicIslandMotion.IsSettled(frame, 1));
        Assert.AreEqual(1, frame.Value, 0.0005);
    }

    [TestMethod]
    public void CrossingTheTargetAtSpeedDoesNotCountAsSettled()
    {
        Assert.IsFalse(DynamicIslandMotion.IsSettled(new IslandSpringFrame(1, 2), 1));
        Assert.IsFalse(DynamicIslandMotion.IsSettled(new IslandSpringFrame(0, 0), 1));
        Assert.IsTrue(DynamicIslandMotion.IsSettled(new IslandSpringFrame(1, 0), 1));
        Assert.IsFalse(DynamicIslandMotion.IsSettled(new IslandSpringFrame(double.NaN, 0), 1));
    }

    [TestMethod]
    public void NonPositiveOrNonFiniteElapsedTimePreservesTheFrame()
    {
        var incoming = new IslandSpringFrame(0.4, 3);
        double[] intervals = [0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (var elapsed in intervals)
            Assert.AreEqual(incoming, DynamicIslandMotion.Advance(incoming, 1, elapsed));
    }

    [TestMethod]
    public void InvalidStateRecoversWithoutPropagatingNonFiniteCoordinates()
    {
        var invalidState = DynamicIslandMotion.Advance(new IslandSpringFrame(double.NaN, double.PositiveInfinity), 1, 0.02);
        var invalidTarget = DynamicIslandMotion.Advance(new IslandSpringFrame(0.4, 0), double.NaN, 0.02);
        var noFiniteAnchor = DynamicIslandMotion.Advance(new IslandSpringFrame(double.NegativeInfinity, double.NaN), double.PositiveInfinity, 0.02);
        var longPause = DynamicIslandMotion.Advance(new IslandSpringFrame(0, 0), 1, double.MaxValue);

        Assert.AreEqual(new IslandSpringFrame(1, 0), invalidState);
        Assert.AreEqual(new IslandSpringFrame(0.4, 0), invalidTarget);
        Assert.AreEqual(new IslandSpringFrame(0, 0), noFiniteAnchor);
        Assert.AreEqual(new IslandSpringFrame(1, 0), longPause);
    }
}
