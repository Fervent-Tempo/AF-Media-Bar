// Win32 interop declarations, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Runtime.InteropServices;
using System.Text;

namespace AFMediaBar.Classes.Interop;

/// <summary>
/// 集中声明 AFMediaBar 使用的 Windows 原生互操作常量、结构和函数。
/// Central declaration of Windows interop constants, structures, and functions used by AFMediaBar.
/// </summary>
public static partial class NativeMethods
{
    // window styles
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CHILD = 0x40000000;
    /// <summary>
    /// 调用 unchecked，提供 API。
    /// Provides the public unchecked entry point required by this component.
    /// </summary>
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // SetWindowPos flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const int SW_RESTORE = 9;

    // window messages
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DPICHANGED_AFTERPARENT = 0x02E3;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_GETOBJECT = 0x003D;
    public const int WM_SHOWWINDOW = 0x0018;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCDESTROY = 0x0082;
    public const int WM_IME_SETCONTEXT = 0x0281;
    public const int WM_IME_NOTIFY = 0x0282;
    public const int WM_APP = 0x8000;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int NIN_SELECT = 0x0400;
    public const int NIN_KEYSELECT = 0x0401;
    public const int NIN_POPUPOPEN = 0x0406;
    public const int WH_MOUSE_LL = 14;

    // Shell notification icon protocol
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1;
    public const uint NIF_ICON = 2;
    public const uint NIF_TIP = 4;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;

    // monitor
    public const int MONITOR_DEFAULTTONEAREST = 2;
    public const int MONITORINFOF_PRIMARY = 1;
    public const int S_OK = 0;

    // GDI region
    public const int RGN_OR = 2;

    /// <summary>监视器选择策略。/ Monitor selection policy.</summary>
    public enum MonitorFromWindowFlags : uint
    {
        DEFAULTTONULL = 0,
        DEFAULTTOPRIMARY = 1,
        DEFAULTTONEAREST = 2
    }

    /// <summary>监视器 DPI 类型。/ Monitor DPI type.</summary>
    public enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
        MDT_DEFAULT
    }

    /// <summary>屏幕坐标点。/ Screen coordinate point.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>整数矩形。/ Integer rectangle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>包含设备名称的监视器信息。/ Monitor information including the device name.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    /// <summary>显示设备枚举信息。/ Enumerated display-device information.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public DisplayDeviceStateFlags StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    /// <summary>显示设备状态标志。/ Display-device state flags.</summary>
    [Flags]
    public enum DisplayDeviceStateFlags : uint
    {
        DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1
    }

    /// <summary>窗口枚举回调。/ Window-enumeration callback.</summary>
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    /// <summary>监视器枚举回调。/ Monitor-enumeration callback.</summary>
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    /// <summary>低级鼠标钩子回调。/ Low-level mouse-hook callback.</summary>
    public delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>低级鼠标钩子数据。/ Low-level mouse hook data.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    /// <summary>Shell 通知区域图标数据。/ Shell notification-area icon data.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    /// <summary>Shell 通知区域图标标识。/ Shell notification-area icon identifier.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    /// <summary>
    /// 调用 FindWindow，提供 API。
    /// Provides the public FindWindow entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string lpClassName, string? lpWindowName);

    /// <summary>
    /// 调用 FindWindowEx，提供 API。
    /// Provides the public FindWindowEx entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowName);

    /// <summary>
    /// 调用 EnumWindows，提供 API。
    /// Provides the public EnumWindows entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    /// <summary>
    /// 调用 EnumThreadWindows，提供 API。
    /// Provides the public EnumThreadWindows entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc enumProc, IntPtr lParam);

    /// <summary>
    /// 调用 GetClassName，提供 API。
    /// Provides the public GetClassName entry point required by this component.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// 调用 GetWindowThreadProcessId，提供 API。
    /// Provides the public GetWindowThreadProcessId entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    /// <summary>
    /// 调用 GetWindowThreadProcessId，提供 API。
    /// Provides the public GetWindowThreadProcessId entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    /// <summary>
    /// 调用 SetParent，提供 API。
    /// Provides the public SetParent entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    /// <summary>
    /// 调用 GetParent，提供 API。
    /// Provides the public GetParent entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetParent(IntPtr hWnd);

    /// <summary>
    /// 调用 GetWindowRect，提供 API。
    /// Provides the public GetWindowRect entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// 调用 ShowWindow，提供 API。
    /// Provides the public ShowWindow entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// 调用 SetForegroundWindow，提供 API。
    /// Provides the public SetForegroundWindow entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 调用 GetWindowLong，提供 API。
    /// Provides the public GetWindowLong entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>
    /// 调用 SetWindowLong，提供 API。
    /// Provides the public SetWindowLong entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// 调用 GetDpiForWindow，提供 API。
    /// Provides the public GetDpiForWindow entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// 调用 ScreenToClient，提供 API。
    /// Provides the public ScreenToClient entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// 调用 GetCursorPos，提供 API。
    /// Provides the public GetCursorPos entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// 调用 EnumDisplayMonitors，提供 API。
    /// Provides the public EnumDisplayMonitors entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    /// <summary>
    /// 调用 MonitorFromWindow，提供 API。
    /// Provides the public MonitorFromWindow entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    /// <summary>
    /// 调用 MonitorFromPoint，提供 API。
    /// Provides the public MonitorFromPoint entry point required by this component.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, int flags);

    /// <summary>
    /// 调用 RegisterWindowMessage，提供 API。
    /// Provides the public RegisterWindowMessage entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterWindowMessage(string lpString);

    /// <summary>
    /// 调用 ShellNotifyIcon，提供 API。
    /// Provides the public ShellNotifyIcon entry point required by this component.
    /// </summary>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NOTIFYICONDATA data);

    /// <summary>
    /// 调用 Shell_NotifyIconGetRect，提供 API。
    /// Provides the public Shell_NotifyIconGetRect entry point required by this component.
    /// </summary>
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    /// <summary>
    /// 调用 ExtractIconEx，提供 API。
    /// Provides the public ExtractIconEx entry point required by this component.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string fileName, int iconIndex, out IntPtr largeIcon, out IntPtr smallIcon, uint icons);

    /// <summary>
    /// 调用 DestroyIcon，提供 API。
    /// Provides the public DestroyIcon entry point required by this component.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    /// <summary>
    /// 调用 SetWindowsHookEx，提供 API。
    /// Provides the public SetWindowsHookEx entry point required by this component.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

    /// <summary>
    /// 调用 UnhookWindowsHookEx，提供 API。
    /// Provides the public UnhookWindowsHookEx entry point required by this component.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    /// <summary>
    /// 调用 CallNextHookEx，提供 API。
    /// Provides the public CallNextHookEx entry point required by this component.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 调用 GetModuleHandle，提供 API。
    /// Provides the public GetModuleHandle entry point required by this component.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    /// <summary>
    /// 调用 SetWindowRgn，提供 API。
    /// Provides the public SetWindowRgn entry point required by this component.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    // DllImport instead of LibraryImport for SetWindowPos because for some reason it functions differently when using
    // LibraryImport, causing windows to not be topmost and it to be hidden unless you focus on the taskbar.
    /// <summary>
    /// 调用 SetWindowPos，提供 API。
    /// Provides the public SetWindowPos entry point required by this component.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// 调用 EnumDisplayDevices，提供 API。
    /// Provides the public EnumDisplayDevices entry point required by this component.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    /// <summary>
    /// 调用 GetMonitorInfo，提供 API。
    /// Provides the public GetMonitorInfo entry point required by this component.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>
    /// 调用 GetDpiForMonitor，提供 API。
    /// Provides the public GetDpiForMonitor entry point required by this component.
    /// </summary>
    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// 调用 CreateRectRgn，提供 API。
    /// Provides the public CreateRectRgn entry point required by this component.
    /// </summary>
    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    /// <summary>
    /// 调用 CombineRgn，提供 API。
    /// Provides the public CombineRgn entry point required by this component.
    /// </summary>
    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    /// <summary>
    /// 调用 DeleteObject，提供 API。
    /// Provides the public DeleteObject entry point required by this component.
    /// </summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);
}
