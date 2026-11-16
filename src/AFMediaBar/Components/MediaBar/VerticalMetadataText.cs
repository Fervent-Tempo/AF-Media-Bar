// 绘制竖向标题与歌手。每列持有绘制内容和滚动时钟，卸载时解除时钟。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using MarqueeFontKey = AFMediaBar.Components.TaskBarMediaControl.MarqueeFontKey;

namespace AFMediaBar.Components;

/// <summary>按字素混排的竖向媒体元数据。</summary>
public sealed class VerticalMetadataText : FrameworkElement
{
    internal readonly record struct Line(string Text, MarqueeFontKey Font, Brush Foreground, double Opacity = 1);

    private readonly VisualCollection _visuals;
    private readonly Column _title = new();
    private readonly Column _artist = new();
    private TaskbarMediaTextAlignment _alignment;
    private double _columnGapDip;
    private bool _useMotion;
    private bool _isHostPaused;

    public VerticalMetadataText()
    {
        _visuals = new VisualCollection(this) { _title.Visual, _artist.Visual };
        Loaded += (_, _) => ArrangeColumns();
        IsVisibleChanged += (_, _) => ArrangeColumns();
        Unloaded += (_, _) => StopAnimations();
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    internal void Configure(Line title, Line artist, double columnGapDip, TaskbarMediaTextAlignment alignment,
        bool useMotion, bool isHostPaused)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var titleChanged = _title.SetLine(title with { Font = title.Font with { PixelsPerDip = dpi } });
        var artistChanged = _artist.SetLine(artist with { Font = artist.Font with { PixelsPerDip = dpi } });
        if (!titleChanged && !artistChanged && _columnGapDip == columnGapDip && _alignment == alignment &&
            _useMotion == useMotion && _isHostPaused == isHostPaused)
            return;

        _columnGapDip = columnGapDip;
        _alignment = alignment;
        _useMotion = useMotion;
        _isHostPaused = isHostPaused;
        ArrangeColumns();
    }

    internal void StopAnimations()
    {
        _title.StopAnimation();
        _artist.StopAnimation();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _title.ApplyDpi(newDpi.PixelsPerDip);
        _artist.ApplyDpi(newDpi.PixelsPerDip);
        ArrangeColumns();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ArrangeColumns();
    }

    private void ArrangeColumns()
    {
        const double inset = VerticalTaskbarPresentationPolicy.InsetDip;
        var viewportDip = Math.Max(0, RenderSize.Height - 2 * inset);
        var gapDip = _title.WidthDip > 0 && _artist.WidthDip > 0 ? _columnGapDip : 0;
        var startDip = (RenderSize.Width - _title.WidthDip - _artist.WidthDip - gapDip) / 2;
        var canAdvance = !_isHostPaused && IsLoaded && IsVisible && RenderSize.Width > 0;
        _title.Arrange(startDip, viewportDip, _alignment, _useMotion, canAdvance);
        _artist.Arrange(startDip + _title.WidthDip + gapDip, viewportDip, _alignment, _useMotion, canAdvance);
    }

    private sealed class Column
    {
        private const double ScrollSpeedDipPerSecond = 28;
        private static readonly TimeSpan StartHold = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan EndHold = TimeSpan.FromMilliseconds(800);
        private readonly DrawingVisual _text = new();
        private readonly TranslateTransform _offset = new();
        private Line? _line;
        private double _lengthDip;
        private AnimationClock? _clock;
        private double _scrollDistanceDip;
        private bool _isAdvancing;

        internal ContainerVisual Visual { get; } = new();
        internal double WidthDip { get; private set; }

        internal Column()
        {
            _text.Transform = _offset;
            Visual.Children.Add(_text);
        }

        internal void ApplyDpi(double pixelsPerDip)
        {
            if (_line is { } line)
                SetLine(line with { Font = line.Font with { PixelsPerDip = pixelsPerDip } });
        }

        internal bool SetLine(Line line)
        {
            if (_line == line) return false;

            _line = line;
            StopAnimation();
            WidthDip = line.Text.Length == 0 ? 0 : Math.Ceiling(line.Font.Size);
            Visual.Opacity = line.Opacity;
            using var drawing = _text.RenderOpen();
            _lengthDip = 0;
            foreach (var run in VerticalTaskbarPresentationPolicy.TextRuns(line.Text))
            {
                var text = TaskBarMediaControl.FormatMetadataText(run.Text, line.Font, line.Foreground);
                var widthDip = text.WidthIncludingTrailingWhitespace;
                if (run.IsSideways)
                {
                    var leadingDip = Math.Max(0, -text.OverhangLeading);
                    drawing.PushTransform(new MatrixTransform(0, 1, -1, 0,
                        (WidthDip + text.Height) / 2, _lengthDip + leadingDip));
                    drawing.DrawText(text, new Point());
                    drawing.Pop();
                    _lengthDip += widthDip + leadingDip + Math.Max(0, -text.OverhangTrailing);
                }
                else
                {
                    drawing.DrawText(text, new Point((WidthDip - widthDip) / 2,
                        _lengthDip + (line.Font.Size - text.Height) / 2));
                    _lengthDip += line.Font.Size;
                }
            }
            return true;
        }

        internal void Arrange(double startDip, double viewportDip, TaskbarMediaTextAlignment alignment, bool useMotion, bool canAdvance)
        {
            Visual.Offset = new Vector(startDip, VerticalTaskbarPresentationPolicy.InsetDip);
            Visual.Clip = new RectangleGeometry(new Rect(0, 0, WidthDip, viewportDip));
            var availableDip = viewportDip - _lengthDip;
            if (!useMotion || viewportDip <= 0 || availableDip >= 0)
            {
                StopAnimation();
                _offset.Y = alignment switch
                {
                    TaskbarMediaTextAlignment.Right => Math.Max(0, availableDip),
                    TaskbarMediaTextAlignment.Center => Math.Max(0, availableDip / 2),
                    _ => 0
                };
                return;
            }

            if (_clock is not null && Math.Abs(_scrollDistanceDip + availableDip) > 0.01)
                StopAnimation();
            if (_clock is null)
            {
                _offset.Y = 0;
                if (canAdvance) StartAnimation(-availableDip);
                return;
            }
            if (_clock?.Controller is not { } controller || _isAdvancing == canAdvance) return;

            _isAdvancing = canAdvance;
            if (canAdvance) controller.Resume(); else controller.Pause();
        }

        private void StartAnimation(double distanceDip)
        {
            var travel = TimeSpan.FromSeconds(distanceDip / ScrollSpeedDipPerSecond);
            var end = StartHold + travel + EndHold + travel;
            var timeline = new DoubleAnimationUsingKeyFrames { Duration = end, RepeatBehavior = RepeatBehavior.Forever };
            timeline.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(StartHold)));
            timeline.KeyFrames.Add(new LinearDoubleKeyFrame(-distanceDip, KeyTime.FromTimeSpan(StartHold + travel)));
            timeline.KeyFrames.Add(new LinearDoubleKeyFrame(-distanceDip, KeyTime.FromTimeSpan(StartHold + travel + EndHold)));
            timeline.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(end)));
            timeline.Freeze();
            _scrollDistanceDip = distanceDip;
            _clock = (AnimationClock)timeline.CreateClock(true);
            _offset.ApplyAnimationClock(TranslateTransform.YProperty, _clock);
            _isAdvancing = true;
        }

        internal void StopAnimation()
        {
            if (_clock is { } clock)
            {
                clock.Controller?.Remove();
                _offset.ApplyAnimationClock(TranslateTransform.YProperty, null);
                _clock = null;
                _isAdvancing = false;
            }
            _offset.Y = 0;
        }
    }
}
