// 绘制封面边缘的只读进度；尺寸和缩放由封面宿主提供，不持有计时器或媒体资源。
using System.Windows;
using System.Windows.Media;

namespace AFMediaBar.Components;

/// <summary>从顶部中点顺时针绘制圆角矩形进度，适配横向和竖向封面。</summary>
public sealed class ArtworkProgress : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private Size _pathSize;
    private Point[] _points = [];
    private double _perimeter;
    private const double StrokeWidth = 1.5;
    private static readonly Pen _trackPen = CreateTrackPen();

    private static Pen CreateTrackPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), StrokeWidth);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (RenderSize.Width <= StrokeWidth || RenderSize.Height <= StrokeWidth)
            return;

        if (_pathSize != RenderSize || _points.Length == 0)
        {
            _pathSize = RenderSize;
            var inset = StrokeWidth / 2;
            var bounds = new Rect(inset, inset, RenderSize.Width - StrokeWidth, RenderSize.Height - StrokeWidth);
            var radius = Math.Min(4.25, Math.Min(bounds.Width, bounds.Height) / 2);
            var path = new PathFigure { StartPoint = new Point(bounds.Left + bounds.Width / 2, bounds.Top), IsClosed = true };
            void Line(double x, double y) => path.Segments.Add(new LineSegment(new Point(x, y), true));
            void Arc(double x, double y) => path.Segments.Add(new ArcSegment(new Point(x, y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
            Line(bounds.Right - radius, bounds.Top);
            Arc(bounds.Right, bounds.Top + radius);
            Line(bounds.Right, bounds.Bottom - radius);
            Arc(bounds.Right - radius, bounds.Bottom);
            Line(bounds.Left + radius, bounds.Bottom);
            Arc(bounds.Left, bounds.Bottom - radius);
            Line(bounds.Left, bounds.Top + radius);
            Arc(bounds.Left + radius, bounds.Top);
            var flattened = new PathGeometry([path]).GetFlattenedPathGeometry(0.1, ToleranceType.Absolute).Figures[0];
            _points = new[] { flattened.StartPoint }
                .Concat(flattened.Segments.SelectMany(segment => segment switch
                {
                    PolyLineSegment polyLine => polyLine.Points.AsEnumerable(),
                    LineSegment line => new[] { line.Point }.AsEnumerable(),
                    _ => Enumerable.Empty<Point>()
                }))
                .Append(flattened.StartPoint).ToArray();
            _perimeter = Enumerable.Range(1, _points.Length - 1).Sum(index => (_points[index] - _points[index - 1]).Length);
        }

        drawingContext.DrawGeometry(null, _trackPen, BuildPath(1));
        var progress = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        if (progress > 0)
            drawingContext.DrawGeometry(null, new Pen(Stroke, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, BuildPath(progress));
    }

    private StreamGeometry BuildPath(double progress)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(_points[0], false, progress >= 1);
            var remaining = _perimeter * progress;
            for (var index = 1; index < _points.Length && remaining > 0; index++)
            {
                var delta = _points[index] - _points[index - 1];
                var length = delta.Length;
                if (length <= 0) continue;
                context.LineTo(_points[index - 1] + delta * Math.Min(1, remaining / length), true, false);
                remaining -= length;
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
