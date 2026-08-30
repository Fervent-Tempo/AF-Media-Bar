// Window helpers, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Utils;

public static class WindowHelper
{
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

    public static void SetVisibility(Window window, bool visible) // show/hide without the WPF Visibility delay
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0,
            (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }
}
