using System.Runtime.InteropServices;
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

        var gap = Math.Max(8, (int)Math.Round(8 * Math.Max(1, dpiScale)));
        return TaskbarFreeRangeCalculator.Calculate(primaryLength, occupied, Math.Max(0, edgePaddingPixels), gap);
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
