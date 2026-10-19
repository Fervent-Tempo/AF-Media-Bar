// 使用隐藏的原生父子窗口复现任务栏采样后移动的竞态；测试拥有并释放 HWND，不操作 Explorer。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证父窗口移动不会改变媒体宿主在客户区中的锚点。</summary>
[TestClass]
public sealed class TaskbarDockPositionTests
{
    [DataTestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 24)]
    [DataRow(0, -24)]
    [DataRow(24, 0)]
    [DataRow(-24, 0)]
    public void ParentMovementBetweenSamplingAndPlacementKeepsChildAtClientOrigin(int deltaX, int deltaY)
    {
        StaTest.Run(_ =>
        {
            using var parent = new HwndSource(new HwndSourceParameters("Taskbar position parent")
            {
                WindowStyle = WS_POPUP,
                PositionX = -30000,
                PositionY = -30000,
                Width = 800,
                Height = 48
            });
            using var child = new HwndSource(new HwndSourceParameters("Taskbar position child")
            {
                WindowStyle = WS_CHILD,
                ParentWindow = parent.Handle,
                Width = 1,
                Height = 1
            });
            var service = new TaskbarDockService(new DisplayMonitorService());
            Assert.IsTrue(GetWindowRect(parent.Handle, out var sampledRect));
            Assert.IsTrue(SetWindowPos(parent.Handle, 0,
                sampledRect.Left + deltaX, sampledRect.Top + deltaY, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE));

            service.SetWindowPosition(child.Handle, parent.Handle, 800, 48);
            Assert.IsTrue(GetWindowRect(child.Handle, out var childRect));
            var childOrigin = new POINT { X = childRect.Left, Y = childRect.Top };
            Assert.IsTrue(ScreenToClient(parent.Handle, ref childOrigin));
            Assert.AreEqual(0, childOrigin.X, "父窗口移动后子窗口不能产生横向偏移。");
            Assert.AreEqual(0, childOrigin.Y, "父窗口移动后子窗口不能产生纵向偏移。");
            Assert.AreEqual(800, childRect.Right - childRect.Left);
            Assert.AreEqual(48, childRect.Bottom - childRect.Top);

            Assert.IsTrue(GetWindowRect(parent.Handle, out var placedParentRect));
            Assert.IsTrue(SetWindowPos(parent.Handle, 0,
                placedParentRect.Left + deltaX, placedParentRect.Top + deltaY, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE));
            Assert.IsTrue(GetWindowRect(child.Handle, out var followedRect));
            var followedOrigin = new POINT { X = followedRect.Left, Y = followedRect.Top };
            Assert.IsTrue(ScreenToClient(parent.Handle, ref followedOrigin));
            Assert.AreEqual(0, followedOrigin.X);
            Assert.AreEqual(0, followedOrigin.Y);
            return Task.CompletedTask;
        });
    }
}
