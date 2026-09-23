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
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

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
    public const int WM_QUIT = 0x0012;
    public const int WM_IME_SETCONTEXT = 0x0281;
    public const int WM_IME_NOTIFY = 0x0282;
    public const int WM_APP = 0x8000;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const int OBJID_WINDOW = 0;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint PM_NOREMOVE = 0x0000;
    public const int NIN_SELECT = 0x0400;
    public const int NIN_KEYSELECT = 0x0401;

    /// <summary>气泡通知被点击。/ The balloon notification was clicked.</summary>
    public const int NIN_BALLOONUSERCLICK = 0x0405;
    public const int NIN_POPUPOPEN = 0x0406;
    public const int WH_MOUSE_LL = 14;
    public const int VK_SHIFT = 0x10;
    internal const uint ErrorSuccess = 0;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhFmtDouble = 0x00000200;
    internal const uint PdhStatusValidData = 0x00000000;
    internal const uint PdhStatusNewData = 0x00000001;

    // Shell notification icon protocol
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1;
    public const uint NIF_ICON = 2;
    public const uint NIF_TIP = 4;
    public const uint NIF_SHOWTIP = 0x80;

    /// <summary>本次调用携带气泡通知内容。/ This call carries balloon-notification content.</summary>
    public const uint NIF_INFO = 0x10;

    /// <summary>信息类气泡（蓝色图标）。/ Informational balloon, which uses the information icon.</summary>
    public const uint NIIF_INFO = 0x1;
    public const uint NOTIFYICON_VERSION_4 = 4;

    // monitor
    public const int MONITOR_DEFAULTTONEAREST = 2;
    public const int MONITORINFOF_PRIMARY = 1;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint CLR_INVALID = 0xFFFFFFFF;
    public const int S_OK = 0;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

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
    /// <summary>WinEvent 回调。/ WinEvent callback.</summary>
    public delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

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

    /// <summary>线程消息循环中的原生消息。/ Native message used by a thread message loop.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;

        internal static MemoryStatusEx Create() => new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
        internal ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValueDouble
    {
        internal uint Status;
        internal double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValueItem
    {
        internal nint Name;
        internal PdhFmtCounterValueDouble Value;
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

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetParent(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>获取当前前台窗口。 / Gets the current foreground window.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    /// <summary>判断窗口是否可见。 / Determines whether a window is visible.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    /// <summary>取窗口层次里的指定祖先。/ Retrieves the requested ancestor of a window.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetAncestor(IntPtr hWnd, uint flags);

    /// <summary>取屏幕坐标点上的窗口（只返回可见、可命中测试的窗口）。/ Retrieves the window at a screen point, skipping invisible and hit-test-transparent windows.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr WindowFromPoint(POINT point);

    /// <summary>窗口层次中的顶层祖先标志，用于把命中到的子窗口提升成顶层窗口。/ Root-ancestor flag, used to promote a hit child window to its top-level window.</summary>
    public const uint GA_ROOT = 2;

    /// <summary>判断窗口是否最小化。 / Determines whether a window is minimized.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>注册异步窗口事件钩子。/ Installs an out-of-context window event hook.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHook,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    /// <summary>移除窗口事件钩子。/ Removes a window event hook.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr eventHook);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    /// <summary>获取屏幕或窗口设备上下文。/ Gets a device context for the screen or a window.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    /// <summary>释放通过 GetDC 获取的设备上下文。/ Releases a device context obtained through GetDC.</summary>
    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>读取系统度量值。/ Reads a system metric.</summary>
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, int flags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string fileName, int iconIndex, out IntPtr largeIcon, out IntPtr smallIcon, uint icons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    /// <summary>读取按键当前物理状态。/ Reads the current physical state of a key.</summary>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>读取当前线程消息。/ Reads a message from the current thread's message queue.</summary>
    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG message, IntPtr window, uint minimumMessage, uint maximumMessage);

    /// <summary>检查线程消息队列并确保该队列已创建。/ Examines and ensures creation of the thread message queue.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(
        out MSG message,
        IntPtr window,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    /// <summary>将虚拟键消息转换为字符消息。/ Translates virtual-key messages into character messages.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG message);

    /// <summary>将消息分派到窗口过程。/ Dispatches a message to its window procedure.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessage(ref MSG message);

    /// <summary>向指定线程的消息队列发送消息。/ Posts a message to the specified thread queue.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    /// <summary>返回当前线程的原生标识。/ Returns the native identifier of the current thread.</summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    internal static extern uint PdhGetFormattedCounterArray(
        nint counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        nint itemBuffer);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(nint query);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    // DllImport instead of LibraryImport for SetWindowPos because for some reason it functions differently when using
    // LibraryImport, causing windows to not be topmost and it to be hidden unless you focus on the taskbar.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>读取 DWM 窗口属性。 / Reads a DWM window attribute.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int valueSize);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    /// <summary>读取设备上下文中指定像素的 COLORREF 值。/ Reads the COLORREF value of a pixel in a device context.</summary>
    [LibraryImport("gdi32.dll")]
    public static partial uint GetPixel(IntPtr hdc, int x, int y);

    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);
}
