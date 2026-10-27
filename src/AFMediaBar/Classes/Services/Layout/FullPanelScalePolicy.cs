// 只计算窗口与内容的统一缩放及边缘拖动，不读取窗口或设置。
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>窗口与内容同步缩放；拖动固定对侧，禁止调整贴近媒体栏的一侧。</summary>
public static class FullPanelScalePolicy
{
    public const double BaseWidthDip = 440;
    public const double ShadowInsetDip = 8;
    public const double ContentWidthDip = BaseWidthDip - ShadowInsetDip * 2;
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

    /// <summary>按拖动的主方向计算统一比例；阴影留白不参与缩放。</summary>
    public static double DragScale(double initial, Vector delta, Size unscaled, string edge, Size limit)
    {
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return Clamp(initial);
        var x = (edge.Contains('L') ? -delta.X : delta.X) / Math.Max(1, unscaled.Width);
        var y = (edge.Contains('T') ? -delta.Y : delta.Y) / Math.Max(1, unscaled.Height);
        var horizontal = edge.Contains('L') || edge.Contains('R');
        var vertical = edge.Contains('T') || edge.Contains('B');
        var change = horizontal && vertical ? Math.Abs(x) >= Math.Abs(y) ? x : y : horizontal ? x : y;
        var fit = Math.Min((limit.Width - ShadowInsetDip * 2) / Math.Max(1, unscaled.Width),
            (limit.Height - ShadowInsetDip * 2) / Math.Max(1, unscaled.Height));
        return Clamp(Math.Min(initial + change, fit));
    }

    public static Size GetResizeLimit(Rect initial, Rect workArea, string edge, FullPanelPlacementSide side) => new(
        Math.Max(1, edge.Contains('L') || side == FullPanelPlacementSide.Left ? initial.Right - workArea.Left
            : edge.Contains('R') || side == FullPanelPlacementSide.Right ? workArea.Right - initial.Left
            : 2 * Math.Min(initial.Left + initial.Width / 2 - workArea.Left, workArea.Right - initial.Left - initial.Width / 2)),
        Math.Max(1, edge.Contains('T') || side == FullPanelPlacementSide.Above ? initial.Bottom - workArea.Top
            : edge.Contains('B') || side == FullPanelPlacementSide.Below ? workArea.Bottom - initial.Top
            : 2 * Math.Min(initial.Top + initial.Height / 2 - workArea.Top, workArea.Bottom - initial.Top - initial.Height / 2)));

    /// <summary>固定拖动的对侧，同时维持媒体栏贴合边；另一方向围绕中点展开。</summary>
    public static Rect PlaceResizeBounds(Rect initial, Size size, string edge, FullPanelPlacementSide side) => new(
        edge.Contains('L') || side == FullPanelPlacementSide.Left ? initial.Right - size.Width
            : edge.Contains('R') || side == FullPanelPlacementSide.Right ? initial.Left : initial.Left + (initial.Width - size.Width) / 2,
        edge.Contains('T') || side == FullPanelPlacementSide.Above ? initial.Bottom - size.Height
            : edge.Contains('B') || side == FullPanelPlacementSide.Below ? initial.Top : initial.Top + (initial.Height - size.Height) / 2,
        size.Width, size.Height);
}
