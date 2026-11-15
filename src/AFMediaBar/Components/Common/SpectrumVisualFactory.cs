// 真实频谱与设置预览共用形状和位置；采样、动画和状态仍由各自宿主负责。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>创建无计时器、无订阅的频谱视觉元素。</summary>
internal static class SpectrumVisualFactory
{
    internal static (Border Bar, ScaleTransform Scale) CreateBar(int index, double height, SpectrumStyle style,
        Brush foreground, double initialScale, Style? visualStyle = null)
    {
        var scale = new ScaleTransform(1, initialScale);
        var bar = new Border
        {
            Style = visualStyle, Background = foreground,
            CornerRadius = new CornerRadius(SpectrumPresentationPolicy.BarCornerRadiusDip),
            Width = SpectrumPresentationPolicy.BarWidthDip, Height = height,
            RenderTransformOrigin = SpectrumPresentationPolicy.IsSymmetric(style) ? new Point(.5, .5) : new Point(.5, 1),
            RenderTransform = scale, IsHitTestVisible = false
        };
        Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
        Canvas.SetTop(bar, 0);
        return (bar, scale);
    }

    internal static Border CreatePeakCap(Brush foreground, Style? visualStyle = null) => new()
    {
        Style = visualStyle, Background = foreground,
        Width = SpectrumPresentationPolicy.BarWidthDip,
        Height = SpectrumPresentationPolicy.PeakCapHeightDip,
        CornerRadius = new CornerRadius(SpectrumPresentationPolicy.PeakCapCornerRadiusDip),
        IsHitTestVisible = false,
        Opacity = 0,
        RenderTransform = new TranslateTransform()
    };

    internal static Ellipse CreateDot(double diameter, Brush foreground)
    {
        var dot = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = foreground,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform()
        };
        return dot;
    }

    internal static Border CreatePixel(int index, int dotIndex, double height, Brush foreground, Style? visualStyle = null)
    {
        var dot = new Border
        {
            Style = visualStyle, Background = foreground,
            CornerRadius = new CornerRadius(.5),
            Width = SpectrumPresentationPolicy.BarWidthDip,
            Height = SpectrumPresentationPolicy.PixelDotHeightDip,
            Opacity = SpectrumPresentationPolicy.DefaultUnlitPixelOpacity,
            IsHitTestVisible = false
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

    /// <summary>正常档的透明渐变遮罩；只改变同一前景色的 alpha，不引入新颜色。 / Transparent gradient mask for the normal tier; it only changes the same foreground's alpha.</summary>
    internal static LinearGradientBrush CreateAlphaMask(double topOpacity, double bottomOpacity)
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)Math.Round(Math.Clamp(topOpacity, 0, 1) * 255), 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)Math.Round(Math.Clamp(bottomOpacity, 0, 1) * 255), 0, 0, 0), 1));
        return mask;
    }

    internal static IEasingFunction CreateEaseOut() => new PowerEase { EasingMode = EasingMode.EaseOut, Power = 3 };
}
