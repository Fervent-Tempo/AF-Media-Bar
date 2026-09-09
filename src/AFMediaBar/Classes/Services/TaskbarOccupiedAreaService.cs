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
    private static readonly TimeSpan ProbeResultTimeout = TimeSpan.FromMilliseconds(1500);
    private readonly object _cacheGate = new();
    private IntPtr _cachedTaskbarHandle;
    private RECT _cachedTaskbarRect;
    private LayoutOrientation _cachedOrientation;
    private double _cachedDpiScale;
    private int _cachedEdgePaddingPixels;
    private DateTime _cachedAtUtc;
    private IReadOnlyList<TaskbarPrimaryRange> _cachedRanges = [];
    private bool _probeInProgress;
    private DateTime _probeStartedAtUtc;
    private long _cacheGeneration;

    /// <summary>
    /// 探测任务栏按钮和托盘占用区域，并返回带安全间距的可用主轴区间。
    /// Probes taskbar buttons and tray occupancy, returning free primary-axis ranges with safety gaps.
    /// </summary>
    /// <param name="taskbarHandle">任务栏窗口句柄 / Taskbar window handle.</param>
    /// <param name="taskbarRect">任务栏屏幕矩形 / Taskbar screen rectangle.</param>
    /// <param name="orientation">布局主轴方向 / Layout primary-axis orientation.</param>
    /// <param name="dpiScale">任务栏 DPI 缩放 / Taskbar DPI scale.</param>
    /// <param name="edgePaddingPixels">媒体栏边缘留白 / Media-bar edge padding.</param>
    /// <returns>按主轴顺序排列的已缓存安全区间；探测未完成时返回空集合。 / Cached safe ranges ordered along the primary axis; empty while a probe is pending.</returns>
    public IReadOnlyList<TaskbarPrimaryRange> GetSafePrimaryRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        IReadOnlyList<TaskbarPrimaryRange> availableCache;
        var startProbe = false;
        var probeGeneration = 0L;
        var probeStartedAtUtc = default(DateTime);
        lock (_cacheGate)
        {
            var now = DateTime.UtcNow;
            var cacheMatches = MatchesCacheKey(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels);
            if (cacheMatches && now - _cachedAtUtc < ProbeCacheDuration)
                return _cachedRanges;

            var activeProbeTimedOut = _probeInProgress && now - _probeStartedAtUtc > ProbeResultTimeout;
            availableCache = cacheMatches && !activeProbeTimedOut ? _cachedRanges : [];
            if (!_probeInProgress)
            {
                _probeInProgress = true;
                _probeStartedAtUtc = now;
                probeGeneration = _cacheGeneration;
                probeStartedAtUtc = now;
                startProbe = true;
            }
        }

        if (startProbe)
        {
            StartProbeThread(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                probeGeneration,
                probeStartedAtUtc);
        }

        // UI Automation 可能等待 Explorer；持有 Explorer 子 HWND 的 WPF 线程绝不能同步等待。
        // UI Automation may wait on Explorer. Never block the WPF thread that owns an
        // Explorer child HWND; use the previous matching snapshot or the caller's fallback.
        return availableCache;
    }

    /// <summary>
    /// 使当前占用区缓存失效；正在运行的旧探测完成后不会发布结果。
    /// Invalidates the current occupancy cache; an in-flight older probe will not publish its result.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _cachedTaskbarHandle = IntPtr.Zero;
            _cachedTaskbarRect = default;
            _cachedDpiScale = 0;
            _cachedAtUtc = default;
            _cachedRanges = [];
        }
    }

    private void StartProbeThread(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        long probeGeneration,
        DateTime probeStartedAtUtc)
    {
        try
        {
            var thread = new Thread(() => ProbeAndCache(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                probeGeneration,
                probeStartedAtUtc))
            {
                IsBackground = true,
                Name = "AFMediaBar.TaskbarUiaProbe"
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        catch (Exception)
        {
            lock (_cacheGate)
            {
                _probeInProgress = false;
            }
        }
    }

    private void ProbeAndCache(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        long probeGeneration,
        DateTime probeStartedAtUtc)
    {
        try
        {
            var ranges = ProbeSafePrimaryRanges(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels);
            lock (_cacheGate)
            {
                var completedAtUtc = DateTime.UtcNow;
                if (probeGeneration == _cacheGeneration &&
                    completedAtUtc - probeStartedAtUtc <= ProbeResultTimeout)
                {
                    _cachedTaskbarHandle = taskbarHandle;
                    _cachedTaskbarRect = taskbarRect;
                    _cachedOrientation = orientation;
                    _cachedDpiScale = dpiScale;
                    _cachedEdgePaddingPixels = edgePaddingPixels;
                    _cachedAtUtc = completedAtUtc;
                    _cachedRanges = ranges;
                }
            }
        }
        catch (Exception)
        {
            // Explorer 可能正在重启或替换 UI Automation 树；调用方继续使用保守区间，后续定位周期会重试。
            // Explorer may be restarting or replacing its UI Automation tree. The caller
            // keeps using a conservative range and a later positioning tick retries.
        }
        finally
        {
            lock (_cacheGate)
            {
                _probeInProgress = false;
            }
        }
    }

    private static IReadOnlyList<TaskbarPrimaryRange> ProbeSafePrimaryRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
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
        return CalculateFreeRanges(primaryLength, occupied, Math.Max(0, edgePaddingPixels), gap);
    }

    private bool MatchesCacheKey(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        return taskbarHandle == _cachedTaskbarHandle &&
               taskbarRect.Equals(_cachedTaskbarRect) &&
               orientation == _cachedOrientation &&
               Math.Abs(dpiScale - _cachedDpiScale) < 0.01 &&
               edgePaddingPixels == _cachedEdgePaddingPixels;
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
            var interactiveControlCondition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var cacheRequest = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                TreeFilter = Automation.ControlViewCondition
            };
            cacheRequest.Add(AutomationElement.IsOffscreenProperty);
            cacheRequest.Add(AutomationElement.BoundingRectangleProperty);

            AutomationElementCollection elements;
            using (cacheRequest.Activate())
            {
                elements = root.FindAll(TreeScope.Descendants, interactiveControlCondition);
            }

            foreach (AutomationElement element in elements)
            {
                try
                {
                    if (element.Cached.IsOffscreen)
                        continue;

                    AddAutomationRange(
                        element.Cached.BoundingRectangle,
                        taskbarRect,
                        orientation,
                        primaryLength,
                        occupied);
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
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

    private static void AddAutomationRange(
        System.Windows.Rect bounds,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied)
    {
        if (!IsUsefulTaskbarElement(bounds, taskbarRect, orientation, primaryLength))
            return;

        var start = orientation == LayoutOrientation.Horizontal
            ? (int)Math.Round(bounds.Left - taskbarRect.Left)
            : (int)Math.Round(bounds.Top - taskbarRect.Top);
        var length = orientation == LayoutOrientation.Horizontal
            ? (int)Math.Round(bounds.Width)
            : (int)Math.Round(bounds.Height);
        occupied.Add(new TaskbarPrimaryRange(start, start + length));
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
