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

    /// <summary>通过 Win32 显示或隐藏窗口。/ Shows or hides a window through Win32.</summary>
    public static void SetVisibility(Window window, bool visible) // show/hide without the WPF Visibility delay
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0,
            (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }
}
