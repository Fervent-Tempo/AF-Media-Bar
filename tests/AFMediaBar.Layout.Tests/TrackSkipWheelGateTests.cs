using System.Diagnostics;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止滚轮突发事件连续向媒体会话发送切歌命令。/ Guards against a wheel burst issuing repeated track-skip commands.</summary>
[TestClass]
public sealed class TrackSkipWheelGateTests
{
    [TestMethod]
    public void BurstAllowsOneSkipUntilScrollingHasStopped()
    {
        var gate = new TrackSkipWheelGate();
        var start = Stopwatch.GetTimestamp();

        Assert.IsTrue(gate.TryBegin(start));
        gate.Complete();
        Assert.IsFalse(gate.TryBegin(start + Milliseconds(100)));
        Assert.IsFalse(gate.TryBegin(start + Milliseconds(450)));
        Assert.IsTrue(gate.TryBegin(start + Milliseconds(1000)));
        gate.Complete();
    }

    [TestMethod]
    public void PendingSkipRejectsAnotherWheelEvenAfterQuietPeriod()
    {
        var gate = new TrackSkipWheelGate();
        var start = Stopwatch.GetTimestamp();

        Assert.IsTrue(gate.TryBegin(start));
        Assert.IsFalse(gate.TryBegin(start + Milliseconds(700)));
        gate.Complete();
        Assert.IsFalse(gate.TryBegin(start + Milliseconds(800)));
        Assert.IsTrue(gate.TryBegin(start + Milliseconds(1400)));
        gate.Complete();
    }

    private static long Milliseconds(int value) => value * Stopwatch.Frequency / 1000;
}
