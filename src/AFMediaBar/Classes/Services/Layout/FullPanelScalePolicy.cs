// 只计算内容缩放和独立宽高的边缘拖动，不读取窗口或设置。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>内容缩放与窗口尺寸分离；拖动固定对侧，禁止调整贴近媒体栏的一侧。</summary>
public static class FullPanelScalePolicy
{
    public const double BaseWidthDip = 440;
    public const double ShadowInsetDip = 8;
    public const double ContentWidthDip = BaseWidthDip - ShadowInsetDip * 2;
    public const double MinimumContentWidthDip = 264;
    public const double MinimumContentHeightDip = 120;
    public static double Clamp(double scale) => double.IsFinite(scale)
        ? Math.Clamp(scale, TaskbarFullPanelSettings.MinimumScalePercent / 100d, TaskbarFullPanelSettings.MaximumScalePercent / 100d) : 1;

    public static bool CanResizeEdge(string edge, FullPanelPlacementSide side) => edge is "L" or "R" or "T" or "B" or "LT" or "RT" or "LB" or "RB"
        && !edge.Contains(side switch
        {
            FullPanelPlacementSide.Above => 'B',
            FullPanelPlacementSide.Below => 'T',
            FullPanelPlacementSide.Left => 'R',
            _ => 'L'
        });

    public static Rect Resize(Rect initial, Vector delta, string edge, Size minimum, Rect workArea, FullPanelPlacementSide side)
    {
        if (!CanResizeEdge(edge, side) || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return initial;
        var limitWidth = Math.Max(1, edge.Contains('L') ? initial.Right - workArea.Left : workArea.Right - initial.Left);
        var limitHeight = Math.Max(1, edge.Contains('T') ? initial.Bottom - workArea.Top : workArea.Bottom - initial.Top);
        var width = edge.Contains('L') || edge.Contains('R')
            ? Math.Clamp(initial.Width + (edge.Contains('L') ? -delta.X : delta.X), Math.Min(minimum.Width, limitWidth), limitWidth) : initial.Width;
        var height = edge.Contains('T') || edge.Contains('B')
            ? Math.Clamp(initial.Height + (edge.Contains('T') ? -delta.Y : delta.Y), Math.Min(minimum.Height, limitHeight), limitHeight) : initial.Height;
        return PlaceResizeBounds(initial, new Size(width, height), edge);
    }

    public static Rect PlaceResizeBounds(Rect initial, Size size, string edge) => new(
        edge.Contains('L') ? initial.Right - size.Width : initial.Left,
        edge.Contains('T') ? initial.Bottom - size.Height : initial.Top, size.Width, size.Height);
}
