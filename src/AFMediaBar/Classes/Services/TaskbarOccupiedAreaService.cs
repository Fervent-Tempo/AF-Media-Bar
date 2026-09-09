using System.Runtime.InteropServices;
using System.Windows.Automation;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using Microsoft.Win32;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 任务栏占用区域探测：为媒体栏计算不会覆盖任务栏图标的主轴空闲区间。
/// Taskbar occupancy probe: calculates primary-axis free ranges that do not cover taskbar icons.
///
/// UI Automation 优先读取 Windows 任务栏按钮；Shell 子窗口和保守区间作为回退。
/// UI Automation is preferred for Windows taskbar buttons; shell child windows and conservative ranges are fallbacks.
/// </summary>
public sealed class TaskbarOccupiedAreaService
{
    private const int MinimumElementPrimaryPixels = 8;
    private const int MaximumElementPrimaryPixels = 260;
    private static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromMilliseconds(250);
    private IntPtr _cachedTaskbarHandle;
    private RECT _cachedTaskbarRect;
    private LayoutOrientation _cachedOrientation;
    private double _cachedDpiScale;
    private int _cachedEdgePaddingPixels;
    private DateTime _cachedAtUtc;
    private IReadOnlyList<TaskbarPrimaryRange> _cachedRanges = [];

    /// <summary>
    /// 探测任务栏按钮和托盘占用区域，并返回带安全间距的可用主轴区间。
    /// Probes taskbar buttons and tray occupancy, returning free primary-axis ranges with safety gaps.
    /// </summary>
    /// <param name="taskbarHandle">任务栏窗口句柄 / Taskbar window handle.</param>
    /// <param name="taskbarRect">任务栏屏幕矩形 / Taskbar screen rectangle.</param>
    /// <param name="orientation">布局主轴方向 / Layout primary-axis orientation.</param>
    /// <param name="dpiScale">任务栏 DPI 缩放 / Taskbar DPI scale.</param>
    /// <param name="edgePaddingPixels">媒体栏边缘留白 / Media-bar edge padding.</param>
    /// <returns>按主轴顺序排列的安全区间 / Safe ranges ordered along the primary axis.</returns>
    public IReadOnlyList<TaskbarPrimaryRange> GetSafePrimaryRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        if (taskbarHandle == _cachedTaskbarHandle &&
            taskbarRect.Equals(_cachedTaskbarRect) &&
            orientation == _cachedOrientation &&
            Math.Abs(dpiScale - _cachedDpiScale) < 0.01 &&
            edgePaddingPixels == _cachedEdgePaddingPixels &&
            DateTime.UtcNow - _cachedAtUtc < ProbeCacheDuration)
        {
            return _cachedRanges;
        }

        var primaryLength = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        if (primaryLength <= 0)
            return [];

        var occupied = new List<TaskbarPrimaryRange>();
        TryCollectAutomationRanges(taskbarHandle, taskbarRect, orientation, primaryLength, occupied);
        var hasAutomationRanges = occupied.Count > 0;

        // TrayNotifyWnd and taskband remain useful on systems where the XAML taskbar tree is not exposed to UIA.
        AddShellFallbackRange(taskbarHandle, "TrayNotifyWnd", taskbarRect, orientation, primaryLength, occupied);
        if (!hasAutomationRanges)
        {
            AddShellFallbackRange(taskbarHandle, "MSTaskSwWClass", taskbarRect, orientation, primaryLength, occupied);
            AddShellFallbackRange(taskbarHandle, "MSTaskListWClass", taskbarRect, orientation, primaryLength, occupied);
        }

        var gap = Math.Max(8, (int)Math.Round(8 * Math.Max(1, dpiScale)));
        var ranges = CalculateFreeRanges(primaryLength, occupied, Math.Max(0, edgePaddingPixels), gap);
        _cachedTaskbarHandle = taskbarHandle;
        _cachedTaskbarRect = taskbarRect;
        _cachedOrientation = orientation;
        _cachedDpiScale = dpiScale;
        _cachedEdgePaddingPixels = edgePaddingPixels;
        _cachedAtUtc = DateTime.UtcNow;
        _cachedRanges = ranges;
        return ranges;
    }

    /// <summary>
    /// 根据占用区间计算安全空闲区间；结果按主轴从小到大排列。
    /// Calculates safe free ranges from occupied intervals, ordered along the primary axis.
    /// </summary>
    public static IReadOnlyList<TaskbarPrimaryRange> CalculateFreeRanges(
        int primaryLength,
        IReadOnlyList<TaskbarPrimaryRange> occupied,
        int edgePaddingPixels,
        int gapPixels)
    {
        var start = Math.Clamp(edgePaddingPixels, 0, Math.Max(0, primaryLength));
        var end = Math.Clamp(primaryLength - edgePaddingPixels, start, primaryLength);
        if (end <= start)
            return [];

        var merged = occupied
            .Select(range => new TaskbarPrimaryRange(
                Math.Clamp(range.Start - gapPixels, start, end),
                Math.Clamp(range.End + gapPixels, start, end)))
            .Where(range => range.End > range.Start)
            .OrderBy(range => range.Start)
            .ToList();

        var result = new List<TaskbarPrimaryRange>();
        var cursor = start;
        foreach (var range in MergeRanges(merged))
        {
            if (range.Start > cursor)
                result.Add(new TaskbarPrimaryRange(cursor, range.Start));
            cursor = Math.Max(cursor, range.End);
        }

        if (cursor < end)
            result.Add(new TaskbarPrimaryRange(cursor, end));

        return result.Where(range => range.Length > 0).ToArray();
    }

    private static IEnumerable<TaskbarPrimaryRange> MergeRanges(IEnumerable<TaskbarPrimaryRange> ranges)
    {
        TaskbarPrimaryRange? current = null;
        foreach (var range in ranges)
        {
            if (current is not { } active)
            {
                current = range;
                continue;
            }

            if (range.Start <= active.End)
            {
                current = new TaskbarPrimaryRange(active.Start, Math.Max(active.End, range.End));
                continue;
            }

            yield return active;
            current = range;
        }

        if (current is { } last)
            yield return last;
    }

    private static void TryCollectAutomationRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbarHandle);
            var walker = TreeWalker.ControlViewWalker;
            Visit(root, walker, taskbarRect, orientation, primaryLength, occupied);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void Visit(
        AutomationElement element,
        TreeWalker walker,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied)
    {
        AutomationElement? child = null;
        try
        {
            var current = element.Current;
            var type = current.ControlType;
            if (!current.IsOffscreen &&
                (type == ControlType.Button ||
                 type == ControlType.SplitButton ||
                 type == ControlType.MenuItem ||
                 type == ControlType.ListItem))
            {
                var bounds = current.BoundingRectangle;
                if (IsUsefulTaskbarElement(bounds, taskbarRect, orientation, primaryLength))
                {
                    var start = orientation == LayoutOrientation.Horizontal
                        ? (int)Math.Round(bounds.Left - taskbarRect.Left)
                        : (int)Math.Round(bounds.Top - taskbarRect.Top);
                    var length = orientation == LayoutOrientation.Horizontal
                        ? (int)Math.Round(bounds.Width)
                        : (int)Math.Round(bounds.Height);
                    occupied.Add(new TaskbarPrimaryRange(start, start + length));
                }
            }

            child = walker.GetFirstChild(element);
            while (child is not null)
            {
                Visit(child, walker, taskbarRect, orientation, primaryLength, occupied);
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }
    }

    private static bool IsUsefulTaskbarElement(
        System.Windows.Rect bounds,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength)
    {
        var crossOverlap = orientation == LayoutOrientation.Horizontal
            ? Math.Min(bounds.Bottom, taskbarRect.Bottom) - Math.Max(bounds.Top, taskbarRect.Top)
            : Math.Min(bounds.Right, taskbarRect.Right) - Math.Max(bounds.Left, taskbarRect.Left);
        if (crossOverlap <= 0)
            return false;

        var primary = orientation == LayoutOrientation.Horizontal ? bounds.Width : bounds.Height;
        if (primary < MinimumElementPrimaryPixels || primary > Math.Min(MaximumElementPrimaryPixels, primaryLength * 0.45))
            return false;

        return true;
    }

    private static void AddShellFallbackRange(
        IntPtr taskbarHandle,
        string className,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied)
    {
        var child = FindDescendantByClass(taskbarHandle, className);
        if (child == IntPtr.Zero || !GetWindowRect(child, out var rect))
            return;

        var start = orientation == LayoutOrientation.Horizontal
            ? rect.Left - taskbarRect.Left
            : rect.Top - taskbarRect.Top;
        var end = orientation == LayoutOrientation.Horizontal
            ? rect.Right - taskbarRect.Left
            : rect.Bottom - taskbarRect.Top;
        start = Math.Clamp(start, 0, primaryLength);
        end = Math.Clamp(end, start, primaryLength);
        if (end > start)
            occupied.Add(new TaskbarPrimaryRange(start, end));
    }

    private static IntPtr FindDescendantByClass(IntPtr parent, string className)
    {
        for (var child = FindWindowEx(parent, IntPtr.Zero, null, null);
             child != IntPtr.Zero;
             child = FindWindowEx(parent, child, null, null))
        {
            var buffer = new System.Text.StringBuilder(256);
            GetClassName(child, buffer, buffer.Capacity);
            if (string.Equals(buffer.ToString(), className, StringComparison.Ordinal))
                return child;

            var nested = FindDescendantByClass(child, className);
            if (nested != IntPtr.Zero)
                return nested;
        }

        return IntPtr.Zero;
    }
}

/// <summary>任务栏主轴区间（物理像素）。/ Taskbar primary-axis interval in physical pixels.</summary>
public readonly record struct TaskbarPrimaryRange(int Start, int End)
{
    public int Length => Math.Max(0, End - Start);
}
