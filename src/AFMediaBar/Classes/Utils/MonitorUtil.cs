// 枚举显示器并提供显示区域信息。
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Resources;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 枚举显示器、工作区和有效 DPI 信息。
/// Enumerates displays, work areas, and effective DPI information.
/// </summary>
public static class MonitorUtil
{
    /// <summary>
    /// 表示单个显示器的屏幕、工作区和设备信息。
    /// Describes one display's screen, work-area, and device information.
    /// </summary>
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
        if (monitors.Count == 0)
            return default;
        return monitors[Math.Clamp(index, 0, monitors.Count - 1)];
    }

    private static MonitorInfo GetMonitorInfoInternal(IntPtr hMonitor, IReadOnlyDictionary<string, string>? friendlyNames = null)
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
                deviceName = friendlyNames?.GetValueOrDefault(info.szDevice) ?? GetMonitorFriendlyName(info.szDevice)
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
        var friendlyNames = GetDisplayConfigNames();
        List<MonitorInfo> result = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, ref _, _) =>
            {
                result.Add(GetMonitorInfoInternal(hMonitor, friendlyNames));
                return true;
            },
            IntPtr.Zero);

        return result
            .OrderByDescending(m => m.isPrimary)
            .ThenBy(m => m.monitorArea.Left)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> GetDisplayConfigNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 热插拔可能使首次申请的缓冲失效，有限重试后回退到旧名称来源。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(DisplayConfigOnlyActivePaths, out var pathCount, out var modeCount) != 0)
                break;
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var status = QueryDisplayConfig(DisplayConfigOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == DisplayConfigInsufficientBuffer) continue;
            if (status != 0) break;
            for (var index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                var source = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigGetSourceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    }
                };
                var target = new DisplayConfigTargetDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigGetTargetName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrWhiteSpace(source.ViewGdiDeviceName) ||
                    DisplayConfigGetDeviceInfo(ref target) != 0) continue;
                // LVDS、内嵌 DisplayPort/UDI 和 INTERNAL 对应内置面板。
                var name = target.OutputTechnology is 6 or 11 or 13 or 0x80000000
                    ? Translations.Get("Common.Monitor.Internal")
                    : target.MonitorFriendlyDeviceName?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) names.TryAdd(source.ViewGdiDeviceName, name);
            }
            break;
        }
        return names;
    }

    private static string GetMonitorFriendlyName(string deviceId)
    {
        var displayDevice = new DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<DISPLAY_DEVICE>()
        };

        // 显示配置不可用或没有型号时保留驱动名称作为回退。
        if (EnumDisplayDevices(deviceId, 0, ref displayDevice, 0))
        {
            return displayDevice.DeviceString.Trim();
        }
        return "Unknown Monitor";
    }
}
