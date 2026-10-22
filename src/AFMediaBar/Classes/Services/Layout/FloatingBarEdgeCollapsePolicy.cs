// 贴边折叠的工作区相对 DIP 几何；不读取鼠标、操作窗口或写入设置。
using System.Windows;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>整条悬浮媒体栏的附着、收起位置与指针迟滞规则。</summary>
public static class FloatingBarEdgeCollapsePolicy
{
    public const double VisibleStripDip = 6;
    public const double EdgeToleranceDip = 10;
    public const double ActivationDistanceDip = 72;
    public const double ActivationSpanPaddingDip = 80;
    public const double ExpandedProximityDip = 64;

    public static FloatingBarDockEdge DetectEdge(Rect normal, Size workArea, bool vertical)
    {
        var left = normal.Left <= EdgeToleranceDip;
        var right = normal.Right >= workArea.Width - EdgeToleranceDip;
        var top = normal.Top <= EdgeToleranceDip;
        var bottom = normal.Bottom >= workArea.Height - EdgeToleranceDip;
        return vertical
            ? left ? FloatingBarDockEdge.Left : right ? FloatingBarDockEdge.Right : top ? FloatingBarDockEdge.Top : bottom ? FloatingBarDockEdge.Bottom : FloatingBarDockEdge.None
            : top ? FloatingBarDockEdge.Top : bottom ? FloatingBarDockEdge.Bottom : left ? FloatingBarDockEdge.Left : right ? FloatingBarDockEdge.Right : FloatingBarDockEdge.None;
    }

    public static Rect Anchor(Rect normal, Size workArea, FloatingBarDockEdge edge) => edge switch
    {
        FloatingBarDockEdge.Left => new(0, normal.Top, normal.Width, normal.Height),
        FloatingBarDockEdge.Right => new(Math.Max(0, workArea.Width - normal.Width), normal.Top, normal.Width, normal.Height),
        FloatingBarDockEdge.Top => new(normal.Left, 0, normal.Width, normal.Height),
        FloatingBarDockEdge.Bottom => new(normal.Left, Math.Max(0, workArea.Height - normal.Height), normal.Width, normal.Height),
        _ => normal
    };

    public static Rect CollapsedBounds(Rect normal, FloatingBarDockEdge edge) => edge switch
    {
        FloatingBarDockEdge.Left => new(normal.Left - Math.Max(0, normal.Width - VisibleStripDip), normal.Top, normal.Width, normal.Height),
        FloatingBarDockEdge.Right => new(normal.Right - Math.Min(normal.Width, VisibleStripDip), normal.Top, normal.Width, normal.Height),
        FloatingBarDockEdge.Top => new(normal.Left, normal.Top - Math.Max(0, normal.Height - VisibleStripDip), normal.Width, normal.Height),
        FloatingBarDockEdge.Bottom => new(normal.Left, normal.Bottom - Math.Min(normal.Height, VisibleStripDip), normal.Width, normal.Height),
        _ => normal
    };

    public static bool ShouldExpand(Rect normal, Size workArea, Point pointer, FloatingBarDockEdge edge)
    {
        if (!new Rect(workArea).Contains(pointer) || pointer.X >= workArea.Width || pointer.Y >= workArea.Height) return false;
        var horizontalSpan = pointer.X >= normal.Left - ActivationSpanPaddingDip && pointer.X <= normal.Right + ActivationSpanPaddingDip;
        var verticalSpan = pointer.Y >= normal.Top - ActivationSpanPaddingDip && pointer.Y <= normal.Bottom + ActivationSpanPaddingDip;
        return edge switch
        {
            FloatingBarDockEdge.Left => pointer.X <= ActivationDistanceDip && verticalSpan,
            FloatingBarDockEdge.Right => pointer.X >= workArea.Width - ActivationDistanceDip && verticalSpan,
            FloatingBarDockEdge.Top => pointer.Y <= ActivationDistanceDip && horizontalSpan,
            FloatingBarDockEdge.Bottom => pointer.Y >= workArea.Height - ActivationDistanceDip && horizontalSpan,
            _ => false
        };
    }

    public static bool ShouldCollapse(Rect normal, Point pointer, bool previouslyExpanded)
    {
        if (previouslyExpanded) normal.Inflate(ExpandedProximityDip, ExpandedProximityDip);
        return !normal.Contains(pointer);
    }

}
