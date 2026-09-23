using System;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 按需回收的判定：哪个原因用多大强度，以及多久之内只做一次。
/// The on-demand reclaim decision: which strength a reason deserves, and how often a reclaim may run.
/// </summary>
[TestClass]
public sealed class MemoryTrimPolicyTests
{
    /// <summary>
    /// 强度映射：只有"短时间不会再回来"的两条（设置窗口关闭、用户手动点击）才交还工作集，其余只收垃圾。
    /// Strength mapping: only the two reasons where the process will not be needed again right away — the settings window closing and a manual click —
    /// return the working set, while everything else only collects garbage.
    /// </summary>
    [TestMethod]
    public void OnlyTheSettingsWindowAndManualRequestsTrimTheWorkingSet()
    {
        Assert.AreEqual(MemoryTrimStrength.Deep, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.SettingsWindowClosed));
        Assert.AreEqual(MemoryTrimStrength.Deep, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.ManualRequest));

        // 空闲档之后用户可能马上回来；浮层则随时会被再打开：这两条剥离工作集只会换来一次硬缺页。
        // The user may come straight back after the idle level, and a panel is reopened at any moment, so returning the working set would only buy a hard
        // page fault.
        Assert.AreEqual(MemoryTrimStrength.Gentle, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.IdleLevelEntered));
        Assert.AreEqual(MemoryTrimStrength.Gentle, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.PanelClosed));
        Assert.AreEqual(MemoryTrimStrength.Gentle, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.TaskbarHidden));
    }

    /// <summary>手动请求只有一处判定来源。/ A manual request has exactly one source of truth.</summary>
    [TestMethod]
    public void OnlyTheManualTriggerCountsAsManual()
    {
        Assert.IsTrue(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.ManualRequest));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.SettingsWindowClosed));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.PanelClosed));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.IdleLevelEntered));
    }

    /// <summary>从未回收过时立刻执行。/ The first reclaim always runs.</summary>
    [TestMethod]
    public void TheFirstReclaimAlwaysRuns()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.PanelClosed, default, now));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.IdleLevelEntered, default, now));
    }

    /// <summary>
    /// 非手动请求受最小间隔限制：刚好到间隔算可以执行，差一毫秒不算。
    /// A non-manual request is held back by the minimum interval: exactly at the interval it may run, one millisecond short it may not.
    /// </summary>
    [TestMethod]
    public void NonManualReclaimsRespectTheMinimumInterval()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsFalse(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.PanelClosed, now, now));
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.PanelClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval - TimeSpan.FromMilliseconds(1)));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.PanelClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.SettingsWindowClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval + TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// 手动请求绕过节流：用户点了按钮就必须有反应，被 5 秒节流吞掉比多跑一次 GC 更糟。
    /// A manual request bypasses the throttle: clicking the button has to do something, and being swallowed by the five-second throttle is worse than one
    /// extra collection.
    /// </summary>
    [TestMethod]
    public void ManualRequestsBypassTheThrottle()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.ManualRequest, now, now));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.ManualRequest,
            now,
            now + TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>节流窗口是一个正整数秒的取值：0 会让每次关窗都回收，过大则等于没有按需回收。/ The throttle window is a positive number of seconds: zero would reclaim on every close and a huge value would disable on-demand reclaims.</summary>
    [TestMethod]
    public void MinimumIntervalStaysInASaneRange()
    {
        Assert.IsTrue(MemoryTrimPolicy.MinimumInterval >= TimeSpan.FromSeconds(1));
        Assert.IsTrue(MemoryTrimPolicy.MinimumInterval <= TimeSpan.FromSeconds(30));
    }

    /// <summary>启动后的回收与手动请求一样按最深强度执行：它存在的意义就是交还启动时读进来的冷页面。/ The post-startup reclaim goes as deep as a manual one: its whole point is handing back the cold pages read during startup.</summary>
    [TestMethod]
    public void ThePostStartupReclaimIsDeep()
    {
        Assert.AreEqual(MemoryTrimStrength.Deep, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.StartupSettled));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.StartupSettled));
    }

    /// <summary>
    /// 启动后回收的三条判据：启动链必须先安定下来，用户必须松手，档位必须仍是常规。
    /// The three conditions of the post-startup reclaim: the startup chain has to have settled, the user has to have let go, and the level has to still be normal.
    /// </summary>
    [TestMethod]
    public void PostStartupReclaimWaitsForSettlingAndAQuietMoment()
    {
        var idleEnough = MemoryTrimPolicy.StartupUserIdleThreshold;
        var busy = TimeSpan.FromMilliseconds(200);
        var settled = MemoryTrimPolicy.StartupMinimumDelay + TimeSpan.FromSeconds(1);

        // 启动链还在跑：即使档位常规、用户空闲也不动手。
        // The startup chain is still running, so nothing happens even with a normal level and an idle user.
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrimAfterStartup(
            MemoryTrimPolicy.StartupMinimumDelay - TimeSpan.FromSeconds(1),
            idleEnough,
            isLevelNormal: true));

        // 已经进入空闲或更深的档位：档位路径刚做过更彻底的事。
        // The level is already idle or deeper, where the level path has just done something more thorough.
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrimAfterStartup(settled, idleEnough, isLevelNormal: false));

        // 用户此刻正在操作：再等等，缺页发生在用户下一次碰到页面的时候。
        // The user is interacting right now: wait, because page faults happen the next time the user touches a page.
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrimAfterStartup(settled, busy, isLevelNormal: true));

        // 三条都满足：执行。
        // All three hold, so it runs.
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrimAfterStartup(
            MemoryTrimPolicy.StartupMinimumDelay,
            idleEnough,
            isLevelNormal: true));
    }

    /// <summary>
    /// 到了最迟时间点即使条件不满足也执行：用户可能一直在动鼠标，而启动时读进来的页面早就冷掉了。
    /// Past the deadline it runs even when the conditions do not hold: the user may keep moving the mouse while the pages read during startup have long gone cold.
    /// </summary>
    [TestMethod]
    public void PostStartupReclaimRunsAtTheDeadlineAnyway()
    {
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrimAfterStartup(
            MemoryTrimPolicy.StartupDeadline,
            TimeSpan.Zero,
            isLevelNormal: true));

        // 但"档位已经不常规"这一条不会被最迟时间点覆盖：那时档位路径已经做过更彻底的回收。
        // The "level is no longer normal" condition is not overridden by the deadline, because the level path has already reclaimed more thoroughly then.
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrimAfterStartup(
            MemoryTrimPolicy.StartupDeadline,
            TimeSpan.Zero,
            isLevelNormal: false));
    }

    /// <summary>启动后的时间窗必须是有界的：太早会与启动链抢页面，太晚会让人以为没生效。/ The startup window has to be bounded: too early competes with the startup chain for pages, too late looks like nothing happened.</summary>
    [TestMethod]
    public void StartupWindowStaysInASaneRange()
    {
        Assert.IsTrue(MemoryTrimPolicy.StartupMinimumDelay >= TimeSpan.FromSeconds(5));
        Assert.IsTrue(MemoryTrimPolicy.StartupMinimumDelay < MemoryTrimPolicy.StartupDeadline);
        Assert.IsTrue(MemoryTrimPolicy.StartupDeadline <= TimeSpan.FromMinutes(5));
        Assert.IsTrue(MemoryTrimPolicy.StartupUserIdleThreshold >= TimeSpan.FromSeconds(1));
        Assert.IsTrue(MemoryTrimPolicy.StartupUserIdleThreshold < MemoryTrimPolicy.StartupMinimumDelay);
    }
}
