// 验证多宿主共用的通知冷却，不调用 Shell 或发送真实通知。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Interop;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>空间不足通知尝试的合并回归。</summary>
[TestClass]
public sealed class TaskbarFallbackNotificationTests
{
    [TestMethod]
    public void MultipleHostsShareOneCooldownAndFailedAttemptsDoNotRetry()
    {
        var gate = new TaskbarFallbackNotificationGate();
        Assert.IsTrue(gate.TryBegin(10));
        Assert.IsFalse(gate.TryBegin(10));
        Assert.IsFalse(gate.TryBegin(39.9));
        Assert.IsTrue(gate.TryBegin(40));
        Assert.IsFalse(gate.TryBegin(double.NaN));
    }
    [TestMethod]
    public void PlacementClickDoesNotCarryTheUpdatePurposeAndRejectedNoticeKeepsPreviousPurpose()
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            var accept = true;
            using var tray = new ShellTrayIconService(icons, (_, _) => accept);
            TrayNotificationPurpose? received = null;
            tray.NotificationClicked += (_, args) => received = args.Purpose;
            void Click() => typeof(ShellTrayIconService).GetMethod("WindowHook", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tray, [nint.Zero, NativeMethods.WM_APP + 17, nint.Zero, (nint)NativeMethods.NIN_BALLOONUSERCLICK, false]);
            Assert.IsTrue(tray.TryShowNotification("Update", "Available"));
            Click();
            Assert.AreEqual(TrayNotificationPurpose.Update, received);
            Assert.IsTrue(tray.TryShowNotification("Space", "Moved", TrayNotificationPurpose.TaskbarPlacement));
            Click();
            Assert.AreEqual(TrayNotificationPurpose.TaskbarPlacement, received);
            accept = false;
            Assert.IsFalse(tray.TryShowNotification("Update", "Rejected"));
            Click();
            Assert.AreEqual(TrayNotificationPurpose.TaskbarPlacement, received);
            return Task.CompletedTask;
        });
    }

}
