// 屏幕批量取色所需的 GDI 声明；DC 和 DIB 由 ScreenBackgroundSampler 串行持有并释放。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Interop;

public static partial class NativeMethods
{
    internal const uint ScreenCopyRasterOperation = 0x00CC0020 | 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenBitmapInfo
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ClrUsed;
        internal uint ClrImportant;
        internal uint Colors;
    }

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint value);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(nint hdc, in ScreenBitmapInfo info, uint usage,
        out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdiFlush();
}
