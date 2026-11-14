// 用测试自己创建并释放的隐藏窗口确认非任务栏句柄不会被当作任务栏几何；不操作 Explorer。
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏句柄身份校验：只有类名仍是主/副任务栏的窗口才提供几何。</summary>
[TestClass]
[TestCategory("Native")]
public sealed class TaskbarHandleIdentityTests
{
    [TestMethod]
    public async Task ForeignWindowHandleIsNotTreatedAsTaskbar()
    {
        if (await NativeTestProcess.RunIfNeededAsync(typeof(TaskbarHandleIdentityTests).FullName + "." +
            nameof(ForeignWindowHandleIsNotTreatedAsTaskbar))) return;
        StaTest.Run(dispatcher =>
        {
            using var foreign = new HwndSource(new HwndSourceParameters("Taskbar identity foreign window")
            {
                WindowStyle = WS_POPUP,
                PositionX = -30000,
                PositionY = -30000,
                Width = 400,
                Height = 40
            });
            var service = new TaskbarDockService(new DisplayMonitorService());
            Assert.IsTrue(GetWindowRect(foreign.Handle, out _), "测试窗口必须真实存在。");
            // 句柄被回收给别的窗口时不能继续提供几何，否则定位与占用区探测会按错误的矩形工作。
            Assert.IsFalse(service.TryGetTaskbarRect(foreign.Handle, out _));
            Assert.IsFalse(service.TryGetTaskbarRect(IntPtr.Zero, out _));
            return Task.CompletedTask;
        });
    }
}
