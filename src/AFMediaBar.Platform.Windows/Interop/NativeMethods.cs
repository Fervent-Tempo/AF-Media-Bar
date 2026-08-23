using System.Runtime.InteropServices;
using System.Text;

// 仅从 System32 解析原生库，避免工作目录中的同名 DLL 被加载。
// Resolve native libraries only from System32 to prevent DLL preloading.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AFMediaBar.Interop;

/// <summary>
/// 集中声明 AF Media Bar 使用的 Win32、Shell 和系统性能 API。
/// Centralizes the Win32, Shell, and system-performance APIs used by AF Media Bar.
/// </summary>
public static class NativeMethods
{
    // WinUser.h：窗口样式与 SetWindowPos 标志。
    // WinUser.h: window styles and SetWindowPos flags.
    internal const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    internal const int GwlpHwndParent = -8;
    internal const long WsChild = 0x40000000L;
    public const long WsPopup = 0x80000000L;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    internal const long WsCaption = 0x00C00000L;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    internal const int SwRestore = 9;
    internal const uint RdwInvalidate = 0x0001;
    internal const uint RdwErase = 0x0004;
    internal const uint RdwAllChildren = 0x0080;
    internal const uint RdwUpdateNow = 0x0100;

    // WinUser.h：无注入 WinEvent 监听所需的事件、对象和钩子标志。
    // WinUser.h: events, object IDs, and flags for out-of-process WinEvent hooks.
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    public const uint EventObjectLocationChange = 0x800B;
    internal const int ObjIdWindow = 0x00000000;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;

    // WinUser.h / shellapi.h：窗口消息、命中测试与通知区域协议。
    // WinUser.h / shellapi.h: window messages, hit testing, and tray-icon protocol.
    public const int WmApp = 0x8000;
    public const int WmDisplayChange = 0x007E;
    public const int WmContextMenu = 0x007B;
    public const int WmNcHitTest = 0x0084;
    public const int WmDeviceChange = 0x0219;
    public const int WmPowerBroadcast = 0x0218;
    public const int WmWtsSessionChange = 0x02B1;
    public const int WmDpiChanged = 0x02E0;
    internal const int WmLeftButtonDown = 0x0201;
    public const int WmLeftButtonDoubleClick = 0x0203;
    internal const int WmRightButtonDown = 0x0204;
    public const int WmRightButtonUp = 0x0205;
    public const int HtClient = 1;
    internal const int RgnOr = 2;
    public const int IdiApplication = 32512;
    internal const int WhMouseLowLevel = 14;
    public const int PbtApmSuspend = 0x0004;
    public const int PbtApmResumeSuspend = 0x0007;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int WtsSessionLock = 0x0007;
    public const int WtsSessionUnlock = 0x0008;
    public const int NotifyForThisSession = 0;

    // shellapi.h：AppBar 查询用于读取自动隐藏状态和任务栏矩形。
    // shellapi.h: AppBar queries expose auto-hide state and taskbar geometry.
    internal const uint AbmGetState = 0x00000004;
    internal const uint AbmGetTaskbarPos = 0x00000005;
    internal const uint AbsAutoHide = 0x00000001;

    internal const uint ErrorSuccess = 0;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhFmtDouble = 0x00000200;
    internal const uint PdhStatusValidData = 0x00000000;
    internal const uint PdhStatusNewData = 0x00000001;

    public const uint NotifyIconAdd = 0x00000000;
    public const uint NotifyIconModify = 0x00000001;
    public const uint NotifyIconDelete = 0x00000002;
    public const uint NotifyIconSetVersion = 0x00000004;
    public const uint NotifyIconMessage = 0x00000001;
    public const uint NotifyIconIcon = 0x00000002;
    public const uint NotifyIconTip = 0x00000004;
    public const uint NotifyIconShowTip = 0x00000080;
    public const uint NotifyIconVersion4 = 4;

    // MONITOR_DEFAULTTONEAREST 来自 WinUser.h；任务栏和浮动窗口的几何回退都需要稳定选择最近显示器。
    // MONITOR_DEFAULTTONEAREST comes from WinUser.h; taskbar and floating-window geometry fallbacks both need a stable nearest monitor.
    public const uint MonitorDefaultToNearest = 0x00000002;

    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint HwndTop = nint.Zero;

    internal delegate void WinEventDelegate(
        nint hook,
        uint eventId,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    internal delegate nint LowLevelMouseDelegate(
        int code,
        nint wParam,
        nint lParam);

    public delegate bool EnumWindowsDelegate(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsDelegate callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint window, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint window, int index, nint newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(nint window, nint region, bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    // 使用 GDI 区域并集让非矩形布局窗口只接收真实条带/触发区输入，避免隐藏折叠内容继续形成碰撞体积。
    // Combine GDI regions so non-rectangular layout windows receive input only on real strip/trigger surfaces; hidden collapsed content cannot remain a collision area.
    [DllImport("gdi32.dll")]
    internal static extern int CombineRgn(
        nint destination,
        nint sourceOne,
        nint sourceTwo,
        int combineMode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(
        nint window,
        nint updateRectangle,
        nint updateRegion,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelMouseDelegate callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(
        string? dataSource,
        nint userData,
        out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounter(
        nint query,
        string counterPath,
        nint userData,
        out nint counter);

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

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll")]
    internal static extern uint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetColorizationColor(
        out uint colorizationColor,
        [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport(
        "wtsapi32.dll",
        EntryPoint = "WTSRegisterSessionNotification",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WtsRegisterSessionNotification(nint window, int flags);

    [DllImport(
        "wtsapi32.dll",
        EntryPoint = "WTSUnRegisterSessionNotification",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WtsUnRegisterSessionNotification(nint window);

    public static bool ShouldHideForFullScreenApp(nint playerWindow)
    {
        var foreground = GetForegroundWindow();
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (foreground == nint.Zero || foreground == playerWindow || foreground == taskbar)
        {
            return false;
        }

        var className = new StringBuilder(128);
        if (GetClassName(foreground, className, className.Capacity) <= 0)
        {
            return false;
        }

        if (className.ToString() is "Progman" or "WorkerW" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        var style = GetWindowLongPtr(foreground, GwlStyle).ToInt64();
        if ((style & WsCaption) != 0 || !GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
            windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
            windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
            windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect : IEquatable<Rect>
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;

        public readonly bool Equals(Rect other)
        {
            return Left == other.Left &&
                Top == other.Top &&
                Right == other.Right &&
                Bottom == other.Bottom;
        }

        public override readonly bool Equals(object? value)
        {
            return value is Rect other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelMouseStruct
    {
        internal Point Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        internal uint Size;
        internal nint Window;
        internal uint CallbackMessage;
        internal uint Edge;
        internal Rect Rectangle;
        internal nint Parameter;

        internal static AppBarData Create()
        {
            return new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tooltip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;

        public static NotifyIconData Create(
            nint window,
            uint id,
            uint callbackMessage,
            nint icon)
        {
            return new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(),
                Window = window,
                Id = id,
                CallbackMessage = callbackMessage,
                Icon = icon,
                Tooltip = string.Empty,
                Info = string.Empty,
                InfoTitle = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        public static MonitorInfo Create()
        {
            return new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        }
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

        internal static MemoryStatusEx Create()
        {
            return new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;

        internal readonly ulong ToUInt64()
        {
            return ((ulong)HighDateTime << 32) | LowDateTime;
        }
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

}
