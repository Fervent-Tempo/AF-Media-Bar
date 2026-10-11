// 验证多宿主通知分级与恢复门槛，不发送真实通知。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>空间不足通知尝试的合并回归。</summary>
[TestClass]
public sealed class TaskbarFallbackNotificationTests
{
    [TestMethod]
    public void CongestionNotifiesEachSeverityOnceAndEscalatesWithoutCooldown()
    {
        var gate = new TaskbarFallbackNotificationGate();
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0, 1, 1));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 10, 1, 1));
        Assert.AreEqual(TaskbarSpacePressure.Constrained, gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0.2, 1, 2));
        Assert.IsNull(gate.Observe("B", TaskbarSpacePressure.Constrained, false, 0.2, 1, 1));
        Assert.IsNull(gate.Observe("B", TaskbarSpacePressure.Constrained, false, 0.4, 1, 2));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0.4, 1, 3));
        Assert.AreEqual(TaskbarSpacePressure.Hidden, gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0.6, 1, 4));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 5, 1, 5));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 6, 1, 6));
    }

    [TestMethod]
    public void DirectHiddenSuppressesDowngradeAndOnlyAmpleStableRecoveryRearms()
    {
        var gate = new TaskbarFallbackNotificationGate();
        gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0, 1, 1);
        Assert.AreEqual(TaskbarSpacePressure.Hidden, gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0.2, 1, 2));
        gate.Observe("A", TaskbarSpacePressure.Normal, false, 1, 1, 3);
        gate.Observe("A", TaskbarSpacePressure.Normal, false, 4, 1, 4);
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 5, 1, 5);
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 5.2, 1, 6));
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 6, 1, 7);
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 8, 1, 7); // 缓存读取不能确认恢复。
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 9, 1, 8);
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 9.2, 1, 9));
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 10, 1, 10);
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 12, 1, 11);
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 13, 1, 12);
        Assert.AreEqual(TaskbarSpacePressure.Constrained, gate.Observe("A", TaskbarSpacePressure.Constrained, false, 13.2, 1, 13));
    }

    [TestMethod]
    public void EveryLiveHostMustRecoverAndHostRebuildDoesNotRearm()
    {
        var gate = new TaskbarFallbackNotificationGate();
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0, 1, 1);
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0.2, 1, 2);
        gate.Observe("B", TaskbarSpacePressure.Hidden, false, 0.2, 1, 1);
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 1, 1, 3);
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 4, 1, 4);
        gate.ForgetHost("B");
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 5, 1, 5);
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 5.2, 1, 6));
        gate.ForgetHost("A");
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 6, 2, 1);
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 6.2, 2, 2));
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 7, 2, 3);
        gate.Observe("A", TaskbarSpacePressure.Normal, true, 9, 2, 4);
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 10, 2, 5);
        Assert.AreEqual(TaskbarSpacePressure.Constrained, gate.Observe("A", TaskbarSpacePressure.Constrained, false, 10.2, 2, 6));
    }
    [TestMethod]
    public void FailedProbeAndStalePublicationCannotConfirmCongestion()
    {
        var gate = new TaskbarFallbackNotificationGate();
        gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0, 1, 1);
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0.3, 1, 3, 2));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0.4, 1, 1));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Hidden, false, 0.4, 0, 99));
        Assert.IsNull(gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0.4, 1, 4, 2));
        Assert.AreEqual(TaskbarSpacePressure.Constrained, gate.Observe("A", TaskbarSpacePressure.Constrained, false, 0.5, 1, 5, 2));
    }

    [TestMethod]
    public void PlacementClickDoesNotOpenUpdatesOrChangeAnEarlierUpdateTarget()
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            NativeMethods.NOTIFYICONDATA notice = default;
            bool Notify(uint command, ref NativeMethods.NOTIFYICONDATA data)
            {
                if ((data.uFlags & NativeMethods.NIF_INFO) != 0) notice = data;
                return true;
            }
            using var tray = new ShellTrayIconService(icons, Notify);
            var targets = new List<ShellNotificationTarget>();
            tray.NotificationClicked += targets.Add;
            void Click(NativeMethods.NOTIFYICONDATA data) =>
                typeof(ShellTrayIconService).GetMethod("WindowHook", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(tray, [data.hWnd, (int)data.uCallbackMessage, nint.Zero,
                        new IntPtr(((long)data.uID << 16) | NativeMethods.NIN_BALLOONUSERCLICK), false]);
            Assert.IsTrue(tray.TryShowNotification("Update", "Available", ShellNotificationTarget.Application));
            var update = notice;
            Assert.IsTrue(tray.TryShowNotification("Space", "Moved", ShellNotificationTarget.None));
            Click(notice);
            Assert.AreEqual(0, targets.Count);
            Click(update);
            CollectionAssert.AreEqual(new[] { ShellNotificationTarget.Application }, targets);
            return Task.CompletedTask;
        });
    }

}
