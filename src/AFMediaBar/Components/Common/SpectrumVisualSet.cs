// 按样式构建一棵可复用的频谱视觉树；真实控件和设置预览共用同一套几何与透明度规则。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 一种频谱样式的视觉树。它不拥有计时器、采样或订阅，只负责把显示值和峰值画到给定的 Canvas 上。
/// The visual tree for one spectrum style. It owns no timer, sampling, or subscription; it only draws displayed levels and peaks
/// into the supplied canvas.
/// </summary>
internal sealed class SpectrumVisualSet
{
    private readonly Canvas _host;
    private readonly SpectrumStyle _style;
    private readonly int _bandCount;
    private readonly double _height;
    private readonly int _sensitivityPercent;
    private readonly List<BarVisual> _bars = [];
    private readonly List<PixelVisual> _pixels = [];
    private readonly List<DotVisual> _dots = [];
    private readonly float[] _lastLevels;
    private readonly float[] _lastPeaks;
    private Path? _curve;
    private PolyLineSegment? _curveSegment;
    private SpectrumPoint[] _curveOutline = [];
    private Brush _foreground = Brushes.White;
    private SpectrumVisualTheme _theme;

    internal SpectrumVisualSet(Canvas host, SpectrumStyle style, int bandCount, double height,
        Brush foreground, SpectrumVisualTheme theme, int sensitivityPercent,
        System.Windows.Style? barStyle = null, System.Windows.Style? pixelStyle = null)
    {
        _host = host;
        _style = style;
        _bandCount = Math.Max(1, bandCount);
        _height = Math.Max(0, height);
        _sensitivityPercent = sensitivityPercent;
        _theme = theme;
        _lastLevels = new float[_bandCount];
        _lastPeaks = new float[_bandCount];
        Build(barStyle, pixelStyle);
        ApplyForeground(foreground, theme);
    }

    /// <summary>把显示值画到视觉树；柱状图可选择交给 WPF 合成线程动画。 / Draws displayed levels; bars may animate on the composition thread.</summary>
    internal void ApplyLevels(ReadOnlySpan<float> levels, bool animate, TimeSpan animationDuration = default)
    {
        Array.Clear(_lastLevels);
        levels[..Math.Min(levels.Length, _bandCount)].CopyTo(_lastLevels);
        switch (_style)
        {
            case SpectrumStyle.Bars:
            case SpectrumStyle.MirroredBars:
                for (var index = 0; index < _bars.Count; index++)
                {
                    var level = ResolveLevel(levels, index);
                    var bar = _bars[index];
                    bar.Apply(SpectrumPresentationPolicy.ResolveBarScale(level, _sensitivityPercent, _height),
                        animate, animationDuration);
                    bar.Bar.Opacity = _theme.ResolveBarOpacity(level);
                }
                break;
            case SpectrumStyle.PixelBars:
                for (var index = 0; index < _pixels.Count; index++)
                {
                    var level = ResolveLevel(levels, index);
                    var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(level, _sensitivityPercent, _height);
                    var column = _pixels[index];
                    for (var dotIndex = 0; dotIndex < column.Blocks.Count; dotIndex++)
                    {
                        var isLit = dotIndex < lit;
                        column.Blocks[dotIndex].Opacity = isLit
                            ? (_theme.UseMasks ? 0.55 + 0.45 * dotIndex / Math.Max(1, column.Blocks.Count - 1) : 1)
                            : _theme.UnlitPixelOpacity;
                    }
                }
                break;
            case SpectrumStyle.Waveform:
                ApplyCurve(levels, area: false);
                break;
            case SpectrumStyle.Area:
                ApplyCurve(levels, area: true);
                break;
            case SpectrumStyle.Dots:
                for (var index = 0; index < _dots.Count; index++)
                {
                    var level = ResolveLevel(levels, index);
                    var dot = _dots[index];
                    dot.DotTransform.Y = SpectrumPresentationPolicy.ResolveDotCenterYDip(
                        level, _sensitivityPercent, _height, SpectrumPresentationPolicy.DotDiameterDip) -
                        SpectrumPresentationPolicy.DotDiameterDip / 2;
                    dot.Dot.Opacity = _theme.DotOpacity;
                }
                break;
        }
    }

    /// <summary>把峰值帽/峰值点画到视觉树；正常档以外隐藏。 / Draws peak caps/dots; hidden outside the normal tier.</summary>
    internal void ApplyPeaks(ReadOnlySpan<float> peaks)
    {
        Array.Clear(_lastPeaks);
        peaks[..Math.Min(peaks.Length, _bandCount)].CopyTo(_lastPeaks);
        var show = _theme.ShowPeakMarkers;
        switch (_style)
        {
            case SpectrumStyle.Bars:
            case SpectrumStyle.MirroredBars:
                for (var index = 0; index < _bars.Count; index++)
                {
                    var peak = ResolveLevel(peaks, index);
                    var bar = _bars[index];
                    var visible = show && peak > SpectrumPresentationPolicy.FollowerTolerance;
                    ApplyCap(bar.TopCap, bar.TopTransform, visible,
                        SpectrumPresentationPolicy.ResolvePeakCapCenterYDip(peak, _sensitivityPercent, _height,
                            SpectrumPresentationPolicy.PeakCapHeightDip, bottom: false,
                            symmetric: _style == SpectrumStyle.MirroredBars));
                    ApplyCap(bar.BottomCap, bar.BottomTransform, visible,
                        SpectrumPresentationPolicy.ResolvePeakCapCenterYDip(peak, _sensitivityPercent, _height,
                            SpectrumPresentationPolicy.PeakCapHeightDip, bottom: true,
                            symmetric: _style == SpectrumStyle.MirroredBars));
                }
                break;
            case SpectrumStyle.PixelBars:
                for (var index = 0; index < _pixels.Count; index++)
                {
                    var peak = ResolveLevel(peaks, index);
                    var column = _pixels[index];
                    var visible = show && peak > SpectrumPresentationPolicy.FollowerTolerance;
                    column.Peak.Opacity = visible ? 1 : 0;
                    if (visible)
                    {
                        var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(peak, _sensitivityPercent, _height);
                        column.PeakTransform.Y = SpectrumPresentationPolicy.ResolvePixelDotTopDip(
                            Math.Max(0, lit - 1), _height);
                    }
                }
                break;
            case SpectrumStyle.Dots:
                for (var index = 0; index < _dots.Count; index++)
                {
                    var peak = ResolveLevel(peaks, index);
                    var dot = _dots[index];
                    var visible = show && peak > SpectrumPresentationPolicy.FollowerTolerance;
                    dot.Peak.Opacity = visible ? 0.65 : 0;
                    if (visible)
                    {
                        dot.PeakTransform.Y = SpectrumPresentationPolicy.ResolveDotCenterYDip(
                            peak, _sensitivityPercent, _height, SpectrumPresentationPolicy.PeakDotDiameterDip) -
                            SpectrumPresentationPolicy.PeakDotDiameterDip / 2;
                    }
                }
                break;
        }
    }

    /// <summary>更新前景与主题；高对比度会移除遮罩并恢复实心形状。 / Updates foreground and theme; high contrast drops masks and restores solid shapes.</summary>
    internal void ApplyForeground(Brush foreground, SpectrumVisualTheme theme)
    {
        _foreground = foreground;
        _theme = theme;
        for (var index = 0; index < _bars.Count; index++)
        {
            var bar = _bars[index];
            bar.Bar.Background = foreground;
            if (bar.TopCap is not null) bar.TopCap.Background = foreground;
            if (bar.BottomCap is not null) bar.BottomCap.Background = foreground;
            bar.Bar.Opacity = theme.ResolveBarOpacity(_lastLevels[index]);
        }
        for (var index = 0; index < _pixels.Count; index++)
        {
            var column = _pixels[index];
            foreach (var block in column.Blocks) block.Background = foreground;
            column.Peak.Background = foreground;
            var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(
                ResolveLevel(_lastLevels, index), _sensitivityPercent, _height);
            for (var dotIndex = 0; dotIndex < column.Blocks.Count; dotIndex++)
            {
                column.Blocks[dotIndex].Opacity = dotIndex < lit
                    ? (theme.UseMasks ? 0.55 + 0.45 * dotIndex / Math.Max(1, column.Blocks.Count - 1) : 1)
                    : theme.UnlitPixelOpacity;
            }
        }
        foreach (var dot in _dots)
        {
            dot.Dot.Fill = foreground;
            dot.Peak.Fill = foreground;
            dot.Dot.Opacity = theme.DotOpacity;
        }
        if (_curve is not null)
        {
            _curve.Fill = foreground;
            _curve.Stroke = foreground;
            _curve.StrokeThickness = _style == SpectrumStyle.Area ? 0.8 : 0.5;
            _curve.OpacityMask = theme.UseMasks
                ? _style == SpectrumStyle.Area
                    ? SpectrumVisualFactory.CreateAlphaMask(0.85, 0.12)
                    : CreateRibbonMask()
                : null;
        }
        ApplyPeaks(_lastPeaks);
    }

    /// <summary>把视觉树恢复到静音槽位。 / Resets the visuals to their silent slots.</summary>
    internal void Clear()
    {
        Array.Clear(_lastLevels);
        Array.Clear(_lastPeaks);
        ApplyLevels(_lastLevels, animate: false);
        ApplyPeaks(_lastPeaks);
    }

    private void Build(System.Windows.Style? barStyle, System.Windows.Style? pixelStyle)
    {
        switch (_style)
        {
            case SpectrumStyle.Bars:
            case SpectrumStyle.MirroredBars:
                BuildBars(barStyle);
                break;
            case SpectrumStyle.PixelBars:
                BuildPixels(pixelStyle);
                break;
            case SpectrumStyle.Waveform:
            case SpectrumStyle.Area:
                BuildCurve();
                break;
            case SpectrumStyle.Dots:
                BuildDots();
                break;
        }
    }

    private void BuildBars(System.Windows.Style? barStyle)
    {
        var initialScale = _height <= 0 ? 0 : Math.Min(1, SpectrumPresentationPolicy.MinimumBarHeightDip / _height);
        for (var index = 0; index < _bandCount; index++)
        {
            var (bar, scale) = SpectrumVisualFactory.CreateBar(
                index, _height, _style, _foreground, initialScale, barStyle);
            _host.Children.Add(bar);
            var top = SpectrumVisualFactory.CreatePeakCap(_foreground);
            Canvas.SetLeft(top, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            _host.Children.Add(top);
            Border? bottom = null;
            if (_style == SpectrumStyle.MirroredBars)
            {
                bottom = SpectrumVisualFactory.CreatePeakCap(_foreground);
                Canvas.SetLeft(bottom, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
                _host.Children.Add(bottom);
            }
            _bars.Add(new BarVisual(bar, scale, top, (TranslateTransform)top.RenderTransform,
                bottom, bottom?.RenderTransform as TranslateTransform));
        }
    }

    private void BuildPixels(System.Windows.Style? pixelStyle)
    {
        var dotCount = SpectrumPresentationPolicy.ResolvePixelDotCount(_height);
        for (var index = 0; index < _bandCount; index++)
        {
            var blocks = new List<Border>(dotCount);
            for (var dotIndex = 0; dotIndex < dotCount; dotIndex++)
            {
                var block = SpectrumVisualFactory.CreatePixel(index, dotIndex, _height, _foreground, pixelStyle);
                _host.Children.Add(block);
                blocks.Add(block);
            }
            var peak = SpectrumVisualFactory.CreatePeakCap(_foreground);
            Canvas.SetLeft(peak, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            _host.Children.Add(peak);
            _pixels.Add(new PixelVisual(blocks, peak, (TranslateTransform)peak.RenderTransform));
        }
    }

    private void BuildDots()
    {
        for (var index = 0; index < _bandCount; index++)
        {
            var left = SpectrumPresentationPolicy.ResolveBarLeftDip(index) +
                       (SpectrumPresentationPolicy.BarWidthDip - SpectrumPresentationPolicy.DotDiameterDip) / 2;
            var dot = SpectrumVisualFactory.CreateDot(SpectrumPresentationPolicy.DotDiameterDip, _foreground);
            Canvas.SetLeft(dot, left);
            Canvas.SetTop(dot, 0);
            _host.Children.Add(dot);
            var peakLeft = SpectrumPresentationPolicy.ResolveBarLeftDip(index) +
                           (SpectrumPresentationPolicy.BarWidthDip - SpectrumPresentationPolicy.PeakDotDiameterDip) / 2;
            var peak = SpectrumVisualFactory.CreateDot(SpectrumPresentationPolicy.PeakDotDiameterDip, _foreground);
            Canvas.SetLeft(peak, peakLeft);
            Canvas.SetTop(peak, 0);
            peak.Opacity = 0;
            _host.Children.Add(peak);
            _dots.Add(new DotVisual(dot, (TranslateTransform)dot.RenderTransform,
                peak, (TranslateTransform)peak.RenderTransform));
        }
    }

    private void BuildCurve()
    {
        var zeros = new float[_bandCount];
        var outline = _style == SpectrumStyle.Area
            ? SpectrumPresentationPolicy.CreateAreaOutline(zeros, _sensitivityPercent, _height)
            : SpectrumPresentationPolicy.CreateWaveformOutline(zeros, _sensitivityPercent, _height);
        _curveOutline = new SpectrumPoint[_style == SpectrumStyle.Area
            ? SpectrumPresentationPolicy.CalculateAreaPointCount(_bandCount)
            : SpectrumPresentationPolicy.CalculateWaveformPointCount(_bandCount)];
        _curve = new Path
        {
            Fill = _foreground,
            Stroke = _foreground,
            StrokeThickness = _style == SpectrumStyle.Area ? 0.8 : 0.5,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        _host.Children.Add(_curve);
        if (outline.Length == 0)
        {
            _curve.Data = null;
            return;
        }
        var (geometry, segment) = SpectrumVisualFactory.CreateWaveform(outline);
        _curveSegment = segment;
        _curve.Data = geometry;
    }

    private void ApplyCurve(ReadOnlySpan<float> levels, bool area)
    {
        if (_curve is null || _curveSegment is null) return;
        var written = area
            ? SpectrumPresentationPolicy.WriteAreaOutline(levels, _sensitivityPercent, _height, _curveOutline)
            : SpectrumPresentationPolicy.WriteWaveformOutline(levels, _sensitivityPercent, _height, _curveOutline);
        if (written == 0)
        {
            _curve.Data = null;
            return;
        }
        if (_curveSegment.Points.Count != written)
        {
            var (geometry, segment) = SpectrumVisualFactory.CreateWaveform(_curveOutline.AsSpan(0, written));
            _curveSegment = segment;
            _curve.Data = geometry;
            return;
        }
        for (var index = 0; index < written; index++)
            _curveSegment.Points[index] = new Point(_curveOutline[index].X, _curveOutline[index].Y);
    }

    private static void ApplyCap(Border? cap, TranslateTransform? transform, bool visible, double top)
    {
        if (cap is null || transform is null) return;
        cap.Opacity = visible ? 1 : 0;
        if (visible) transform.Y = top;
    }

    private LinearGradientBrush CreateRibbonMask()
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.5));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 1));
        return mask;
    }

    private static float ResolveLevel(ReadOnlySpan<float> values, int index) =>
        index < values.Length && float.IsFinite(values[index]) ? Math.Clamp(values[index], 0f, 1f) : 0f;

    private sealed class BarVisual(
        Border bar,
        ScaleTransform scale,
        Border topCap,
        TranslateTransform topTransform,
        Border? bottomCap,
        TranslateTransform? bottomTransform)
    {
        private (double Target, bool Animate)? _last;
        internal Border Bar { get; } = bar;
        internal ScaleTransform Scale { get; } = scale;
        internal Border TopCap { get; } = topCap;
        internal TranslateTransform TopTransform { get; } = topTransform;
        internal Border? BottomCap { get; } = bottomCap;
        internal TranslateTransform? BottomTransform { get; } = bottomTransform;

        internal void Apply(double target, bool animate, TimeSpan duration)
        {
            if (_last is { } last && Math.Abs(last.Target - target) < 1e-6 && last.Animate == animate)
                return;
            _last = (target, animate);
            if (!animate || duration <= TimeSpan.Zero)
            {
                Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                Scale.ScaleY = target;
                return;
            }
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
            {
                To = target,
                Duration = duration,
                EasingFunction = SpectrumVisualFactory.CreateEaseOut()
            }, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private sealed class PixelVisual(List<Border> blocks, Border peak, TranslateTransform peakTransform)
    {
        internal List<Border> Blocks { get; } = blocks;
        internal Border Peak { get; } = peak;
        internal TranslateTransform PeakTransform { get; } = peakTransform;
    }

    private sealed class DotVisual(Ellipse dot, TranslateTransform dotTransform, Ellipse peak, TranslateTransform peakTransform)
    {
        internal Ellipse Dot { get; } = dot;
        internal TranslateTransform DotTransform { get; } = dotTransform;
        internal Ellipse Peak { get; } = peak;
        internal TranslateTransform PeakTransform { get; } = peakTransform;
    }
}
