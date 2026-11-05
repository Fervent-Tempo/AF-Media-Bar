// 验证 Raw Input 通知的发布顺序与降级行为；不注册 Raw Input，不发送系统输入。
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>Raw Input 按压通知与点击确认的边界。</summary>
[TestClass]
public sealed class MouseInputNotificationTests
{
    [TestMethod]
    public void RawButtonPressPublishesSnapshot()
    {
        StaTest.Run(async dispatcher =>
        {
            using var input = new NativeMouseInputMonitor(null!, dispatcher);
            NativeMousePressEventArgs? received = null;
            input.ButtonPressed += (_, args) => received = args;

            input.PublishButtonPress(10, 20);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.IsNotNull(received);
            Assert.AreEqual(10, received.ScreenX);
            Assert.AreEqual(20, received.ScreenY);
            Assert.IsTrue(received.CapturedAt > 0);
            Assert.IsNotNull(received.WindowChain);
        });
    }

    [TestMethod]
    public void PressAndReleaseArePublishedInOrder()
    {
        StaTest.Run(dispatcher =>
        {
            using var input = new NativeMouseInputMonitor(null!, dispatcher);
            var events = new List<string>();
            input.LeftButtonPressed += (_, args) => events.Add($"P{args.ScreenX}");
            input.LeftButtonReleased += (_, args) => events.Add($"R{args.ScreenX}");

            // 时间戳取处理时刻之前的确定值；Windows 时钟粒度约 15.6 ms，不能依赖 Sleep 推进 TickCount。
            var now = unchecked((uint)Environment.TickCount);
            typeof(NativeMouseInputMonitor).GetField("_minimumInputTime", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(input, unchecked(now - 60000));
            Packet(1, now - 3, 10);
            Packet(2, now - 2, 10);
            Packet(1, now - 1, 20);
            Packet(2, now, 20);
            // 直接驱动排空，不依赖 Dispatcher 调度时机，避免全量测试负载下点击被新鲜度门限丢弃。
            typeof(NativeMouseInputMonitor).GetMethod("DrainNotifications", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, null);

            Assert.AreEqual("P10,R10,P20,R20", string.Join(",", events));

            void Packet(ushort buttons, uint time, int x) => typeof(NativeMouseInputMonitor)
                .GetMethod("OnRawMouse", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, [new RawMousePacket(new IntPtr(1), buttons, 0, time, x, 0)]);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void UnregisteredReceiverDoesNotSwallowClicks()
    {
        StaTest.Run(async dispatcher =>
        {
            using var input = new NativeMouseInputMonitor(null!, dispatcher);
            Assert.IsFalse(await input.ConsumeSuppressedClickAsync(MouseButton.Left, unchecked((int)Environment.TickCount)));
        });
    }
}
