using System.Windows;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 计算固定顶中位置，不访问窗口、设置或显示器 API。
/// Calculates fixed top-center placement without accessing windows, settings, or monitor APIs.
/// </summary>
public static class DynamicIslandPositionCalculator
{
    /// <summary>
    /// 在 DIP 工作区水平居中并限制顶边距；工作区过小时从左上角开始，保留可见部分。
    /// Centers horizontally in a DIP work area and clamps the top inset; an undersized work area keeps the top-left visible.
    /// </summary>
    public static Point GetCenteredPosition(Rect workArea, double width, double height, double topInset = 12)
    {
        if (workArea.IsEmpty)
            return new Point(0, 0);

        var left = double.IsFinite(workArea.Left) ? workArea.Left : 0;
        var top = double.IsFinite(workArea.Top) ? workArea.Top : 0;
        var horizontalSpace = Math.Max(0, NonNegative(workArea.Width) - NonNegative(width));
        var verticalSpace = Math.Max(0, NonNegative(workArea.Height) - NonNegative(height));
        var x = left + horizontalSpace / 2;
        var y = top + Math.Min(NonNegative(topInset), verticalSpace);

        return new Point(double.IsFinite(x) ? x : left, double.IsFinite(y) ? y : top);
    }

    private static double NonNegative(double value)
    {
        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}
