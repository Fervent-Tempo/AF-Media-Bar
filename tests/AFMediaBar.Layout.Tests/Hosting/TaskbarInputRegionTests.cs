// 用隐藏的原生父子窗口验证输入区域的下发约定；测试拥有并释放 HWND，不触碰 Explorer。
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证 SetWindowRgn 的调用约定：不裁剪空几何，区域确实按给定矩形落地。</summary>
[TestClass]
public sealed class TaskbarInputRegionTests
{
    private const int SimpleRegion = 2;
    private const int NullRegion = 1;

    [TestMethod]
    public void EmptyRectanglesKeepThePreviousRegion()
    {
        StaTest.Run(_ =>
        {
            using var parent = new HwndSource(Parameters("Taskbar region parent", WS_POPUP));
            using var child = new HwndSource(Parameters("Taskbar region child", WS_CHILD, parent.Handle));
            var service = new TaskbarDockService(new DisplayMonitorService());
            var bar = new RECT { Left = 20, Top = 2, Right = 300, Bottom = 46 };

            service.ApplyInputRegion(child.Handle, [bar], redraw: true);
            Assert.AreEqual(SimpleRegion, GetWindowRgnBox(child.Handle, out var applied));
            Assert.AreEqual((bar.Left, bar.Top, bar.Right, bar.Bottom),
                (applied.Left, applied.Top, applied.Right, applied.Bottom));

            // 安全几何未就绪时宿主会带着空矩形进来：空区域会把窗口裁到什么都不画，而恢复成真实区域的那一步并不会
            // 强迫分层宿主重新提交像素，媒体栏于是永久不可见。这里要求适配层直接拒绝，而不是照做。
            service.ApplyInputRegion(child.Handle, [], redraw: true);
            Assert.AreNotEqual(NullRegion, GetWindowRgnBox(child.Handle, out var afterEmpty),
                "空矩形不得把宿主裁成空区域。");
            Assert.AreEqual(SimpleRegion, GetWindowRgnBox(child.Handle, out afterEmpty));
            Assert.AreEqual((bar.Left, bar.Top, bar.Right, bar.Bottom),
                (afterEmpty.Left, afterEmpty.Top, afterEmpty.Right, afterEmpty.Bottom),
                "被拒绝的空矩形必须保持上一次的区域。");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void EmptyRectanglesLeaveAnUnclippedHostWithoutARegion()
    {
        StaTest.Run(dispatcher =>
        {
            using var parent = new HwndSource(Parameters("Taskbar region parent", WS_POPUP));
            using var child = new HwndSource(Parameters("Taskbar region child", WS_CHILD, parent.Handle));
            var service = new TaskbarDockService(new DisplayMonitorService());

            service.ApplyInputRegion(child.Handle, [default], redraw: true);
            Assert.AreEqual(0, GetWindowRgnBox(child.Handle, out var untouched),
                "首次落地就没有安全几何时不能给宿主留下任何区域，整窗保持可命中并由显隐表达不可用。");
            Assert.AreEqual(0, untouched.Right - untouched.Left);
            return Task.CompletedTask;
        });
    }

    private static HwndSourceParameters Parameters(string name, int style, nint parent = default) => new(name)
    {
        WindowStyle = style,
        ParentWindow = parent,
        PositionX = -30000,
        PositionY = -30000,
        Width = 800,
        Height = 48
    };
}
