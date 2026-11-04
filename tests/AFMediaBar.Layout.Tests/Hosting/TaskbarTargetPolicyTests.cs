// 验证任务栏句柄复用和断屏回退时的配置归属，避免交互被误判为只读或覆盖离线屏配置。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>实际任务栏与逻辑显示器身份的回归。</summary>
[TestClass]
public sealed class TaskbarTargetPolicyTests
{
    [TestMethod]
    public void ConnectedSelectedTaskbarWinsOverStaleCatalogTarget()
    {
        Assert.AreEqual("DISPLAY6", TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY1", "DISPLAY6", ["DISPLAY1", "DISPLAY6"]));
        Assert.AreEqual("DISPLAY6", TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY1", "DISPLAY6", []));
    }

    [TestMethod]
    public void UnselectedFallbackKeepsOriginalLogicalOwner()
    {
        Assert.AreEqual("DISPLAY1", TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY6", "DISPLAY6", ["DISPLAY1"]));
        Assert.AreEqual("DISPLAY2", TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY2", "DISPLAY6", ["DISPLAY1", "DISPLAY2"]));
    }

    [TestMethod]
    public void LogicalIdentityChangesWhenSameTaskbarBecomesExplicitlySelected()
    {
        var fallback = TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY1", "DISPLAY6", ["DISPLAY1"]);
        var selected = TaskbarTargetPolicy.ResolveHostDeviceId("DISPLAY1", "DISPLAY6", ["DISPLAY1", "display6"]);
        Assert.AreNotEqual(fallback, selected);
        Assert.AreEqual("DISPLAY6", selected);
    }
}
