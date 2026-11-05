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
        StaTest.Run(async dispatcher =>
        {
            using var input = new NativeMouseInputMonitor(null!, dispatcher);
            var events = new List<string>();
            input.LeftButtonPressed += (_, args) => events.Add($"P{args.ScreenX}");
            input.LeftButtonReleased += (_, args) => events.Add($"R{args.ScreenX}");

            typeof(NativeMouseInputMonitor).GetMethod("OnPowerChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, [null, EventArgs.Empty]);
            var boundary = (uint)typeof(NativeMouseInputMonitor)
                .GetField("_minimumInputTime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(input)!;
            Thread.Sleep(6);
            Packet(1, boundary + 1, 10);
            Packet(2, boundary + 2, 10);
            Packet(1, boundary + 3, 20);
            Packet(2, boundary + 4, 20);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.AreEqual("P10,R10,P20,R20", string.Join(",", events));

            void Packet(ushort buttons, uint time, int x) => typeof(NativeMouseInputMonitor)
                .GetMethod("OnRawMouse", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, [new RawMousePacket(new IntPtr(1), buttons, 0, time, x, 0)]);
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
