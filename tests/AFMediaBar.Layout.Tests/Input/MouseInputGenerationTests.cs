// 覆盖电源/会话恢复后迟到输入不能重新发布旧意图；不注册 Raw Input，不发送系统输入。
using System.Reflection;
using System.Windows.Input;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>取消代际与输入事件时间的边界。</summary>
[TestClass]
public sealed class MouseInputGenerationTests
{
    [TestMethod]
    public void RecoveryRejectsOldClickButKeepsNewChordAndFollowingClick()
    {
        StaTest.Run(async dispatcher =>
        {
            using var input = new NativeMouseInputMonitor(null!, dispatcher);
            typeof(NativeMouseInputMonitor).GetMethod("OnPowerChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, [null, EventArgs.Empty]);
            var boundary = (uint)typeof(NativeMouseInputMonitor).GetField("_minimumInputTime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(input)!;
            Assert.IsTrue(await input.ConsumeSuppressedClickAsync(MouseButton.Left, unchecked((int)(boundary - 1))));
            Packet(1, boundary + 1); Packet(0x400, boundary + 2); Packet(2, boundary + 3);
            Assert.IsTrue(await input.ConsumeSuppressedClickAsync(MouseButton.Left, unchecked((int)(boundary + 3))));
            Packet(1, boundary + 4); Packet(2, boundary + 5);
            Assert.IsFalse(await input.ConsumeSuppressedClickAsync(MouseButton.Left, unchecked((int)(boundary + 5))));

            void Packet(ushort buttons, uint time) => typeof(NativeMouseInputMonitor)
                .GetMethod("OnRawMouse", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(input, [new RawMousePacket(new IntPtr(1), buttons, 120, time, 0, 0)]);
        });
    }
}
