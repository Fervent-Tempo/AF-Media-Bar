// 用真实顶层窗口验证来源窗口筛选；窗口由独立 STA 创建并释放，不激活外部应用。
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止通信窗口代替隐藏的播放器主界面被激活。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MediaSourceWindowActivationTests
{
    [TestMethod]
    public void FindsHiddenMainWindowAndRejectsAuxiliaryAndOwnedWindows()
    {
        StaTest.Run(_ =>
        {
            const int Popup = unchecked((int)0x80000000);
            const int Visible = 0x10000000;
            const int AppWindow = 0x00040000;
            const int NoActivate = 0x08000000;
            const int ToolWindow = 0x00000080;
            using var main = new HwndSource(new HwndSourceParameters("Hidden player")
            {
                WindowStyle = Popup, ExtendedWindowStyle = AppWindow, Width = 240, Height = 160
            });
            using var auxiliary = new HwndSource(new HwndSourceParameters("Transport receiver")
            {
                WindowStyle = Popup | Visible, ExtendedWindowStyle = NoActivate | ToolWindow,
                Width = 16, Height = 16
            });
            using var tool = new HwndSource(new HwndSourceParameters("Utility")
            {
                WindowStyle = Popup, ExtendedWindowStyle = ToolWindow, Width = 120, Height = 80
            });
            using var owned = new HwndSource(new HwndSourceParameters("Owned helper")
            {
                WindowStyle = Popup, ExtendedWindowStyle = AppWindow, ParentWindow = main.Handle,
                Width = 120, Height = 80
            });

            var windows = MediaSourceActivationService.FindActivationWindows(Environment.ProcessId);
            CollectionAssert.Contains(windows.ToArray(), main.Handle);
            CollectionAssert.DoesNotContain(windows.ToArray(), auxiliary.Handle);
            CollectionAssert.DoesNotContain(windows.ToArray(), tool.Handle);
            CollectionAssert.DoesNotContain(windows.ToArray(), owned.Handle);
            CollectionAssert.DoesNotContain(
                MediaSourceActivationService.FindActivationWindows(0).ToArray(), main.Handle);
            return Task.CompletedTask;
        });
    }
}
