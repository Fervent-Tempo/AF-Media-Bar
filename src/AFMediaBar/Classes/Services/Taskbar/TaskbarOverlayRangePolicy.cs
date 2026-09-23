using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 判断一个"盖在任务栏上"的窗口是不是任务栏自己的一部分，并按主轴换算成占用区间。
///
/// 之所以需要这一层：Windows 11 的任务栏是一整块 XAML 岛，开始按钮、搜索框、任务按钮与托盘都是它的子窗口，UI Automation 从
/// 任务栏句柄就能枚举到全部内容；Windows 10 的左侧一簇（开始、**搜索框**、任务视图）由**别的进程的顶层窗口**叠在任务栏上绘制，
/// 它们既不是任务栏的子窗口（UIA 枚举不到），又盖在我们的媒体栏之上（抢走鼠标输入，媒体栏因此既被盖住也拖不动）。
/// 这一层给出两条判定：顶层覆盖窗口（按 Z 序扫描与命中测试找到）与任务栏自己的子窗口"槽位"（按几何找到，不依赖 UIA）。
/// Decides whether a window lying over the taskbar is part of the taskbar itself, and converts it into a primary-axis occupied range.
///
/// Why this exists: the Windows 11 taskbar is one XAML island whose children are the Start button, the search box, the task buttons
/// and the tray, so UI Automation enumerates all of it from the taskbar handle. On Windows 10 the left cluster (Start, the **search
/// box**, Task View) is drawn by top-level windows of *other processes* stacked over the taskbar: they are not descendants of the
/// taskbar (so UIA never sees them) and they paint over our media bar and swallow its mouse input, which is why the bar is both
/// covered and undraggable there. This layer offers two decisions: a covering top-level window (found by a Z-order scan and by hit
/// testing) and one of the taskbar's own child "slots" (found geometrically, without UIA).
/// </summary>
public static class TaskbarOverlayRangePolicy
{
    /// <summary>
    /// 认定"盖在任务栏上"所需的横轴重叠比例：低于它只算擦到任务栏一角，不计入占用。
    /// Cross-axis overlap ratio required to count as lying over the taskbar: below it the window merely clips a corner.
    /// </summary>
    private const double MinimumCrossOverlapRatio = 0.4;

    /// <summary>
    /// 允许的最大横轴尺寸倍数：搜索框这类任务栏元素的窗口高度与任务栏相当，而开始菜单、搜索结果与桌面窗口要高得多，
    /// 它们不是任务栏的一部分，把它们计入占用会让媒体栏为了一个浮层让位。取 6 倍是留出"搜索宿主窗口比任务栏高一些"的余量。
    /// Maximum cross-axis size as a multiple of the taskbar's: a taskbar element such as the search box has a window about as tall as
    /// the taskbar, while the Start menu, the search results, and the desktop are far taller. They are not part of the taskbar, and
    /// counting them would move the bar out of the way of a flyout. Six times leaves room for a search host window that is somewhat
    /// taller than the taskbar.
    /// </summary>
    private const double MaximumCrossSizeRatio = 6;

    /// <summary>
    /// 允许的最大主轴占比：占掉任务栏大半宽度的窗口（开始菜单、全宽浮层）不是任务栏元素；把它们计入占用会让空闲区间归零，
    /// 媒体栏反而会退回保守区间跳到最左边。
    /// Maximum share of the primary axis: a window taking most of the taskbar's width (the Start menu, a full-width flyout) is not a
    /// taskbar element, and counting it would leave no free range at all, which pushes the bar back to the conservative range at the
    /// far left.
    /// </summary>
    private const double MaximumPrimaryShare = 0.5;

    /// <summary>
    /// 试着把一个顶层覆盖窗口换算成占用区间。
    /// Tries to convert a covering top-level window into an occupied range.
    /// </summary>
    /// <param name="windowRect">候选顶层窗口的矩形（屏幕物理像素）。/ Candidate top-level window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <returns>该窗口算作任务栏元素时为 true。/ True when the window counts as part of the taskbar.</returns>
    public static bool TryResolve(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range) =>
        TryResolve(windowRect, taskbarRect, orientation, out range, out _);

    /// <summary>
    /// 试着把一个顶层覆盖窗口换算成占用区间，并给出被拒的原因（供日志记录现场）。
    /// Tries to convert a covering top-level window into an occupied range and reports the rejection reason, which the log records on site.
    /// </summary>
    /// <param name="windowRect">候选顶层窗口的矩形（屏幕物理像素）。/ Candidate top-level window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <param name="rejection">被拒原因；接受时为 <see cref="TaskbarOverlayRejection.None"/>。/ Rejection reason, <see cref="TaskbarOverlayRejection.None"/> when accepted.</param>
    /// <returns>该窗口算作任务栏元素时为 true。/ True when the window counts as part of the taskbar.</returns>
    public static bool TryResolve(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range,
        out TaskbarOverlayRejection rejection)
    {
        range = default;
        rejection = TaskbarOverlayRejection.None;

        var (primaryLength, crossLength) = ResolveLengths(taskbarRect, orientation);
        if (primaryLength <= 0 || crossLength <= 0)
            return false;

        var (windowStart, windowEnd, windowCrossStart, windowCrossEnd) = ToAxes(windowRect, orientation);
        var (taskbarStart, taskbarEnd, taskbarCrossStart, taskbarCrossEnd) = ToAxes(taskbarRect, orientation);

        var crossOverlap = Math.Min(windowCrossEnd, taskbarCrossEnd) - Math.Max(windowCrossStart, taskbarCrossStart);
        if (crossOverlap < crossLength * MinimumCrossOverlapRatio)
        {
            rejection = TaskbarOverlayRejection.NotOverTaskbar;
            return false;
        }

        var windowCrossSize = windowCrossEnd - windowCrossStart;
        if (windowCrossSize > crossLength * MaximumCrossSizeRatio)
        {
            rejection = TaskbarOverlayRejection.TooTall;
            return false;
        }

        var overlapStart = Math.Max(windowStart, taskbarStart);
        var overlapEnd = Math.Min(windowEnd, taskbarEnd);
        if (overlapEnd <= overlapStart)
        {
            rejection = TaskbarOverlayRejection.OutsideTaskbar;
            return false;
        }

        if (overlapEnd - overlapStart > primaryLength * MaximumPrimaryShare)
        {
            rejection = TaskbarOverlayRejection.TooWide;
            return false;
        }

        range = new TaskbarPrimaryRange(
            Math.Clamp(overlapStart - taskbarStart, 0, primaryLength),
            Math.Clamp(overlapEnd - taskbarStart, 0, primaryLength));
        return range.End > range.Start;
    }

    private static (int PrimaryLength, int CrossLength) ResolveLengths(RECT taskbarRect, LayoutOrientation orientation) =>
        orientation == LayoutOrientation.Horizontal
            ? (taskbarRect.Right - taskbarRect.Left, taskbarRect.Bottom - taskbarRect.Top)
            : (taskbarRect.Bottom - taskbarRect.Top, taskbarRect.Right - taskbarRect.Left);

    private static (int Start, int End, int CrossStart, int CrossEnd) ToAxes(RECT rect, LayoutOrientation orientation) =>
        orientation == LayoutOrientation.Horizontal
            ? (rect.Left, rect.Right, rect.Top, rect.Bottom)
            : (rect.Top, rect.Bottom, rect.Left, rect.Right);
}

/// <summary>候选窗口未被计入任务栏占用的原因。/ Why a candidate window was not counted as taskbar occupancy.</summary>
public enum TaskbarOverlayRejection
{
    /// <summary>被接受。/ Accepted.</summary>
    None = 0,

    /// <summary>横轴几乎不与任务栏重叠。/ Almost no cross-axis overlap with the taskbar.</summary>
    NotOverTaskbar = 1,

    /// <summary>比任务栏高太多，是浮层而不是任务栏元素。/ Far taller than the taskbar, so a flyout rather than a taskbar element.</summary>
    TooTall = 2,

    /// <summary>主轴完全不与任务栏重叠。/ No primary-axis overlap with the taskbar at all.</summary>
    OutsideTaskbar = 3,

    /// <summary>占掉任务栏大半主轴长度，是浮层而不是任务栏元素。/ Takes most of the taskbar's primary axis, so a flyout rather than a taskbar element.</summary>
    TooWide = 4,

    /// <summary>由普通应用而非 Windows Shell 拥有；截图工具等临时浮层不得触发避让。/ Owned by an ordinary app rather than Windows Shell; transient capture overlays must not trigger avoidance.</summary>
    NotShellOwned = 5
}
