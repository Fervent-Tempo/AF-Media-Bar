// 只计算缩放与边缘拖动，不读取窗口、显示器或全局配置。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>在固定媒体栏锚点下解释边缘拖动，内容只使用一个等比缩放系数。</summary>
public static class FullPanelScalePolicy
{
    public const double BaseWidthDip = 440;
    public const double ShadowInsetDip = 8;
    public const double ContentWidthDip = BaseWidthDip - ShadowInsetDip * 2;
    public static double Clamp(double scale) => double.IsFinite(scale)
        ? Math.Clamp(scale, TaskbarFullPanelSettings.MinimumScalePercent / 100d, TaskbarFullPanelSettings.MaximumScalePercent / 100d) : 1;

    public static double DragScale(double initial, Vector delta, Size unscaled, string edge, FullPanelPlacementSide side)
    {
        var horizontal = edge.Contains('L') || edge.Contains('R');
        var vertical = edge.Contains('T') || edge.Contains('B');
        var x = (edge.Contains('L') ? -delta.X : delta.X) / Math.Max(1, unscaled.Width);
        var y = (edge.Contains('T') ? -delta.Y : delta.Y) / Math.Max(1, unscaled.Height);
        // 贴合边的法向尺寸单侧增长，另一个方向围绕中心对称增长。
        if (side is FullPanelPlacementSide.Above or FullPanelPlacementSide.Below) x *= 2;
        else y *= 2;
        var change = horizontal && vertical ? Math.Abs(x) >= Math.Abs(y) ? x : y : horizontal ? x : y;
        return Clamp(initial + change);
    }
}
