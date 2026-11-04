// 复用现有隔离 WPF 进程检查提示窗；不激活窗口、不注册输入 Hook，也不模拟真实拖动验收。
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Services;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

internal static class DragSnapGuideChecks
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    internal static void Verify()
    {
        DragSnapGuideWindow? guide = null;
        using var session = new DragSnapSession(() => guide = new DragSnapGuideWindow());
        var area = new Rect(0, 0, 800, 600);
        var context = new DragSnapContext(area, 1, DragSnapMode.Floating, "test", 1);
        Assert.IsTrue(session.TryResolve(new Rect(300, 0, 200, 40), context, false, out _));
        session.Show();
        Assert.IsNotNull(guide);
        var hwnd = new WindowInteropHelper(guide).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        Assert.AreEqual(WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | 0x80, style & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | 0x80));
        Assert.IsFalse(guide.IsActive);
        Assert.IsFalse(guide.IsHitTestVisible);
        Assert.IsFalse(guide.Focusable);
        session.Reset();
        Assert.IsFalse(guide.IsVisible);
        session.TryResolve(new Rect(300, 0, 200, 40), context, false, out _);
        session.Show();
        Assert.AreEqual(hwnd, new WindowInteropHelper(guide).Handle, "同一宿主应复用提示句柄。");
        session.Dispose();
        Assert.IsFalse(IsWindow(hwnd));
        using var shuttingDown = new DragSnapSession(() => guide = new DragSnapGuideWindow());
        shuttingDown.TryResolve(new Rect(300, 0, 200, 40), context, false, out _);
        shuttingDown.Show();
        guide.Close(); // Application 也可能先关闭提示窗，宿主随后才停止。
        shuttingDown.Reset();
        shuttingDown.Dispose();
    }
}
