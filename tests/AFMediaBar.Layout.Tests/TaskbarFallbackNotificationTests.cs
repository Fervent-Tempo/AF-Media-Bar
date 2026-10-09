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
