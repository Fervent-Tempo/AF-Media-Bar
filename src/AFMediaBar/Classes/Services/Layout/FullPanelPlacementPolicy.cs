// 统一任务栏与悬浮媒体栏的外侧入口和面板方向；只计算几何，不读取 HWND 或设置。
using System.Windows;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>入口使用物理像素，面板使用屏幕 DIP；同一次计算内的矩形必须采用相同单位。</summary>
public static class FullPanelPlacementPolicy
{
    public const double HitLengthDip = 52;
    public const double HitThicknessDip = 16;

    /// <summary>横向媒体栏按上下半屏，竖向媒体栏按左右半屏，朝工作区中心展开。</summary>
    public static FullPanelPlacementSide ResolveSide(Rect anchor, Rect workArea, bool vertical)
    {
        if (vertical)
            return anchor.Left + anchor.Width / 2 < workArea.Left + workArea.Width / 2 ? FullPanelPlacementSide.Right : FullPanelPlacementSide.Left;
        return anchor.Top + anchor.Height / 2 < workArea.Top + workArea.Height / 2 ? FullPanelPlacementSide.Below : FullPanelPlacementSide.Above;
    }

    /// <summary>限制面板在指定侧的可用尺寸，空间不足时不跨过媒体栏。</summary>
    public static Size GetPanelLimit(Rect anchor, Rect workArea, FullPanelPlacementSide side) => new(
        Math.Max(1, Math.Min(workArea.Width, side switch
        {
            FullPanelPlacementSide.Left => anchor.Left - workArea.Left - 8,
            FullPanelPlacementSide.Right => workArea.Right - anchor.Right - 8,
            _ => workArea.Width
        })),
        Math.Max(1, Math.Min(workArea.Height, side switch
        {
            FullPanelPlacementSide.Above => anchor.Top - workArea.Top - 8,
            FullPanelPlacementSide.Below => workArea.Bottom - anchor.Bottom - 8,
            _ => workArea.Height
        })));

    /// <summary>沿指定侧放置面板，并将位置限制在所属显示器工作区。</summary>
    public static Rect PlacePanel(Rect anchor, Size panel, Rect workArea, FullPanelPlacementSide side)
    {
        var left = side switch
        {
            FullPanelPlacementSide.Left => anchor.Left - panel.Width - 8,
            FullPanelPlacementSide.Right => anchor.Right + 8,
            _ => anchor.Left + (anchor.Width - panel.Width) / 2
        };
        var top = side switch
        {
            FullPanelPlacementSide.Above => anchor.Top - panel.Height - 8,
            FullPanelPlacementSide.Below => anchor.Bottom + 8,
            _ => anchor.Top + (anchor.Height - panel.Height) / 2
        };
        return new Rect(Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - panel.Width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panel.Height)), panel.Width, panel.Height);
    }

    /// <summary>attachment 是任务栏或悬浮媒体栏的边界；扩大命中区时只向外延伸，不覆盖媒体栏。</summary>
    public static (Rect VisualBounds, Rect HitBounds, Rect TransitBounds) PlaceHandle(Rect bar, Rect attachment,
        Rect screen, Point center, FullPanelPlacementSide side, double dpi)
    {
        var scale = double.IsFinite(dpi) && dpi > 0 ? dpi : 1;
        var vertical = side is FullPanelPlacementSide.Left or FullPanelPlacementSide.Right;
        var positive = side is FullPanelPlacementSide.Below or FullPanelPlacementSide.Right;
        var thickness = Math.Max(1, Math.Round(8 * scale));
        var length = Math.Max(1, Math.Round(44 * scale));
        var visual = vertical
            ? new Rect(positive ? attachment.Right : attachment.Left - thickness, Math.Round(center.Y - length / 2), thickness, length)
            : new Rect(Math.Round(center.X - length / 2), positive ? attachment.Bottom : attachment.Top - thickness, length, thickness);
        if (vertical)
            visual.Y = Math.Clamp(visual.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - length));
        else
            visual.X = Math.Clamp(visual.X, screen.Left, Math.Max(screen.Left, screen.Right - length));
        var hitThickness = Math.Max(1, Math.Round(HitThicknessDip * scale));
        var hitLength = Math.Max(1, Math.Round(HitLengthDip * scale));
        var hit = vertical
            ? new Rect(positive ? visual.Left : visual.Right - hitThickness,
                Math.Round(visual.Top + (visual.Height - hitLength) / 2), hitThickness, hitLength)
            : new Rect(Math.Round(visual.Left + (visual.Width - hitLength) / 2),
                positive ? visual.Top : visual.Bottom - hitThickness, hitLength, hitThickness);
        hit.Intersect(screen);
        if (hit.IsEmpty) return (visual, hit, Rect.Empty);
        var transit = vertical
            ? new Rect(Math.Min(hit.Left, bar.Right), hit.Top,
                Math.Max(hit.Right, bar.Left) - Math.Min(hit.Left, bar.Right), hit.Height)
            : new Rect(hit.Left, Math.Min(hit.Top, bar.Bottom), hit.Width,
                Math.Max(hit.Bottom, bar.Top) - Math.Min(hit.Top, bar.Bottom));
        return (visual, hit, transit);
    }
}
