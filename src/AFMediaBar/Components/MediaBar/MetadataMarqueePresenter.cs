// 拥有一行媒体文字的双份绘制和动画时钟；所属控件在卸载时 Stop，旧完成回调按代际失效。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using MarqueeFontKey = AFMediaBar.Components.TaskBarMediaControl.MarqueeFontKey;

namespace AFMediaBar.Components;

internal sealed class MetadataMarqueePresenter(TextBlock element, TextBlock duplicate)
{
    internal TextBlock Element { get; } = element;
    internal TextBlock Duplicate { get; } = duplicate;
    internal TranslateTransform Transform { get; } = new();
    internal bool Advancing => _clock is not null;
    internal bool IsPaused => _paused;
    internal MetadataMarqueePlan? Plan => _plan;
    internal AnimationClock? Clock => _clock;
    private AnimationClock? _clock;
    private EventHandler? _completed;
    private MetadataMarqueePlan? _plan;
    private string? _content;
    private MarqueeFontKey? _font;
    private double _naturalWidth;
    private double _leadingInset;
    private int _generation;
    private bool _paused;

    internal bool Configure(bool enabled, double availableWidth, bool paused, TextAlignment alignment,
        double? pixelsPerDip = null)
    {
        Element.Dispatcher.VerifyAccess();
        var available = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        var content = Element.Text ?? string.Empty;
        var font = MarqueeFontKey.Capture(Element);
        if (pixelsPerDip is { } scale) font = font with { PixelsPerDip = scale };
        var contentChanged = _content != content;
        var fontChanged = _font is not { } old || old != (font with { PixelsPerDip = old.PixelsPerDip });
        var measureChanged = contentChanged || _font != font;
        if (measureChanged)
            (_naturalWidth, _leadingInset) = TaskBarMediaControl.MeasureMetadataText(content, font);
        _content = content; _font = font;
        if (!enabled || available <= 0 || !double.IsFinite(_naturalWidth) || _naturalWidth - available <= 1)
        {
            Stop();
            Element.Padding = new Thickness(0);
            Element.Width = available;
            Element.TextTrimming = TextTrimming.CharacterEllipsis;
            Element.TextAlignment = alignment;
            return false;
        }

        Element.RenderTransform = Transform;
        Duplicate.RenderTransform = Transform;
        Element.TextTrimming = TextTrimming.None;
        Element.TextAlignment = Duplicate.TextAlignment = TextAlignment.Left;
        // 多留少量字形边缘空间；两份文字与布局测量始终保持原文。
        var width = _naturalWidth + 4;
        Element.Padding = Duplicate.Padding = new Thickness(_leadingInset + 2, 0, 2, 0);
        Element.Width = Duplicate.Width = width;
        Duplicate.Text = content;
        Duplicate.Visibility = Visibility.Visible;
        Canvas.SetLeft(Duplicate, width + Math.Min(24, available * 0.1));
        if (_clock is null || contentChanged || fontChanged || _plan is null ||
            Math.Abs(_plan.TextWidth - width) > 0.01 || Math.Abs(_plan.ViewportWidth - available) > 0.01)
        {
            var cursor = _clock is not null && !contentChanged && !fontChanged && _plan is not null
                ? _plan.Sample(_clock.CurrentTime ?? TimeSpan.Zero) with { Offset = Math.Max(0, -Transform.X) }
                : (MetadataMarqueeCursor?)null;
            Start(MetadataMarqueePolicy.Create(width, available, cursor));
        }
        SetPaused(paused);
        return true;
    }

    private void Start(MetadataMarqueePlan plan)
    {
        RemoveClock();
        if (plan.Duration <= TimeSpan.Zero) plan = MetadataMarqueePolicy.Create(plan.TextWidth, plan.ViewportWidth);
        _plan = plan;
        var generation = _generation;
        var timeline = new DoubleAnimationUsingKeyFrames
        {
            Duration = plan.Duration, RepeatBehavior = plan.Repeat ? RepeatBehavior.Forever : new RepeatBehavior(1),
            FillBehavior = FillBehavior.HoldEnd
        };
        timeline.KeyFrames.Add(new LinearDoubleKeyFrame(-plan.InitialOffset, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var segment in plan.Segments)
            timeline.KeyFrames.Add(new LinearDoubleKeyFrame(-segment.To, KeyTime.FromTimeSpan(segment.End)));
        timeline.Freeze();
        var clock = (AnimationClock)timeline.CreateClock(true);
        _clock = clock;
        if (!plan.Repeat)
        {
            _completed = (_, _) =>
            {
                if (generation != _generation || !ReferenceEquals(_clock, clock)) return;
                var paused = _paused;
                Start(MetadataMarqueePolicy.Create(plan.TextWidth, plan.ViewportWidth));
                SetPaused(paused);
            };
            clock.Completed += _completed;
        }
        Transform.X = -plan.InitialOffset;
        Transform.ApplyAnimationClock(TranslateTransform.XProperty, clock, HandoffBehavior.SnapshotAndReplace);
        _paused = false;
    }

    private void SetPaused(bool paused)
    {
        if (_clock?.Controller is not { } controller || _paused == paused) return;
        _paused = paused;
        if (paused) controller.Pause(); else controller.Resume();
    }

    private void RemoveClock()
    {
        _generation++;
        if (_clock is { } clock)
        {
            if (_completed is not null) clock.Completed -= _completed;
            clock.Controller?.Remove();
        }
        _clock = null; _completed = null;
        Transform.ApplyAnimationClock(TranslateTransform.XProperty, null);
    }

    internal void Stop()
    {
        RemoveClock();
        _plan = null; _paused = false;
        Transform.X = 0;
        Element.Padding = new Thickness(0);
        Element.TextTrimming = TextTrimming.CharacterEllipsis;
        Duplicate.Visibility = Visibility.Collapsed;
    }
}
