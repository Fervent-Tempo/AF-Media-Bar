// 纯 WPF 指标绘制；宿主投递共享快照，本控件不创建计时器，卸载或隐藏时释放历史。
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>适用于完整面板和媒体栏的微型指标图，数值文本由宿主保留。</summary>
public sealed class MetricChart : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MetricChart), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    private readonly MetricHistory _history = new();
    private MetricVisualStyle _visualStyle;
    private double? _value;
    public bool IsPercentage { get; set; } = true;
    public MetricVisualStyle VisualStyle
    {
        get => _visualStyle;
        set
        {
            _visualStyle = value;
            Visibility = value == MetricVisualStyle.Numeric ? Visibility.Collapsed : Visibility.Visible;
            InvalidateVisual();
        }
    }

    public MetricChart()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => Reset();
    }

    public void ApplySample(double? value)
    {
        _value = value is double number && double.IsFinite(number) && number >= 0 ? number : null;
        _history.Add(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, _value);
        InvalidateVisual();
    }

    public void ShowPreview()
    {
        _history.Clear();
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        double[] values = [15, 30, 22, 58, 40, 75, 45, 62, 28, 44, 60];
        for (var index = 0; index < values.Length; index++) _history.Add(now - 50 + index * 5, values[index]);
        _value = values[^1];
        InvalidateVisual();
    }

    public void Reset()
    {
        _history.Clear();
        _value = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth < 2 || ActualHeight < 2 || VisualStyle == MetricVisualStyle.Numeric) return;
        var brush = TextElement.GetForeground(this);
        var pen = new Pen(brush, 1.2);
        var rect = new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2));
        var maximum = _history.Samples.Count == 0 ? 100 : _history.GetMaximum(IsPercentage);
        var ratio = Math.Clamp((_value ?? 0) / maximum, 0, 1);
        if (VisualStyle == MetricVisualStyle.Bar)
        {
            var rail = new Rect(rect.Left, rect.Top + rect.Height * 0.35, rect.Width, rect.Height * 0.3);
            context.DrawRoundedRectangle(null, pen, rail, 2, 2);
            if (_value.HasValue && ratio > 0) context.DrawRoundedRectangle(brush, null, new Rect(rail.TopLeft, new Size(rail.Width * ratio, rail.Height)), 2, 2);
            return;
        }
        if (VisualStyle == MetricVisualStyle.Ring)
        {
            var radius = Math.Max(0, Math.Min(rect.Width, rect.Height) / 2 - 1);
            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            context.DrawEllipse(null, new Pen(brush, 0.5), center, radius, radius);
            if (!_value.HasValue || ratio <= 0) return;
            var angle = Math.Min(ratio, 0.9999) * Math.PI * 2;
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                writer.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                writer.ArcTo(new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius),
                    new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
            }
            context.DrawGeometry(null, new Pen(brush, 2), geometry);
            return;
        }
        context.DrawRectangle(null, new Pen(brush, 0.4), rect);
        if (_history.Samples.Count == 0) return;
        var latest = _history.Samples[^1].Time;
        var segment = new List<Point>();
        void DrawSegment()
        {
            if (segment.Count == 0) return;
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                writer.BeginFigure(segment[0], VisualStyle == MetricVisualStyle.Area, VisualStyle == MetricVisualStyle.Area);
                foreach (var point in segment.Skip(1)) writer.LineTo(point, true, false);
                if (VisualStyle == MetricVisualStyle.Area)
                {
                    writer.LineTo(new Point(segment[^1].X, rect.Bottom), false, false);
                    writer.LineTo(new Point(segment[0].X, rect.Bottom), false, false);
                }
            }
            if (VisualStyle == MetricVisualStyle.Area && !SystemParameters.HighContrast)
            {
                context.PushOpacity(0.18);
                context.DrawGeometry(brush, null, geometry);
                context.Pop();
            }
            context.DrawGeometry(null, pen, geometry);
            if (segment.Count == 1) context.DrawEllipse(brush, null, segment[0], 1, 1);
            segment.Clear();
        }
        double? previousTime = null;
        foreach (var sample in _history.Samples)
        {
            if (sample.Value is not double value || previousTime is double previous && sample.Time - previous > 10)
                DrawSegment();
            if (sample.Value is double valid)
                segment.Add(new Point(rect.Right - (latest - sample.Time) / MetricHistory.WindowSeconds * rect.Width,
                    rect.Bottom - Math.Clamp(valid / maximum, 0, 1) * rect.Height));
            previousTime = sample.Time;
        }
        DrawSegment();
    }
}
