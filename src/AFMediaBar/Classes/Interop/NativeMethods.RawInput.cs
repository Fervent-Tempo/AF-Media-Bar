// Raw Input 注册和数据读取声明；接收窗口、缓冲与注销均由 RawMouseInputSource 拥有。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Interop;

/// <summary>Raw Input 消息与注册的 Windows 互操作声明。</summary>
public static partial class NativeMethods
{
    internal const uint PM_REMOVE = 1;
    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        internal uint Type;
        internal uint Size;
        internal IntPtr Device;
        internal IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRegisteredRawInputDevices([Out] RAWINPUTDEVICE[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    internal static extern int GetMessageTime();

    [DllImport("user32.dll")]
    internal static extern uint GetMessagePos();
}
