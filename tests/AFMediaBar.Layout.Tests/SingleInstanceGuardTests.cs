using AFMediaBar.Classes.Services.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证进程门禁在重复启动和退出后能正确转移。/ Verifies the process gate across duplicate starts and exit.</summary>
[TestClass]
public sealed class SingleInstanceGuardTests
{
    /// <summary>第二个实例不能取得门禁；首个实例退出后可以重新启动。/ A duplicate is rejected, and a later start succeeds after exit.</summary>
    [TestMethod]
    public void DuplicateIsRejectedUntilFirstInstanceExits()
    {
        var name = @"Local\AFMediaBar.SingleInstance.Test." + Guid.NewGuid().ToString("N");
        Assert.IsTrue(SingleInstanceGuard.TryAcquire(name, out var first));

        using (first)
        {
            Assert.IsFalse(SingleInstanceGuard.TryAcquire(name, out var duplicate));
            Assert.IsNull(duplicate);
        }

        Assert.IsTrue(SingleInstanceGuard.TryAcquire(name, out var restarted));
        restarted?.Dispose();
    }
}
