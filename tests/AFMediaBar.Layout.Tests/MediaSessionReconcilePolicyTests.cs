using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 自动重连看门狗的判定策略：只在断连、未处于宽限期、系统确有会话且冷却已过时动作；
/// 按剪枝档位与慢调用退避调整节奏，并在 ForceUpdate 连续失败时改为重建目录。
/// Decision policy of the auto-reconcile watchdog: act only while disconnected, outside the grace period, with sessions the OS
/// actually publishes, and past the cooldown; cadence follows the prune level and slow-call backoff, and repeated ForceUpdate
/// failures escalate to a catalog rebuild.
/// </summary>
[TestClass]
public sealed class MediaSessionReconcilePolicyTests
{
    private static MediaSessionReconcileSignals Signals(
        bool isConnected = false,
        bool isGraceActive = false,
        bool isArmed = false,
        double secondsSinceLastAttempt = 0,
        MemoryPruneLevel pruneLevel = MemoryPruneLevel.None,
        int consecutiveSlowReconciles = 0,
        int osSessionCount = 1,
        int consecutiveFailedReconciles = 0) =>
        new(
            isConnected,
            isGraceActive,
            isArmed,
            TimeSpan.FromSeconds(secondsSinceLastAttempt),
            pruneLevel,
            consecutiveSlowReconciles,
            osSessionCount,
            consecutiveFailedReconciles);

    [TestMethod]
    public void DisconnectedSessionForceUpdatesOnTheCooldown()
    {
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(secondsSinceLastAttempt: 4)));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(Signals(secondsSinceLastAttempt: 5)));
    }

    [TestMethod]
    public void ArmedWindowUsesTheFastCadence()
    {
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(isArmed: true, secondsSinceLastAttempt: 0.9)));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(Signals(isArmed: true, secondsSinceLastAttempt: 1.0)));
    }

    [TestMethod]
    public void ConnectedOrGracedSessionNeverActs()
    {
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(isConnected: true, isArmed: true, secondsSinceLastAttempt: 3600)));
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(isGraceActive: true, isArmed: true, secondsSinceLastAttempt: 3600)));
    }

    [TestMethod]
    public void NoSessionsFromTheOsMeansNoMediaAndNoWork()
    {
        // 系统确实没有会话：ForceUpdate 变不出会话，因此即使冷却已过也不动作。
        // The OS genuinely has no sessions: ForceUpdate cannot conjure one, so nothing happens even past the cooldown.
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(secondsSinceLastAttempt: 3600, osSessionCount: 0)));
    }

    [TestMethod]
    public void PruneLevelSlowsOrStopsTheWatchdog()
    {
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(
                Signals(secondsSinceLastAttempt: 6, pruneLevel: MemoryPruneLevel.Idle)));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(
                Signals(secondsSinceLastAttempt: 10, pruneLevel: MemoryPruneLevel.Idle)));

        // 显示关闭与睡眠档位下不做任何重同步：屏幕已经黑了，媒体栏没人看。
        // No reconcile under display-off or suspend: the screen is dark and nobody reads the bar.
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(
                Signals(isArmed: true, secondsSinceLastAttempt: 3600, pruneLevel: MemoryPruneLevel.DisplayOff)));
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(
                Signals(isArmed: true, secondsSinceLastAttempt: 3600, pruneLevel: MemoryPruneLevel.Suspended)));
    }

    [TestMethod]
    public void SlowCallsBackOffExponentially()
    {
        // 一次慢调用把 5 秒的间隔翻倍；退避连续累积，直到恢复连接时清零。
        // One slow call doubles the five-second interval; backoff accumulates until a connected snapshot clears it.
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(Signals(secondsSinceLastAttempt: 9, consecutiveSlowReconciles: 1)));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(Signals(secondsSinceLastAttempt: 10, consecutiveSlowReconciles: 1)));

        var maximum = MediaSessionReconcilePolicy.DisconnectedInterval
            * (1 << MediaSessionReconcilePolicy.MaximumBackoffShift);
        Assert.AreEqual(
            MediaSessionReconcileAction.None,
            MediaSessionReconcilePolicy.Decide(
                Signals(secondsSinceLastAttempt: maximum.TotalSeconds - 1, consecutiveSlowReconciles: 99)));
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(
                Signals(secondsSinceLastAttempt: maximum.TotalSeconds, consecutiveSlowReconciles: 99)));
    }

    [TestMethod]
    public void RepeatedFailuresWithLiveSessionsEscalateToACatalogRebuild()
    {
        // 系统有会话、ForceUpdate 却连续失败：字典里残留了失效条目，下一次改为重建目录。
        // The OS has sessions while ForceUpdate keeps failing: the dictionary holds a dead entry, so the next attempt rebuilds.
        var belowThreshold = MediaSessionReconcilePolicy.CatalogRestartFailureThreshold - 1;
        Assert.AreEqual(
            MediaSessionReconcileAction.ForceUpdate,
            MediaSessionReconcilePolicy.Decide(
                Signals(secondsSinceLastAttempt: 5, consecutiveFailedReconciles: belowThreshold)));
        Assert.AreEqual(
            MediaSessionReconcileAction.RestartCatalog,
            MediaSessionReconcilePolicy.Decide(
                Signals(
                    secondsSinceLastAttempt: 5,
                    consecutiveFailedReconciles: MediaSessionReconcilePolicy.CatalogRestartFailureThreshold)));
    }

    [TestMethod]
    public void SlowCallThresholdSeparatesStallsFromNormalCalls()
    {
        Assert.IsFalse(MediaSessionReconcilePolicy.IsSlowCall(
            MediaSessionReconcilePolicy.SlowCallThreshold - TimeSpan.FromMilliseconds(1)));
        Assert.IsTrue(MediaSessionReconcilePolicy.IsSlowCall(MediaSessionReconcilePolicy.SlowCallThreshold));
    }
}
