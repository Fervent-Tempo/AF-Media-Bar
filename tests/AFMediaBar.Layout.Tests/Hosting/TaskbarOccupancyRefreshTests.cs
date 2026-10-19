// 验证事件合并和有限补探测频率，不安装 WinEvent Hook 或访问 UIA。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏图标变化后的短时补探测回归。</summary>
[TestClass]
public sealed class TaskbarOccupancyRefreshTests
{
    [TestMethod]
    public void EventsCoalesceAndKeepAtMostTenRefreshesPerSecond()
    {
        var policy = new TaskbarOccupancyRefreshPolicy();
        policy.Changed(0);
        policy.Changed(0.03);
        Assert.IsFalse(policy.TryBegin(0.049));
        Assert.IsTrue(policy.TryBegin(0.05));
        Assert.IsFalse(policy.TryBegin(0.1));
        policy.Changed(0.12);
        Assert.IsTrue(policy.TryBegin(0.15));
        Assert.IsFalse(policy.TryBegin(0.2));
        Assert.IsTrue(policy.TryBegin(0.25));
        Assert.IsTrue(policy.IsActive(1.1));
        Assert.IsFalse(policy.TryBegin(1.13));
        policy.Changed(2);
        Assert.IsFalse(policy.TryBegin(2.01));
        Assert.IsTrue(policy.TryBegin(2.05));
        policy.Reset();
        Assert.IsFalse(policy.IsActive(2.1));
        Assert.IsFalse(policy.TryBegin(double.NaN));
    }
}
