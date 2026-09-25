using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Automation;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 使用 UI Automation 和 Shell 子窗口回退探测任务栏占用区域。
/// Probes taskbar occupancy with UI Automation and Shell child-window fallbacks.
///
/// 每次探测在单独的 MTA 后台线程运行，避免持有 Explorer 子 HWND 的 WPF 线程同步等待 Explorer。
/// Each probe runs on a dedicated MTA background thread so the WPF thread owning an Explorer child HWND never waits on Explorer.
/// </summary>
public sealed class TaskbarOccupiedAreaProbe : ITaskbarOccupiedAreaProbe
{
    private const int MinimumElementPrimaryPixels = 8;
    private const int MaximumElementPrimaryPixels = 260;

    /// <summary>
    /// 每个任务栏上一次写进日志的外部窗口描述：探测会重复运行，内容不变时不重复写。
    /// 多显示器会并发探测不同任务栏，因此签名按 HWND 隔离并在锁内更新。
    /// Last overlay description written to the log for each taskbar; repeated probes do not log unchanged content.
    /// Different taskbars are probed concurrently on multi-monitor systems, so signatures are isolated by HWND and updated under a lock.
    /// </summary>
    private static readonly object OverlaySignatureGate = new();
    private static readonly Dictionary<IntPtr, string> LastOverlaySignatures = [];

    /// <inheritdoc />
    public void Start(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed)
    {
        ArgumentNullException.ThrowIfNull(succeeded);
        ArgumentNullException.ThrowIfNull(failed);

        try
        {
            var thread = new Thread(() => Probe(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                succeeded,
                failed))
            {
                IsBackground = true,
                Name = "AFMediaBar.TaskbarUiaProbe"
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        catch (Exception)
        {
            failed();
        }
    }

    private static void Probe(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed)
    {
        IReadOnlyList<TaskbarPrimaryRange> ranges;
        try
        {
            ranges = ProbeSafePrimaryRanges(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels);
        }
        catch (Exception)
        {
            // Explorer 可能正在重启或替换 UI Automation 树；外层服务不会发布本次结果。
            // Explorer may be restarting or replacing its UI Automation tree; the outer service will not publish this result.
            failed();
            return;
        }

        succeeded(ranges);
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

        // 别的进程叠在任务栏上的窗口（Windows 10 的搜索框与任务视图）既枚举不到也躲不开，只能按"盖在任务栏上"识别。
        // Windows over the taskbar from other processes (the Windows 10 search box and Task View) are neither descendants nor
        // avoidable any other way, so they are identified by covering the taskbar.
        AddOverlayRanges(taskbarHandle, taskbarRect, orientation, primaryLength, occupied);

        var gap = Math.Max(8, (int)Math.Round(8 * Math.Max(1, dpiScale)));
        return TaskbarFreeRangeCalculator.Calculate(primaryLength, occupied, Math.Max(0, edgePaddingPixels), gap);
    }

    /// <summary>
    /// 把"盖在任务栏上、又不属于任务栏"的顶层窗口计入占用区间，两条采集方式取并集：
    ///
    /// ① **Z 序扫描**（`EnumWindows`，只看到任务栏**之上**的窗口）：与谁盖在谁上面无关，因此媒体栏自己压在搜索框上时也能发现它——
    /// 这正是 Windows 10 上"启动后卡在搜索框里、播放时又被放到正确位置"的成因：窄媒体栏落在搜索框那一段，采样时命中的是媒体栏
    /// 自己（属于任务栏），搜索框因此永远发现不了。
    /// ② **命中测试**（`WindowFromPoint`）：返回的正是**会抢走鼠标输入的那个窗口**，用来补上矩形不规则的覆盖窗口。
    ///
    /// 两条都按类名、进程与几何条件过滤，并把**被拒的候选**也写进同一行日志——现场机器（Release 构建）拿不到调试输出，
    /// 这一行是"媒体栏为什么放在这里/为什么没发现搜索框"的唯一证据。
    /// Counts top-level windows lying over the taskbar that do not belong to it, taking the union of two collections:
    ///
    /// 1. **Z-order scan** (`EnumWindows`, only windows **above** the taskbar): independent of who covers whom, so it still finds the
    ///    search box while the media bar sits on top of it — which is exactly the Windows 10 loop of "stuck in the search box at
    ///    startup, placed correctly while playing": a narrow bar lands on the search box, and hit testing then returns the bar itself
    ///    (part of the taskbar), so the search box can never be discovered.
    /// 2. **Hit test** (`WindowFromPoint`): returns the very window that would take the mouse input, covering overlays with irregular
    ///    rectangles.
    ///
    /// Both apply the class, process, and geometry filters, and **rejected candidates are logged in the same line**: a reporting
    /// machine runs a Release build with no debug output, and that line is the only evidence for why the bar ended up where it did.
    /// </summary>
    private static void AddOverlayRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied)
    {
        var ownProcessId = Environment.ProcessId;
        var seen = new HashSet<IntPtr>();
        var accepted = new List<(IntPtr Handle, TaskbarPrimaryRange Range)>();
        var rejected = new List<(IntPtr Handle, TaskbarOverlayRejection Reason)>();

        void Consider(IntPtr candidate)
        {
            if (candidate == IntPtr.Zero || candidate == taskbarHandle || IsDescendantOf(candidate, taskbarHandle))
                return;

            // 本程序自己的窗口（媒体栏、设置窗口、通知）不计入：媒体栏本身是任务栏的子窗口，而其余是我们自己的界面。
            // This application's own windows (the bar, settings, notification) are ignored: the bar is a child of the taskbar and the
            // rest are our own interface.
            _ = GetWindowThreadProcessId(candidate, out var processId);
            if (processId == ownProcessId)
                return;

            if (!IsWindowVisible(candidate))
                return;

            if (!seen.Add(candidate))
                return;

            if (!GetWindowRect(candidate, out var windowRect))
                return;

            if (TaskbarOverlayRangePolicy.TryResolve(windowRect, taskbarRect, orientation, out var range, out var rejection))
            {
                // 几何只能说明“有一个窄窗口盖在任务栏带上”，无法区分 Windows 10 搜索框与截图工具的浮动工具条。
                // 只有 Windows Shell 自己的体验进程可以进入占用区；普通应用覆盖任务栏时不应让媒体栏折叠或跳位。
                // Geometry only says that a narrow window overlaps the taskbar band; it cannot distinguish the Windows 10 search box
                // from a capture tool's floating toolbar. Only Windows Shell experience processes may become occupancy, because an
                // ordinary application covering the taskbar must not collapse or move the media bar.
                if (!TaskbarOverlayOwnerPolicy.IsShellOwned(TryGetProcessName(processId)))
                {
                    rejected.Add((candidate, TaskbarOverlayRejection.NotShellOwned));
                    return;
                }

                accepted.Add((candidate, range));
                occupied.Add(range);
                return;
            }

            // 不覆盖任务栏的窗口不值得记录：它们与"媒体栏放在哪里"无关。
            // Windows that do not lie over the taskbar are not worth recording: they cannot affect where the bar goes.
            if (rejection is TaskbarOverlayRejection.TooTall or TaskbarOverlayRejection.TooWide)
                rejected.Add((candidate, rejection));
        }

        CollectWindowsAboveTaskbar(taskbarHandle, Consider);
        CollectHitTestWindows(taskbarHandle, taskbarRect, orientation, primaryLength, ownProcessId, Consider);

        if (accepted.Count == 0 && rejected.Count == 0)
            return;

        var description = string.Join(
            "; ",
            accepted
                .Select(candidate => $"{DescribeWindow(candidate.Handle)} range={candidate.Range.Start}..{candidate.Range.End}")
                .Concat(rejected.Select(candidate => $"{DescribeWindow(candidate.Handle)} 被拒/{candidate.Reason}")));

        // 探测每 250 毫秒重跑一次，因此只有内容变化时才写日志：这一行是"媒体栏为什么被放在这里"的唯一现场证据，
        // 用户报障的机器（Release 构建）拿不到调试输出，所以它 MUST 进日志而不是只进调试输出。
        // The probe re-runs every 250 ms, so the line is only written when its content changes. This is the only on-site evidence for
        // "why the bar ended up here", and a reporting user runs a Release build with no debug output, so it MUST go to the log
        // rather than to debug output only.
        lock (OverlaySignatureGate)
        {
            if (LastOverlaySignatures.TryGetValue(taskbarHandle, out var previous) &&
                string.Equals(previous, description, StringComparison.Ordinal))
            {
                return;
            }

            LastOverlaySignatures[taskbarHandle] = description;
        }
        AppLogService.Current?.Info("Taskbar", $"任务栏上的外部窗口 / foreign windows over the taskbar: {description}");
    }

    /// <summary>
    /// 按 Z 序枚举任务栏**之上**的顶层窗口（`EnumWindows` 从最上面开始，遇到任务栏即停止）。
    /// Enumerates the top-level windows **above** the taskbar in Z-order (`EnumWindows` starts at the top and stops at the taskbar).
    /// </summary>
    private static void CollectWindowsAboveTaskbar(IntPtr taskbarHandle, Action<IntPtr> consider)
    {
        var reachedTaskbar = false;
        _ = EnumWindows((handle, _) =>
        {
            if (handle == taskbarHandle)
            {
                reachedTaskbar = true;
                return false;
            }

            consider(handle);
            return true;
        }, IntPtr.Zero);

        if (!reachedTaskbar)
        {
            // 任务栏不在枚举序列里（极少见）：此时不发布这一轮结果，交给下一次探测。
            // The taskbar was not in the enumeration (very rare): publish nothing this round and let the next probe try again.
            throw new InvalidOperationException("taskbar not reachable in Z-order enumeration");
        }
    }

    /// <summary>沿任务栏中线取样，把命中的顶层窗口交给同一个判定。/ Samples the taskbar's centre line and hands the hit top-level windows to the same decision.</summary>
    private static void CollectHitTestWindows(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        int ownProcessId,
        Action<IntPtr> consider)
    {
        const int SampleStepPixels = 16;
        var crossCentre = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Top + (taskbarRect.Bottom - taskbarRect.Top) / 2
            : taskbarRect.Left + (taskbarRect.Right - taskbarRect.Left) / 2;
        var primaryStart = orientation == LayoutOrientation.Horizontal ? taskbarRect.Left : taskbarRect.Top;

        for (var offset = 0; offset < primaryLength; offset += SampleStepPixels)
        {
            var primary = primaryStart + offset;
            var point = orientation == LayoutOrientation.Horizontal
                ? new POINT { X = primary, Y = crossCentre }
                : new POINT { X = crossCentre, Y = primary };
            var hit = WindowFromPoint(point);
            if (hit == IntPtr.Zero)
                continue;

            var root = GetAncestor(hit, GA_ROOT);
            if (root == IntPtr.Zero)
                root = hit;

            // 命中媒体栏自己时 MUST NOT 当作"没有外部窗口"：媒体栏压在搜索框上时，正是它挡住了搜索框。
            // A hit on the bar itself MUST NOT be read as "nothing is over the taskbar": while the bar covers the search box, the bar
            // is exactly what hides it.
            _ = GetWindowThreadProcessId(root, out var processId);
            if (root == taskbarHandle || processId == ownProcessId || IsDescendantOf(root, taskbarHandle))
                continue;

            consider(root);
        }
    }

    private static string DescribeWindow(IntPtr handle)
    {
        var className = new System.Text.StringBuilder(128);
        _ = GetClassName(handle, className, className.Capacity);
        _ = GetWindowThreadProcessId(handle, out var processId);
        GetWindowRect(handle, out var rect);
        var processName = TryGetProcessName(processId) ?? "?";
        return $"{className} process={processName} pid={processId} rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})";
    }

    private static string? TryGetProcessName(int processId)
    {
        if (processId <= 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool IsDescendantOf(IntPtr handle, IntPtr ancestor)
    {
        for (var current = GetParent(handle); current != IntPtr.Zero; current = GetParent(current))
        {
            if (current == ancestor)
                return true;
        }

        return false;
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
                // https://github.com/Fervent-Tempo/AF-Media-Bar/issues/45
                // 任务栏右键菜单会被当成MenuItem检测到并避让
                // new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var cacheRequest = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                TreeFilter = Automation.ControlViewCondition
            };
            cacheRequest.Add(AutomationElement.IsOffscreenProperty);
            cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
            cacheRequest.Add(AutomationElement.ControlTypeProperty);
            cacheRequest.Add(AutomationElement.NameProperty);
            cacheRequest.Add(AutomationElement.NativeWindowHandleProperty);

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
        return primary >= MinimumElementPrimaryPixels &&
               primary <= Math.Min(MaximumElementPrimaryPixels, primaryLength * 0.45);
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
