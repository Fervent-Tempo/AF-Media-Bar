// 为单个文字表面选择相邻留白，不合并其它组件或采入文字/按钮；几何均为物理像素。
using System.Windows;

namespace AFMediaBar.Classes.Services;

/// <summary>媒体栏前景采样的两条留白带，不操作窗口或读取屏幕。</summary>
internal static class ForegroundSampleRegionPolicy
{
    internal static IReadOnlyList<Int32Rect> Create(Int32Rect surface, Int32Rect host, bool vertical, double dpi)
    {
        var thickness = Math.Max(1, (int)Math.Ceiling(2 * dpi));
        var gap = Math.Max(1, (int)Math.Ceiling(2 * dpi));
        var start = vertical ? Math.Max(surface.Y, host.Y) : Math.Max(surface.X, host.X);
        var end = vertical ? Math.Min(surface.Y + surface.Height, host.Y + host.Height) : Math.Min(surface.X + surface.Width, host.X + host.Width);
        var hostStart = vertical ? host.X : host.Y;
        var hostEnd = hostStart + (vertical ? host.Width : host.Height);
        var surfaceStart = vertical ? surface.X : surface.Y;
        var surfaceEnd = surfaceStart + (vertical ? surface.Width : surface.Height);
        if (end <= start || hostEnd - hostStart < (gap + thickness) * 2) return [];

        // 优先取组件之外的留白；组件铺满横轴时取宿主内缘，避开居中的字形与外描边。
        var leading = surfaceStart - gap - thickness >= hostStart ? surfaceStart - gap - thickness : hostStart + gap;
        var trailing = surfaceEnd + gap + thickness <= hostEnd ? surfaceEnd + gap : hostEnd - gap - thickness;
        Int32Rect Band(int offset) => vertical
            ? new Int32Rect(offset, start, thickness, end - start)
            : new Int32Rect(start, offset, end - start, thickness);
        return leading == trailing ? [Band(leading)] : [Band(leading), Band(trailing)];
    }
}
