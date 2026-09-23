// Window helpers, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 提供窗口激活和可见性相关的 Win32 适配操作。
/// Provides Win32 adapters for window activation and visibility behavior.
/// </summary>
public static class WindowHelper
{
    /// <summary>设置窗口不激活样式。/ Sets the no-activate window style.</summary>
    public static void SetNoActivate(Window window) // prevent window from stealing focus
    {
        window.ShowActivated = false;

        void ApplyNoActivateStyle()
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
                return;

            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        window.SourceInitialized += (sender, e) => ApplyNoActivateStyle();
        ApplyNoActivateStyle();
    }

    /// <summary>
    /// 临时切换分层窗口的鼠标穿透；任务栏移动期间使用它，避免子窗口抢走 Explorer 的边缘触发与显隐输入。
    /// Temporarily toggles mouse transparency for a layered window; taskbar motion uses it so the child cannot steal Explorer's
    /// edge-trigger and reveal/hide input.
    /// </summary>
    public static void SetInputTransparent(Window window, bool transparent)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLong(handle, GWL_EXSTYLE);
        var next = transparent ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (next != style)
            SetWindowLong(handle, GWL_EXSTYLE, next);
    }

    /// <summary>通过 Win32 显示或隐藏窗口。/ Shows or hides a window through Win32.</summary>
    public static void SetVisibility(Window window, bool visible) // show/hide without the WPF Visibility delay
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0,
            (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }
}
