using System.Windows;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算灵动岛展开、收起、贴边和 DPI 恢复位置，不访问窗口、设置或显示器 API。
/// Calculates dynamic-island expanded, collapsed, docked, and DPI-restored positions without window, settings, or monitor API access.
/// </summary>
public static class DynamicIslandPositionCalculator
{
    /// <summary>
    /// 将候选左上角夹取到工作区内。
    /// Clamps a candidate top-left point to the work area.
    /// </summary>
    /// <param name="workArea">可用工作区。/ Available work area.</param>
    /// <param name="width">窗口宽度。/ Window width.</param>
    /// <param name="height">窗口高度。/ Window height.</param>
    /// <param name="candidate">候选左上角。/ Candidate top-left point.</param>
    /// <returns>工作区内的左上角。/ A top-left point inside the work area.</returns>
    public static Point ClampPosition(Rect workArea, double width, double height, Point candidate)
    {
        var maxLeft = Math.Max(workArea.Left, workArea.Right - width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - height);
        return new Point(
            Math.Clamp(candidate.X, workArea.Left, maxLeft),
            Math.Clamp(candidate.Y, workArea.Top, maxTop));
    }

    /// <summary>
    /// 根据保存位置或工作区中心计算展开位置。
    /// Calculates the expanded position from a saved position or the work-area center.
    /// </summary>
    /// <param name="workArea">可用工作区。/ Available work area.</param>
    /// <param name="width">窗口宽度。/ Window width.</param>
    /// <param name="height">窗口高度。/ Window height.</param>
    /// <param name="savedLeft">保存的左坐标。/ Saved left coordinate.</param>
    /// <param name="savedTop">保存的顶坐标。/ Saved top coordinate.</param>
    /// <returns>夹取后的展开位置。/ The clamped expanded position.</returns>
    public static Point GetExpandedPosition(
        Rect workArea,
        double width,
        double height,
        double? savedLeft,
        double? savedTop)
    {
        var defaultLeft = (workArea.Left + workArea.Right - width) / 2;
        var left = savedLeft ?? defaultLeft;
        var top = savedTop ?? workArea.Top;
        return ClampPosition(workArea, width, height, new Point(left, top));
    }

    /// <summary>
    /// 根据贴靠边缘计算收起位置，并保留指定的露出尺寸。
    /// Calculates the collapsed position for a docked edge while preserving the requested reveal size.
    /// </summary>
    /// <param name="workArea">可用工作区。/ Available work area.</param>
    /// <param name="width">窗口宽度。/ Window width.</param>
    /// <param name="height">窗口高度。/ Window height.</param>
    /// <param name="edge">贴靠边缘。/ Docked edge.</param>
    /// <param name="edgeRevealDip">露出尺寸。/ Reveal size.</param>
    /// <param name="savedLeft">保存的左坐标。/ Saved left coordinate.</param>
    /// <param name="savedTop">保存的顶坐标。/ Saved top coordinate.</param>
    /// <returns>收起后的窗口位置。/ The collapsed window position.</returns>
    public static Point GetCollapsedPosition(
        Rect workArea,
        double width,
        double height,
        DynamicIslandEdge edge,
        double edgeRevealDip,
        double? savedLeft,
        double? savedTop)
    {
        var expanded = GetExpandedPosition(workArea, width, height, savedLeft, savedTop);
        return edge switch
        {
            DynamicIslandEdge.Left => new Point(workArea.Left - width + edgeRevealDip, expanded.Y),
            DynamicIslandEdge.Right => new Point(workArea.Right - edgeRevealDip, expanded.Y),
            DynamicIslandEdge.Bottom => new Point(expanded.X, workArea.Bottom - edgeRevealDip),
            _ => new Point(expanded.X, workArea.Top - height + edgeRevealDip)
        };
    }

    /// <summary>
    /// 返回距离窗口最近且在阈值内的贴靠边缘。
    /// Returns the nearest docked edge when it is within the threshold.
    /// </summary>
    /// <param name="left">窗口左坐标。/ Window left coordinate.</param>
    /// <param name="top">窗口顶坐标。/ Window top coordinate.</param>
    /// <param name="width">窗口宽度。/ Window width.</param>
    /// <param name="height">窗口高度。/ Window height.</param>
    /// <param name="workArea">可用工作区。/ Available work area.</param>
    /// <param name="thresholdDip">贴边阈值。/ Docking threshold.</param>
    /// <returns>贴靠边缘或 null。/ The docked edge, or null.</returns>
    public static DynamicIslandEdge? FindDockedEdge(
        double left,
        double top,
        double width,
        double height,
        Rect workArea,
        double thresholdDip)
    {
        var distances = new (DynamicIslandEdge Edge, double Distance)[]
        {
            (DynamicIslandEdge.Top, Math.Abs(top - workArea.Top)),
            (DynamicIslandEdge.Right, Math.Abs(workArea.Right - (left + width))),
            (DynamicIslandEdge.Bottom, Math.Abs(workArea.Bottom - (top + height))),
            (DynamicIslandEdge.Left, Math.Abs(left - workArea.Left))
        };
        var nearest = distances.MinBy(item => item.Distance);
        return nearest.Distance <= thresholdDip ? nearest.Edge : null;
    }

    /// <summary>
    /// 根据工作区归一化中心和贴靠边缘计算 DPI 恢复位置。
    /// Calculates a DPI-restored position from a normalized work-area center and optional docked edge.
    /// </summary>
    /// <param name="workArea">新的可用工作区。/ New available work area.</param>
    /// <param name="width">窗口宽度。/ Window width.</param>
    /// <param name="height">窗口高度。/ Window height.</param>
    /// <param name="normalizedCenter">归一化中心坐标。/ Normalized center coordinate.</param>
    /// <param name="dockedEdge">可选贴靠边缘。/ Optional docked edge.</param>
    /// <returns>夹取后的恢复位置。/ The clamped restored position.</returns>
    public static Point GetDpiRestoredPosition(
        Rect workArea,
        double width,
        double height,
        Point normalizedCenter,
        DynamicIslandEdge? dockedEdge)
    {
        var center = new Point(
            Math.Clamp(normalizedCenter.X, 0, 1),
            Math.Clamp(normalizedCenter.Y, 0, 1));
        var candidate = new Point(
            workArea.Left + center.X * workArea.Width - width / 2,
            workArea.Top + center.Y * workArea.Height - height / 2);

        if (dockedEdge is { } edge)
        {
            candidate = edge switch
            {
                DynamicIslandEdge.Left => new Point(workArea.Left, candidate.Y),
                DynamicIslandEdge.Right => new Point(workArea.Right - width, candidate.Y),
                DynamicIslandEdge.Bottom => new Point(candidate.X, workArea.Bottom - height),
                _ => new Point(candidate.X, workArea.Top)
            };
        }

        return ClampPosition(workArea, width, height, candidate);
    }
}
