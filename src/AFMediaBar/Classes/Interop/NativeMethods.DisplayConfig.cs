// 读取活动显示路径和显示器名称；数组由调用方持有，不保留原生资源。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Interop;

public static partial class NativeMethods
{
    internal const uint DisplayConfigOnlyActivePaths = 0x00000002;
    internal const int DisplayConfigInsufficientBuffer = 122;
    internal const uint DisplayConfigGetSourceName = 1;
    internal const uint DisplayConfigGetTargetName = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigLuid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        internal DisplayConfigLuid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        internal DisplayConfigLuid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal DisplayConfigRational RefreshRate;
        internal uint ScanLineOrdering;
        internal int TargetAvailable;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo SourceInfo;
        internal DisplayConfigPathTargetInfo TargetInfo;
        internal uint Flags;
    }

    // 原生模式结构为 16 字节头和 48 字节联合体；这里只接收缓冲，不读取模式数据。
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DisplayConfigModeInfo
    {
        [FieldOffset(0)] internal uint InfoType;
        [FieldOffset(4)] internal uint Id;
        [FieldOffset(8)] internal DisplayConfigLuid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal DisplayConfigLuid AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigTargetDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal uint Flags;
        internal uint OutputTechnology;
        internal ushort EdidManufactureId;
        internal ushort EdidProductCodeId;
        internal uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string MonitorDevicePath;
    }

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(uint flags, ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, nint topologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);
}
