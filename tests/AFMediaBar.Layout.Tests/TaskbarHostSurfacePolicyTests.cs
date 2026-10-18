// 自愈判定与会话预算的纯逻辑回归；不创建窗口、不探测原生窗口。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarHostSurfacePolicyTests
{
    [TestMethod]
    public void IsStaleSample_OnlyCountsHittingTheTaskbar()
    {
        Assert.IsTrue(TaskbarHostSurfacePolicy.IsStaleSample(TaskbarHostSurfaceHit.Taskbar));
        Assert.IsFalse(TaskbarHostSurfacePolicy.IsStaleSample(TaskbarHostSurfaceHit.Host));
        Assert.IsFalse(TaskbarHostSurfacePolicy.IsStaleSample(TaskbarHostSurfaceHit.Other),
            "浮层、其他应用、全屏窗口与空桌面覆盖媒体栏位置时不能算作失绘证据。");
    }

    [TestMethod]
    public void ShouldReload_NeedsConsecutiveSamplesBeforeReloading()
    {
        var neverReloaded = TimeSpan.MaxValue;
        Assert.IsFalse(TaskbarHostSurfacePolicy.ShouldReload(TaskbarHostSurfacePolicy.RequiredStaleSamples - 1, 0, neverReloaded),
            "单次命中可能只是动画的一帧或某一处透明留白。");
        Assert.IsTrue(TaskbarHostSurfacePolicy.ShouldReload(TaskbarHostSurfacePolicy.RequiredStaleSamples, 0, neverReloaded));
    }

    [TestMethod]
    public void ShouldReload_HoldsReloadUntilCooldownElapsed()
    {
        var samples = TaskbarHostSurfacePolicy.RequiredStaleSamples;
        Assert.IsFalse(TaskbarHostSurfacePolicy.ShouldReload(samples, 1,
            TaskbarHostSurfacePolicy.ReloadCooldown - TimeSpan.FromMilliseconds(1)), "刚重建过就再次重建会把界面刷成闪烁。");
        Assert.IsTrue(TaskbarHostSurfacePolicy.ShouldReload(samples, 1, TaskbarHostSurfacePolicy.ReloadCooldown));
    }

    [TestMethod]
    public void GetCooldown_BacksOffAndStaysCapped()
    {
        Assert.AreEqual(TaskbarHostSurfacePolicy.ReloadCooldown, TaskbarHostSurfacePolicy.GetCooldown(1));
        Assert.AreEqual(TaskbarHostSurfacePolicy.ReloadCooldown * 2, TaskbarHostSurfacePolicy.GetCooldown(2));
        Assert.AreEqual(TaskbarHostSurfacePolicy.ReloadCooldown * 4, TaskbarHostSurfacePolicy.GetCooldown(3));
        Assert.AreEqual(TaskbarHostSurfacePolicy.MaximumReloadCooldown, TaskbarHostSurfacePolicy.GetCooldown(8),
            "反复失绘只降低自愈频率，不放弃自愈——否则用户只能手动重载。");
    }

    [TestMethod]
    public void Session_KeepsBackoffAcrossHostRecreation()
    {
        var session = new TaskbarHostSurfaceSession();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var samples = TaskbarHostSurfacePolicy.RequiredStaleSamples;

        Assert.IsTrue(session.ShouldReload(samples, now), "会话起点没有任何重建，连续样本达标后应立即重建。");

        session.RecordReload(now);
        Assert.AreEqual(1, session.Reloads);
        Assert.IsFalse(session.ShouldReload(samples, now + TimeSpan.FromSeconds(1)),
            "重建后进入冷却，新的宿主实例也必须受同一条冷却约束。");
        Assert.IsTrue(session.ShouldReload(samples, now + TaskbarHostSurfacePolicy.ReloadCooldown));

        now += TaskbarHostSurfacePolicy.ReloadCooldown;
        session.RecordReload(now);
        Assert.AreEqual(2, session.ConsecutiveReloads);
        Assert.IsFalse(session.ShouldReload(samples, now + TaskbarHostSurfacePolicy.ReloadCooldown),
            "第二次连续重建后冷却翻倍，避免反复重建把界面刷成闪烁。");

        session.Reset();
        Assert.AreEqual(0, session.Reloads);
        Assert.IsTrue(session.ShouldReload(samples, now), "重置后回到会话起点，供开发诊断与测试重复使用。");
    }

    [TestMethod]
    public void Session_HealthySamplesClearTheBackoff()
    {
        var session = new TaskbarHostSurfaceSession();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        session.RecordReload(now);
        session.RecordReload(now);
        Assert.AreEqual(2, session.ConsecutiveReloads);

        for (var sample = 1; sample < TaskbarHostSurfacePolicy.HealthySamplesToResetBackoff; sample++)
        {
            session.RecordHealthy();
            Assert.AreEqual(2, session.ConsecutiveReloads, "零星正常样本还不足以证明失绘已经结束。");
        }

        session.RecordHealthy();
        Assert.AreEqual(0, session.ConsecutiveReloads, "连续正常后清零退避，下一次失绘又能立即自愈。");
        Assert.IsTrue(session.ShouldReload(TaskbarHostSurfacePolicy.RequiredStaleSamples, now.AddSeconds(1)),
            "确认恢复后不能继续沿用上次重建的冷却时间。");
    }

    [TestMethod]
    public void Session_InterruptedHealthySamplesDoNotClearBackoff()
    {
        var session = new TaskbarHostSurfaceSession();
        session.RecordReload(DateTime.UtcNow);
        for (var sample = 0; sample < TaskbarHostSurfacePolicy.HealthySamplesToResetBackoff * 2; sample++)
        {
            session.RecordHealthy();
            session.RecordUnhealthy();
        }
        Assert.AreEqual(1, session.ConsecutiveReloads);
    }

    [TestMethod]
    public void Sessions_PreserveTargetBudgetAndIsolateOtherMonitors()
    {
        var sessions = new TaskbarHostSurfaceSessions();
        var first = sessions.Get("DISPLAY1");
        first.RecordReload(DateTime.UtcNow);
        Assert.AreSame(first, sessions.Get("display1"), "重建同一目标不能丢失退避。");
        var second = sessions.Get("DISPLAY2");
        for (var sample = 0; sample < TaskbarHostSurfacePolicy.HealthySamplesToResetBackoff; sample++)
            second.RecordHealthy();
        Assert.AreEqual(1, first.ConsecutiveReloads, "另一个屏幕正常不能清除故障屏幕的退避。");
        Assert.IsTrue(second.ShouldReload(TaskbarHostSurfacePolicy.RequiredStaleSamples, DateTime.UtcNow),
            "故障屏幕的冷却也不能阻挡另一个屏幕恢复。");
    }
}
