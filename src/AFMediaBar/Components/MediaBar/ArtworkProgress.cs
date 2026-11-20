// 绘制封面进度；本控件拥有水波时钟，卸载时撤销动画并解除系统动效订阅。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Components;

/// <summary>
/// 沿封面轮廓从顶部中点顺时针报告播放进度。两种画法：
/// <see cref="ArtworkProgressStyle.Line"/> 只用一条细线描边；<see cref="ArtworkProgressStyle.Fill"/> 像注水一样
/// 从下往上把已播放的部分染上强调色，水面是两层错相位的滚动波浪。未播放部分不画底轨。
/// </summary>
public sealed class ArtworkProgress : FrameworkElement
{
    public ArtworkProgress()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateWaveAnimation();
    }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnWaveStateChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>封面本身的圆角半径（DIP），进度线沿同一条圆角走。默认 5 与横向任务栏的封面一致。</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(5d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>进度的画法：细线或注水。/ How the progress is drawn: a thin line or a rising fill.</summary>
    public static readonly DependencyProperty ProgressStyleProperty = DependencyProperty.Register(
        nameof(ProgressStyle), typeof(ArtworkProgressStyle), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(ArtworkProgressStyle.Line, FrameworkPropertyMetadataOptions.AffectsRender, OnWaveStateChanged));

    /// <summary>水波相位（弧度）：注水样式下决定水面起伏的位置。默认由本元素自己的动画推进；替身封面关掉
    /// <see cref="AnimateWave"/> 后把这个属性绑定到真环，换树那一帧两层水波才对得上。</summary>
    public static readonly DependencyProperty WavePhaseProperty = DependencyProperty.Register(
        nameof(WavePhase), typeof(double), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>是否自己驱动水波动画。默认开；镜像真环相位的替身关掉它，否则两个独立时钟会各滚各的。</summary>
    public static readonly DependencyProperty AnimateWaveProperty = DependencyProperty.Register(
        nameof(AnimateWave), typeof(bool), typeof(ArtworkProgress),
        new FrameworkPropertyMetadata(true, OnWaveStateChanged));

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

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public ArtworkProgressStyle ProgressStyle
    {
        get => (ArtworkProgressStyle)GetValue(ProgressStyleProperty);
        set => SetValue(ProgressStyleProperty, value);
    }

    public double WavePhase
    {
        get => (double)GetValue(WavePhaseProperty);
        set => SetValue(WavePhaseProperty, value);
    }

    public bool AnimateWave
    {
        get => (bool)GetValue(AnimateWaveProperty);
        set => SetValue(AnimateWaveProperty, value);
    }

    private bool _waveLoaded;
    private bool _waveRunning;

    private static void OnWaveStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ArtworkProgress)d).UpdateWaveAnimation();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _waveLoaded = true;
        SystemParameters.StaticPropertyChanged += OnMotionEnvironmentChanged;
        RenderCapability.TierChanged += OnMotionEnvironmentChanged;
        UpdateWaveAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _waveLoaded = false;
        SystemParameters.StaticPropertyChanged -= OnMotionEnvironmentChanged;
        RenderCapability.TierChanged -= OnMotionEnvironmentChanged;
        StopWaveAnimation();
    }

    private void OnMotionEnvironmentChanged(object? sender, EventArgs e) =>
        DispatcherHelper.Run(Dispatcher, UpdateWaveAnimation);

    private void UpdateWaveAnimation()
    {
        var shouldRun = AnimateWave && ProgressStyle == ArtworkProgressStyle.Fill && _waveLoaded && IsVisible &&
                        double.IsFinite(Progress) && Progress > 0 && Progress < 1 &&
                        MotionPolicy.ResolveCurrent().UseContinuousMotion;
        if (!shouldRun)
        {
            StopWaveAnimation();
            return;
        }
        if (_waveRunning)
            return;

        _waveRunning = true;
        // 从当前相位接上：真封面在放大/缩回时会先离树再入树，每次从 0 重启会让水面明显跳一下。
        var from = double.IsFinite(WavePhase) ? WavePhase % TwoPi : 0;
        var animation = new DoubleAnimation(from, from + TwoPi, new Duration(TimeSpan.FromSeconds(WavePeriodSeconds)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        // 水波 30fps 已经够顺，没必要跟着 60fps 每帧重绘。
        Timeline.SetDesiredFrameRate(animation, 30);
        BeginAnimation(WavePhaseProperty, animation);
    }

    private void StopWaveAnimation()
    {
        if (!_waveRunning)
            return;

        _waveRunning = false;
        // 暂停、挂起或换树后继续使用当前相位，避免恢复时水面跳动。
        var resting = double.IsFinite(WavePhase) ? WavePhase % TwoPi : 0;
        BeginAnimation(WavePhaseProperty, null);
        SetCurrentValue(WavePhaseProperty, resting);
    }

    private Size _pathSize;
    private double _pathRadius;
    private Point[] _points = [];
    private double _perimeter;
    private StreamGeometry? _fullGeometry;
    private StreamGeometry? _progressGeometry;
    private double _pathProgress;

    private const double StrokeWidth = 2;

    /// <summary>进度段下面垫的深色细边，比线宽多 1px（两侧各半像素），浅色封面上也能看清。</summary>
    private const double HaloWidth = StrokeWidth + 1;

    /// <summary>注水染色透明度：既标出已播放部分，又不盖住封面。</summary>
    private const double FillOpacity = 0.30;

    /// <summary>水波振幅相对封面高度的比例；40px 封面约 1.4px。</summary>
    private const double WaveAmplitudeRatio = 0.035;

    /// <summary>水波横向跨过的波数。/ Wave cycles across the width.</summary>
    private const double WaveCycles = 1.5;

    /// <summary>水波滚动一圈的时长（秒）。/ Seconds for one full wave cycle.</summary>
    private const double WavePeriodSeconds = 2.4;

    /// <summary>一个完整正弦周期（弧度）：相位归一化与动画区间的上界。</summary>
    private const double TwoPi = Math.PI * 2;

    /// <summary>水位线宽度。/ Water-line width.</summary>
    private const double WaterlineWidth = 1.2;

    private static readonly Pen _haloPen = CreateHaloPen();

    private static Pen CreateHaloPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), HaloWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var progress = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        if (progress <= 0)
            return;

        if (ProgressStyle == ArtworkProgressStyle.Fill)
        {
            RenderFill(drawingContext, progress);
            return;
        }

        if (RenderSize.Width <= StrokeWidth || RenderSize.Height <= StrokeWidth)
            return;

        if (_pathSize != RenderSize || _pathRadius != CornerRadius || _points.Length == 0)
            RebuildPath();

        if (_progressGeometry is null || _pathProgress != progress)
        {
            _pathProgress = progress;
            _progressGeometry = progress >= 1 ? _fullGeometry : BuildPath(progress);
        }

        var path = _progressGeometry!;
        drawingContext.DrawGeometry(null, _haloPen, path);
        drawingContext.DrawGeometry(null,
            new Pen(Stroke, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
    }

    /// <summary>注水：两层错相位的滚动水波，前沿带一条水位线；圆角跟封面一致。</summary>
    private void RenderFill(DrawingContext drawingContext, double progress)
    {
        var width = RenderSize.Width;
        var height = RenderSize.Height;
        var baseline = height * (1 - progress);
        var amplitude = Math.Clamp(height * WaveAmplitudeRatio, 0.8, 3);
        var back = BuildWave(width, height, baseline + 1, amplitude * 1.3, WavePhase + 1.4);
        var front = BuildWave(width, height, baseline, amplitude, WavePhase);
        var radius = Math.Max(0, CornerRadius);

        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize), radius, radius));
        drawingContext.PushOpacity(0.16);
        drawingContext.DrawGeometry(Stroke, null, back.Fill);
        drawingContext.Pop();
        drawingContext.PushOpacity(FillOpacity);
        drawingContext.DrawGeometry(Stroke, null, front.Fill);
        drawingContext.Pop();
        drawingContext.DrawGeometry(null,
            new Pen(Stroke, WaterlineWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, front.Surface);
        drawingContext.Pop();
    }

    /// <summary>沿给定水位线生成水波：闭合到封面底部的填充，以及只有上沿的描边路径。</summary>
    private static (StreamGeometry Fill, StreamGeometry Surface) BuildWave(
        double width, double height, double baseline, double amplitude, double phase)
    {
        var segments = Math.Max(8, (int)Math.Ceiling(width / 2));
        var points = new Point[segments + 1];
        for (var index = 0; index <= segments; index++)
        {
            var x = width * index / segments;
            var y = baseline + amplitude * Math.Sin(phase + TwoPi * WaveCycles * index / segments);
            points[index] = new Point(x, y);
        }

        var fill = new StreamGeometry();
        using (var context = fill.Open())
        {
            context.BeginFigure(new Point(0, height), true, true);
            context.LineTo(points[0], true, false);
            for (var index = 1; index < points.Length; index++)
                context.LineTo(points[index], true, false);
            context.LineTo(new Point(width, height), true, false);
        }
        fill.Freeze();

        var surface = new StreamGeometry();
        using (var context = surface.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (var index = 1; index < points.Length; index++)
                context.LineTo(points[index], true, false);
        }
        surface.Freeze();
        return (fill, surface);
    }

    private void RebuildPath()
    {
        _pathSize = RenderSize;
        _pathRadius = CornerRadius;
        // 线宽的一半就是唯一的内缩量：再往里缩就会露出封面、看起来像内嵌容器。
        var inset = StrokeWidth / 2;
        var bounds = new Rect(inset, inset, RenderSize.Width - StrokeWidth, RenderSize.Height - StrokeWidth);
        var radius = Math.Max(0, Math.Min(CornerRadius - inset, Math.Min(bounds.Width, bounds.Height) / 2));
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
        _fullGeometry = BuildPath(1);
        _progressGeometry = null;
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
