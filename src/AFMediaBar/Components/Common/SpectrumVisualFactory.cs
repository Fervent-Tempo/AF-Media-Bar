// 真实频谱与设置预览共用形状和位置；采样、动画和状态仍由各自宿主负责。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>创建无计时器、无订阅的频谱视觉元素。</summary>
internal static class SpectrumVisualFactory
{
    internal const double UnlitPixelOpacity = 0.16;

    internal static (Border Bar, ScaleTransform Scale) CreateBar(int index, double height, SpectrumStyle style,
        Brush foreground, double initialScale, Style? visualStyle = null)
    {
        var scale = new ScaleTransform(1, initialScale);
        var bar = new Border
        {
            Style = visualStyle, Background = foreground, CornerRadius = new CornerRadius(1),
            Width = SpectrumPresentationPolicy.BarWidthDip, Height = height,
            RenderTransformOrigin = SpectrumPresentationPolicy.IsSymmetric(style) ? new Point(.5, .5) : new Point(.5, 1),
            RenderTransform = scale, IsHitTestVisible = false
        };
        Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
        Canvas.SetTop(bar, 0);
        return (bar, scale);
    }

    internal static Border CreatePixel(int index, int dotIndex, double height, Brush foreground, Style? visualStyle = null)
    {
        var dot = new Border
        {
            Style = visualStyle, Background = foreground, CornerRadius = new CornerRadius(.5),
            Width = SpectrumPresentationPolicy.BarWidthDip, Height = SpectrumPresentationPolicy.PixelDotHeightDip,
            Opacity = UnlitPixelOpacity, IsHitTestVisible = false
        };
        Canvas.SetLeft(dot, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
        Canvas.SetTop(dot, SpectrumPresentationPolicy.ResolvePixelDotTopDip(dotIndex, height));
        return dot;
    }

    internal static (PathGeometry Geometry, PolyLineSegment Segment) CreateWaveform(ReadOnlySpan<SpectrumPoint> outline)
    {
        var points = new PointCollection(outline.Length);
        foreach (var point in outline) points.Add(new Point(point.X, point.Y));
        var segment = new PolyLineSegment { Points = points };
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return (geometry, segment);
    }
}
