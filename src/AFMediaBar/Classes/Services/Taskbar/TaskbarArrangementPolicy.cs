// Resolves horizontal presentation anchors without changing text, icons or stored component order.
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>横向任务栏组件和悬停裁剪的锚点策略，不拥有窗口或设置。</summary>
public static class TaskbarArrangementPolicy
{
    /// <summary>未手动覆盖时由任务栏对齐决定内容排列。</summary>
    public static TaskbarContentArrangement ResolveContent(TaskbarContentArrangement? arrangement, TaskbarBarPosition position) =>
        arrangement ?? (position == TaskbarBarPosition.End ? TaskbarContentArrangement.Right : TaskbarContentArrangement.Left);

    /// <summary>居中按钮必须完整落在文字区，固定区包含文字区内留白。</summary>
    public static double MinimumLength(double leftFixed, double rightFixed, double actionsWidth, TaskbarBarPosition alignment) =>
        actionsWidth + (alignment == TaskbarBarPosition.Center
            ? 2 * Math.Max(leftFixed, rightFixed)
            : leftFixed + rightFixed);

    /// <summary>计算按钮组在文字区中的左缘；居中以整条媒体栏为基准，受文字区边界限制。</summary>
    public static double ActionsLeft(double barWidth, double textLeft, double textWidth,
        double actionsWidth, TaskbarBarPosition position)
    {
        var minimum = Math.Min(5, Math.Max(0, textWidth));
        var maximum = Math.Max(minimum, textWidth - 6 - Math.Max(0, actionsWidth));
        var desired = position switch
        {
            TaskbarBarPosition.End => maximum,
            TaskbarBarPosition.Center => (barWidth - actionsWidth) / 2 - textLeft,
            _ => minimum
        };
        return Math.Clamp(desired, minimum, maximum);
    }

    /// <summary>按锚点计算悬停揭示矩形的左缘；收起与展开使用同一基准。</summary>
    public static double RevealLeft(double width, double revealedWidth, TaskbarBarPosition position) =>
        position switch
        {
            TaskbarBarPosition.End => Math.Max(0, width - revealedWidth),
            TaskbarBarPosition.Center => Math.Max(0, (width - revealedWidth) / 2),
            _ => 0
        };

    /// <summary>
    /// 原地放大期间把媒体栏的输入/可见区沿主轴扩到内容溢出的一侧。内容从右侧展开时封面在主轴尾端、被推挤的内容
    /// 从首端溢出，扩展改到首端（横向 <c>Left</c>、纵向 <c>Top</c>）；其余情形维持既有的尾端扩展（横向
    /// <c>Right</c>、纵向 <c>Bottom</c>）。与 <see cref="TaskBarMediaControl"/> 放大链路的推挤符号同源于
    /// <see cref="ResolveContent"/>：两侧必须同时翻，否则放大的组件仍被 HWND 裁掉、点不到。
    /// Widens the bar's input/visible region along the primary axis to the side the pushed content overflows during the
    /// in-place zoom: with right-side expansion the artwork sits at the trailing end and the pushed content overflows the
    /// leading end, so the extension moves there (<c>Left</c> horizontally, <c>Top</c> vertically); every other case keeps
    /// the established trailing-side extension (<c>Right</c> horizontally, <c>Bottom</c> vertically). It shares its source
    /// with the control's zoom-push sign through <see cref="ResolveContent"/>: both sides must flip together, or the
    /// enlarged widgets stay clipped by the HWND region and unclickable.
    /// </summary>
    /// <param name="rect">媒体栏窗口矩形（屏幕物理像素）。/ The bar window rect, in screen physical pixels.</param>
    /// <param name="extensionPx">放大期间的扩展量（物理像素）；不大于 0 时原样返回。/ The zoom extension, in physical pixels; the rect returns unchanged when it is not positive.</param>
    /// <param name="vertical">是否为纵向任务栏。/ Whether the taskbar is vertical.</param>
    /// <param name="arrangement">解析后生效的展开方向。/ The resolved expansion direction.</param>
    public static NativeMethods.RECT ExtendZoomOverflow(NativeMethods.RECT rect, int extensionPx, bool vertical, TaskbarContentArrangement arrangement)
    {
        if (extensionPx <= 0)
            return rect;
        if (arrangement == TaskbarContentArrangement.Right)
        {
            if (vertical) rect.Top -= extensionPx;
            else rect.Left -= extensionPx;
        }
        else
        {
            if (vertical) rect.Bottom += extensionPx;
            else rect.Right += extensionPx;
        }
        return rect;
    }
}
