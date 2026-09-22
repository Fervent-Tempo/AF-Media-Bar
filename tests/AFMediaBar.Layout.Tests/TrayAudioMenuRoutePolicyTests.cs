using System;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 托盘左键音频菜单宿主的纯逻辑测试：请求必须落到一个真实的菜单宿主上，绝不允许被静默丢弃。
/// Pure-logic tests for the tray left-click audio-menu host: every request has to land on a real menu host and must
/// never be dropped silently.
/// </summary>
[TestClass]
public sealed class TrayAudioMenuRoutePolicyTests
{
    /// <summary>
    /// 任务栏宿主存在时走任务栏宿主：这是原本就能工作的那条路径，必须保持优先级不变。
    /// With a taskbar host present the request goes to it: that path already worked and its priority has to stay put.
    /// </summary>
    [TestMethod]
    public void Resolve_任务栏宿主存在时路由到任务栏宿主()
    {
        Assert.AreEqual(
            TrayAudioMenuRoute.TaskbarHost,
            TrayAudioMenuRoutePolicy.Resolve(hasTaskbarHost: true, hasIslandHost: false));
        Assert.AreEqual(
            TrayAudioMenuRoute.TaskbarHost,
            TrayAudioMenuRoutePolicy.Resolve(hasTaskbarHost: true, hasIslandHost: true));
    }

    /// <summary>
    /// 灵动岛模式下没有任务栏宿主：请求必须路由到岛，这正是"托盘左键的两项菜单在岛模式下静默失效"的修复点
    /// （原实现遇到空的任务栏宿主列表就直接 return）。
    /// In dynamic-island mode there is no taskbar host: the request has to route to the island, which is exactly the fix
    /// for "the two tray left-click menus fail silently in island mode" (the old implementation returned as soon as the
    /// taskbar host list was empty).
    /// </summary>
    [TestMethod]
    public void Resolve_岛模式下路由到灵动岛宿主()
    {
        Assert.AreEqual(
            TrayAudioMenuRoute.IslandHost,
            TrayAudioMenuRoutePolicy.Resolve(hasTaskbarHost: false, hasIslandHost: true));
    }

    /// <summary>
    /// 两个宿主都不存在（例如刚启动、模式还没激活）时回退到托盘音频浮窗：这不是静默丢弃，而是换一个有内容、
    /// 永远可用的入口。
    /// With neither host available — for example right after startup, before a mode is activated — the request falls back
    /// to the tray audio flyout, which is a real menu rather than a silent drop.
    /// </summary>
    [TestMethod]
    public void Resolve_两个宿主都不存在时回退到托盘音频浮窗()
    {
        Assert.AreEqual(
            TrayAudioMenuRoute.TrayAudioFlyout,
            TrayAudioMenuRoutePolicy.Resolve(hasTaskbarHost: false, hasIslandHost: false));
    }

    /// <summary>
    /// 穷举四种宿主组合，每一种都必须落到一个真实宿主：任何"无路由"的结果都意味着托盘左键又会出现点了没反应的静默失效。
    /// Every one of the four host combinations has to land on a real host: an "unrouted" outcome would bring back the
    /// silent "clicked and nothing happened" failure.
    /// </summary>
    [TestMethod]
    public void Resolve_任何宿主组合都有真实落点()
    {
        foreach (var hasTaskbarHost in new[] { false, true })
        {
            foreach (var hasIslandHost in new[] { false, true })
            {
                var route = TrayAudioMenuRoutePolicy.Resolve(hasTaskbarHost, hasIslandHost);
                Assert.IsTrue(
                    Enum.IsDefined(route),
                    $"宿主组合 taskbar={hasTaskbarHost} island={hasIslandHost} 落到了未定义路由 {route}。");
            }
        }
    }
}
