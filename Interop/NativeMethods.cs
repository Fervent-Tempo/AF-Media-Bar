using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarPlayer.Interop;

internal static class NativeMethods
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const int GwlpHwndParent = -8;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;
    internal const long WsCaption = 0x00C00000L;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const int SwRestore = 9;

    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const int ObjIdWindow = 0x00000000;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;

    internal const int WmApp = 0x8000;
    internal const int WmContextMenu = 0x007B;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmLeftButtonDown = 0x0201;
    internal const int WmLeftButtonDoubleClick = 0x0203;
    internal const int WmRightButtonDown = 0x0204;
    internal const int WmRightButtonUp = 0x0205;
    internal const int HtClient = 1;
    internal const int IdiApplication = 32512;
    internal const int WhMouseLowLevel = 14;

    internal const uint AbmGetState = 0x00000004;
    internal const uint AbmGetTaskbarPos = 0x00000005;
    internal const uint AbsAutoHide = 0x00000001;

    internal const uint ErrorSuccess = 0;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhFmtDouble = 0x00000200;
    internal const uint PdhStatusValidData = 0x00000000;
    internal const uint PdhStatusNewData = 0x00000001;

    internal const uint NotifyIconAdd = 0x00000000;
    internal const uint NotifyIconModify = 0x00000001;
    internal const uint NotifyIconDelete = 0x00000002;
    internal const uint NotifyIconSetVersion = 0x00000004;
    internal const uint NotifyIconMessage = 0x00000001;
    internal const uint NotifyIconIcon = 0x00000002;
    internal const uint NotifyIconTip = 0x00000004;
    internal const uint NotifyIconShowTip = 0x00000080;
    internal const uint NotifyIconVersion4 = 4;

    private const uint MonitorDefaultToNearest = 0x00000002;

    internal static readonly nint HwndTopmost = new(-1);

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

    internal delegate bool EnumWindowsDelegate(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsDelegate callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint newLong);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint LoadIcon(nint instance, nint iconName);

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
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

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
    internal static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll")]
    internal static extern uint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    internal static bool ShouldHideForFullScreenApp(nint playerWindow)
    {
        var foreground = GetForegroundWindow();
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (foreground == nint.Zero || foreground == playerWindow || foreground == taskbar)
        {
            return false;
        }

        var className = new StringBuilder(128);
        GetClassName(foreground, className, className.Capacity);
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
    internal struct Rect : IEquatable<Rect>
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;

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
    internal struct Point
    {
        internal int X;
        internal int Y;
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
    internal struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tooltip;

        internal uint State;
        internal uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Info;

        internal uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string InfoTitle;

        internal uint InfoFlags;
        internal Guid GuidItem;
        internal nint BalloonIcon;

        internal static NotifyIconData Create(
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
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect WorkArea;
        internal uint Flags;

        internal static MonitorInfo Create()
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
