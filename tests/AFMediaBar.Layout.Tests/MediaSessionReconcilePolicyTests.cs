using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 自动重连看门狗的两段判定：<c>ShouldProbe</c> 只做时间/档位门控（先于系统查询），<c>DecideAction</c> 才按系统会话数决定动作。
/// The two halves of the auto-reconcile watchdog: <c>ShouldProbe</c> only gates time and prune level (before any system query), while
/// <c>DecideAction</c> picks the action from the OS session count.
/// </summary>
[TestClass]
public sealed class MediaSessionReconcilePolicyTests
{
    private static MediaSessionReconcileProbeSignals Signals(
        bool isConnected = false,
        bool isGraceActive = false,
        bool isArmed = false,
        double secondsSinceLastAttempt = 0,
        MemoryPruneLevel pruneLevel = MemoryPruneLevel.None,
        int consecutiveSlowReconciles = 0) =>
        new(
            isConnected,
            isGraceActive,
            isArmed,
            TimeSpan.FromSeconds(secondsSinceLastAttempt),
            pruneLevel,
            consecutiveSlowReconciles);

    [TestMethod]
    public void DisconnectedSessionIsProbedOnTheCooldown()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(Signals(secondsSinceLastAttempt: 4)));
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(Signals(secondsSinceLastAttempt: 5)));
    }

    [TestMethod]
    public void ArmedWindowUsesTheFastCadence()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isArmed: true, secondsSinceLastAttempt: 0.9)));
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isArmed: true, secondsSinceLastAttempt: 1.0)));
    }

    [TestMethod]
    public void ConnectedOrGracedSessionIsNeverProbed()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isConnected: true, isArmed: true, secondsSinceLastAttempt: 3600)));
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isGraceActive: true, isArmed: true, secondsSinceLastAttempt: 3600)));
    }

    [TestMethod]
    public void ProbeGatingDoesNotDependOnTheOsSessionCount()
    {
        // 门控签名刻意没有系统会话数：探测时隙先被消耗，系统查询只发生在时隙内。这样"没有媒体"时不会每秒查询一次。
        // The gating signature deliberately has no OS session count: the probe slot is consumed first and the system query only happens
        // inside a slot, so a "no media" state cannot be queried once per second.
        var signals = Signals(secondsSinceLastAttempt: 5);
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(signals));
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.DecideAction(osSessionCount: 0, consecutiveFailedReconciles: 0));
    }

    [TestMethod]
    public void PruneLevelSlowsOrStopsTheWatchdog()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: 6, pruneLevel: MemoryPruneLevel.Idle)));
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: 10, pruneLevel: MemoryPruneLevel.Idle)));

        // 显示关闭与睡眠档位下不探测：屏幕已经黑了，媒体栏没人看。
        // No probing under display-off or suspend: the screen is dark and nobody reads the bar.
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isArmed: true, secondsSinceLastAttempt: 3600, pruneLevel: MemoryPruneLevel.DisplayOff)));
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(isArmed: true, secondsSinceLastAttempt: 3600, pruneLevel: MemoryPruneLevel.Suspended)));
    }

    [TestMethod]
    public void SlowCallsBackOffExponentially()
    {
        // 一次慢调用把 5 秒的间隔翻倍；退避连续累积，直到恢复连接时清零。
        // One slow attempt doubles the five-second interval; backoff accumulates until a connected snapshot clears it.
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: 9, consecutiveSlowReconciles: 1)));
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: 10, consecutiveSlowReconciles: 1)));

        var maximum = MediaSessionReconcilePolicy.DisconnectedInterval
            * (1 << MediaSessionReconcilePolicy.MaximumBackoffShift);
        Assert.IsFalse(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: maximum.TotalSeconds - 1, consecutiveSlowReconciles: 99)));
        Assert.IsTrue(MediaSessionReconcilePolicy.ShouldProbe(
            Signals(secondsSinceLastAttempt: maximum.TotalSeconds, consecutiveSlowReconciles: 99)));
    }

    [TestMethod]
    public void NoSessionsMeansNoMediaAndNoAction()
    {
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.DecideAction(osSessionCount: 0, consecutiveFailedReconciles: 0));
    }

    [TestMethod]
    public void AFailedProbeStillAttemptsAForceUpdate()
    {
        // 查询失败（负值）时分不清"没有媒体"还是"库坏了"，做一次保守的 ForceUpdate。
        // A failed query (negative) cannot tell "no media" from "the library is broken", so it does one conservative ForceUpdate.
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.DecideAction(osSessionCount: -1, consecutiveFailedReconciles: 0));
    }

    [TestMethod]
    public void RepeatedFailuresWithLiveSessionsEscalateToACatalogRebuild()
    {
        // 系统有会话、ForceUpdate 却连续失败：字典里残留了失效条目，下一次改为重建目录。
        // The OS has sessions while ForceUpdate keeps failing: the dictionary holds a dead entry, so the next attempt rebuilds.
        var belowThreshold = MediaSessionReconcilePolicy.CatalogRestartFailureThreshold - 1;
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.DecideAction(osSessionCount: 1, consecutiveFailedReconciles: belowThreshold));
        Assert.AreEqual(
            MediaSessionReconcileAction.RestartCatalog,
            MediaSessionReconcilePolicy.DecideAction(
                osSessionCount: 1,
                consecutiveFailedReconciles: MediaSessionReconcilePolicy.CatalogRestartFailureThreshold));
    }

    [TestMethod]
    public void RepeatedCatalogRebuildsFallBackToPlainForceUpdates()
    {
        // 全屏独占游戏挡住系统查询时，重建目录救不回来；连续重建到上限后必须退回 ForceUpdate，
        // 否则看门狗会每几秒重造一次媒体目录（真机日志里出现过 75 秒内 14 次）。
        // A fullscreen exclusive game that blocks the OS query cannot be healed by rebuilding; past the limit the action must
        // fall back to ForceUpdate, otherwise the catalog is rebuilt every few seconds (a real log held 14 rebuilds in 75 s).
        var failures = MediaSessionReconcilePolicy.CatalogRestartFailureThreshold;
        Assert.AreEqual(
            MediaSessionReconcileAction.RestartCatalog,
            MediaSessionReconcilePolicy.DecideAction(
                osSessionCount: 1,
                consecutiveFailedReconciles: failures,
                consecutiveCatalogRestarts: MediaSessionReconcilePolicy.CatalogRestartLimit - 1));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.DecideAction(
                osSessionCount: 1,
                consecutiveFailedReconciles: failures,
                consecutiveCatalogRestarts: MediaSessionReconcilePolicy.CatalogRestartLimit));
    }

    [TestMethod]
    public void SlowCallThresholdSeparatesStallsFromNormalCalls()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.IsSlowCall(
            MediaSessionReconcilePolicy.SlowCallThreshold - TimeSpan.FromMilliseconds(1)));
        Assert.IsTrue(MediaSessionReconcilePolicy.IsSlowCall(MediaSessionReconcilePolicy.SlowCallThreshold));
    }
}
