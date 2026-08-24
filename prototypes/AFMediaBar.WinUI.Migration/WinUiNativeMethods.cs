using System.Runtime.InteropServices;
using AFMediaBar.Interop;

namespace AFMediaBar.WinUI;

internal static class WinUiNativeMethods
{
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsPopup = 0x80000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;

    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmWindowCornerDoNotRound = 1;

    internal static bool ResizeClientWindow(nint window, int clientWidth, int clientHeight)
    {
        if (!NativeMethods.GetWindowRect(window, out var windowRect) ||
            !NativeMethods.GetClientRect(window, out var clientRect))
        {
            return false;
        }

        var frameWidth = Math.Max(0, windowRect.Width - clientRect.Width);
        var frameHeight = Math.Max(0, windowRect.Height - clientRect.Height);
        return SetWindowPos(
            window,
            nint.Zero,
            windowRect.Left,
            windowRect.Top,
            Math.Max(1, clientWidth + frameWidth),
            Math.Max(1, clientHeight + frameHeight),
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged);
    }

    internal static bool ConfigureBorderlessWindow(nint window)
    {
        var style = NativeMethods.GetWindowLongPtr(window, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsBorder | WsDlgFrame | WsThickFrame |
            WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        style |= WsPopup;
        NativeMethods.SetWindowLongPtr(window, GwlStyle, new nint(style));
        return SetWindowPos(
            window,
            nint.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate |
            SwpNoOwnerZOrder | SwpFrameChanged);
    }

    internal static int SetBorderColor(nint window, ref uint color) =>
        DwmSetWindowAttribute(window, DwmwaBorderColor, ref color, sizeof(uint));

    internal static int SetCornerPreference(nint window, ref int preference) =>
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref preference, sizeof(int));

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref uint value,
        uint valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        uint valueSize);
}
