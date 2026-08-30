// Monitor enumeration, ported from FluentFlyout
// (https://github.com/ManualDinosaur/FluentFlyout, GPL-3.0-or-later).
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Utils;

public static class MonitorUtil
{
    public struct MonitorInfo
    {
        public Rect monitorArea;
        public Rect workArea;
        public bool isPrimary;
        public uint dpiX;
        public uint dpiY;
        public string deviceId;
        public string deviceName;
    }

    public static MonitorInfo GetSelectedMonitor(int index = 0)
    {
        var monitors = GetMonitors();
        return monitors[Math.Clamp(index, 0, monitors.Count - 1)];
    }

    private static MonitorInfo GetMonitorInfoInternal(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX();
        info.cbSize = Marshal.SizeOf<MONITORINFOEX>();

        if (GetMonitorInfo(hMonitor, ref info))
        {
            var newInfo = new MonitorInfo
            {
                monitorArea = new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
                workArea = new Rect(info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
                isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                deviceId = info.szDevice,
                deviceName = GetMonitorFriendlyName(info.szDevice)
            };

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == S_OK)
            {
                newInfo.dpiX = dpiX;
                newInfo.dpiY = dpiY;
            }
            else
            {
                newInfo.dpiX = 96;
                newInfo.dpiY = 96;
            }

            return newInfo;
        }

        return new MonitorInfo(); // defaults: empty rects/strings
    }

    public static MonitorInfo GetMonitor(IntPtr hwnd, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        var hMonitor = MonitorFromWindow(hwnd, (int)flag);
        return GetMonitorInfoInternal(hMonitor);
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        List<MonitorInfo> result = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, ref _, _) =>
            {
                result.Add(GetMonitorInfoInternal(hMonitor));
                return true;
            },
            IntPtr.Zero);

        return result
            .OrderByDescending(m => m.isPrimary)
            .ThenBy(m => m.monitorArea.Left)
            .ToList();
    }

    private static string GetMonitorFriendlyName(string deviceId)
    {
        var displayDevice = new DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<DISPLAY_DEVICE>()
        };

        // Enumerate all display devices to find the display given by deviceId
        if (EnumDisplayDevices(deviceId, 0, ref displayDevice, 0))
        {
            return displayDevice.DeviceString.Trim(); // e.g. "Dell U2720Q"
        }
        return "Unknown Monitor";
    }
}
