// 用可控 Shell 调用验证真实托盘服务的通知标识、点击与资源清理；不安装图标或向桌面发送通知。
using System.Reflection;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>背景提示与更新通知的点击意图和生命周期回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ShellNotificationTests
{
    [TestMethod]
    public void ClickingOldBackgroundNoticeAfterAnUpdateStillTargetsBackground()
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            var calls = new List<(uint Command, NativeMethods.NOTIFYICONDATA Data)>();
            bool Notify(uint command, ref NativeMethods.NOTIFYICONDATA data) { calls.Add((command, data)); return true; }
            using var tray = new ShellTrayIconService(icons, Notify);
            ISystemNotificationService notifications = tray;
            var targets = new List<ShellNotificationTarget>();
            notifications.NotificationClicked += targets.Add;
            Assert.IsTrue(notifications.TryShowNotification("background", "body", ShellNotificationTarget.TaskbarBackground));
            var background = calls.Last().Data;
            Assert.IsTrue(notifications.TryShowNotification("lyrics", "body", ShellNotificationTarget.LyricsRecovery));
            var lyrics = calls.Last().Data;
            Assert.IsTrue(notifications.TryShowNotification("preview", "body", ShellNotificationTarget.DeveloperLyricsPreview));
            var preview = calls.Last().Data;
            Assert.IsTrue(notifications.TryShowNotification("update", "body", ShellNotificationTarget.Application));
            var update = calls.Last().Data;
            Assert.AreNotEqual(background.uID, update.uID);
            Assert.AreNotEqual(lyrics.uID, update.uID);
            Assert.AreNotEqual(lyrics.uID, background.uID);
            Assert.AreNotEqual(preview.uID, lyrics.uID);
            Assert.AreNotEqual(preview.uID, background.uID);
            Assert.AreNotEqual(preview.uID, update.uID);
            var registration = calls.Single(call => call.Command == NativeMethods.NIM_ADD && call.Data.uID == background.uID).Data;
            Assert.AreEqual(NativeMethods.NIS_HIDDEN, registration.dwState & registration.dwStateMask);
            Click(tray, background);
            Click(tray, lyrics);
            Click(tray, preview);
            Click(tray, update);
            Assert.IsTrue(tray.TryShowNotification("generic", "body"));
            Click(tray, calls.Last().Data);
            CollectionAssert.AreEqual(new[] { ShellNotificationTarget.TaskbarBackground, ShellNotificationTarget.LyricsRecovery,
                ShellNotificationTarget.DeveloperLyricsPreview, ShellNotificationTarget.Application }, targets);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void UnsupportedCallbackVersionDoesNotPublishAnAmbiguousBackgroundNotice()
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            var deleted = new List<uint>();
            bool Notify(uint command, ref NativeMethods.NOTIFYICONDATA data)
            {
                if (command == NativeMethods.NIM_DELETE) deleted.Add(data.uID);
                return command != NativeMethods.NIM_SETVERSION;
            }
            using var tray = new ShellTrayIconService(icons, Notify);
            Assert.IsFalse(tray.TryShowNotification("background", "body", ShellNotificationTarget.TaskbarBackground));
            Assert.AreEqual(1, deleted.Count);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DisposeRemovesHiddenRegistrationsOnceAndRejectsLateClicks()
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            var deleted = new List<uint>();
            NativeMethods.NOTIFYICONDATA notice = default;
            bool Notify(uint command, ref NativeMethods.NOTIFYICONDATA data)
            {
                if (command == NativeMethods.NIM_DELETE) deleted.Add(data.uID);
                if ((data.uFlags & NativeMethods.NIF_INFO) != 0) notice = data;
                return true;
            }
            using var tray = new ShellTrayIconService(icons, Notify);
            var clicked = 0;
            tray.NotificationClicked += _ => clicked++;
            tray.TryShowNotification("background", "body", ShellNotificationTarget.TaskbarBackground);
            tray.Dispose();
            tray.Dispose();
            Assert.AreEqual(2, deleted.Count);
            Assert.AreEqual(2, deleted.Distinct().Count());
            Click(tray, notice);
            Assert.AreEqual(0, clicked);
            Assert.IsFalse(tray.TryShowNotification("background", "body", ShellNotificationTarget.TaskbarBackground));
            return Task.CompletedTask;
        });
    }

    [DataTestMethod]
    [DataRow(ShellNotificationTarget.TaskbarBackground)]
    [DataRow(ShellNotificationTarget.DeveloperLyricsPreview)]
    public void ShellRecreationRestoresTheSameHiddenNotificationIdentity(ShellNotificationTarget target)
    {
        StaTest.Run(_ =>
        {
            using var icons = new AppIconService();
            var registrations = new List<uint>();
            NativeMethods.NOTIFYICONDATA notice = default;
            bool Notify(uint command, ref NativeMethods.NOTIFYICONDATA data)
            {
                if (command == NativeMethods.NIM_ADD) registrations.Add(data.uID);
                if ((data.uFlags & NativeMethods.NIF_INFO) != 0) notice = data;
                return true;
            }
            using var tray = new ShellTrayIconService(icons, Notify);
            var targets = new List<ShellNotificationTarget>();
            tray.NotificationClicked += targets.Add;
            tray.TryShowNotification("notice", "body", target);
            var restart = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");
            typeof(ShellTrayIconService).GetMethod("WindowHook", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tray, [notice.hWnd, restart, IntPtr.Zero, IntPtr.Zero, false]);
            Assert.AreEqual(2, registrations.Count(id => id == notice.uID));
            Click(tray, notice);
            CollectionAssert.AreEqual(new[] { target }, targets);
            return Task.CompletedTask;
        });
    }

    private static void Click(ShellTrayIconService tray, NativeMethods.NOTIFYICONDATA data)
    {
        // 在原生消息边界模拟 VERSION_4 回调；不读取内部路由状态。
        var callback = typeof(ShellTrayIconService).GetMethod("WindowHook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        callback.Invoke(tray, [data.hWnd, (int)data.uCallbackMessage, IntPtr.Zero, new IntPtr(((long)data.uID << 16) | NativeMethods.NIN_BALLOONUSERCLICK), false]);
    }
}
