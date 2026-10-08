using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的频谱呈现：按柱数与样式重建视觉树，并把采样值画上去。
/// Spectrum presentation for the taskbar media control: rebuilds the visual tree for the current bar count and style, then
/// draws the sampled values into it.
/// </summary>
public partial class TaskBarMediaControl
{
    /// <summary>像素柱状图未点亮方块的相对不透明度；留一点亮度才看得出这是一列像素而不是空白。 / Relative opacity of an unlit pixel block; a little brightness is what makes the column read as pixels rather than as empty space.</summary>
    private const double UnlitPixelOpacity = 0.16;

    private readonly List<SpectrumBarState> _spectrumBars = [];
    private readonly SpectrumPoint[] _waveformOutline = new SpectrumPoint[
        SpectrumPresentationPolicy.CalculateWaveformPointCount(SpectrumComponentSettings.MaximumBandCount)];
    private readonly List<List<Border>> _spectrumPixelColumns = [];
    // 用完整限定名，避免与隐式 using 引入的 System.IO.Path 冲突。
    // Fully qualified so it cannot collide with System.IO.Path, which the implicit usings bring in.
    private System.Windows.Shapes.Path? _spectrumWaveform;
    private PolyLineSegment? _spectrumWaveformSegment;
    private int _appliedSpectrumBandCount = -1;

    /// <summary>最近一次重建视觉树时使用的频谱内容区横轴尺寸（DIP）；它变了就必须重建柱子的高度。 / Cross-axis size of the spectrum content used by the last rebuild, in DIP; a change means the bars have to be rebuilt.</summary>
    private double _appliedSpectrumContentHeight;
    private SpectrumStyle? _appliedSpectrumStyle;
    private Color? _spectrumForegroundColor;
    private Brush? _spectrumForegroundBrush;
    private readonly float[] _targetSpectrum = new float[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _displayedSpectrum = new float[SpectrumComponentSettings.MaximumBandCount];
    private DispatcherTimer? _spectrumFrameTimer;
    private long _lastSpectrumFrameTimestamp;
    private bool _spectrumUnloadHooked;

    /// <summary>
    /// 逐帧跟随的间隔。取 16 ms（约 60 Hz）而不是跟着频谱刷新率走：采样率最高 30 Hz，直接用采样率驱动会让像素与波形
    /// 每秒只动三十次，正是"卡卡的"的来源。优先级取 Render，让这一帧排在真正的渲染之前；单次推进只写几十个坐标，
    /// 不会占住 Dispatcher。
    /// Interval of the per-frame follower. It is 16 ms (about 60 Hz) rather than the spectrum refresh rate: sampling tops out at
    /// 30 Hz, and driving the visuals at that rate would move the pixel chart and the waveform only thirty times a second, which
    /// is exactly where the perceived stutter came from. Render priority places the tick just before the actual render pass, and a
    /// step writes only a few dozen coordinates, so it never occupies the dispatcher.
    /// </summary>
    private static readonly TimeSpan SpectrumFrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>当前柱数决定的任务栏频谱占用宽度（DIP）。 / Width the taskbar spectrum occupies for the current bar count, in DIP.</summary>
    private double SpectrumSurfaceWidth =>
        SpectrumPresentationPolicy.CalculateSurfaceWidthDip(SettingsManager.Current.SpectrumComponent.Normalize().BandCount);

    /// <summary>
    /// 让频谱与媒体文字共用同一支自动前景：柱状图、像素柱状图与波形都换成同一颜色，因此在浅色背景上一起转深、
    /// 在深色背景上一起转浅，强制浅色/深色与高对比度下也和文字完全一致。
    /// Gives the spectrum the same automatic foreground as the media text: bars, pixel blocks, and the waveform all take one
    /// colour, so they darken together on light backgrounds and lighten together on dark ones, and forced light/dark plus high
    /// contrast stay identical to the text as well.
    /// </summary>
    /// <param name="foreground">媒体文字正在使用的画刷。/ Brush currently used by the media text.</param>
    internal void ApplySpectrumForeground(Brush foreground)
    {
        Brush brush;
        Color? color = null;
        // 高对比度下文字用的是系统画刷，它的不透明度本身就是对比度的一部分，因此这里不再压低，直接沿用同一支画刷。
        // Under high contrast the text uses the system brush whose opacity is part of the contrast guarantee, so it is reused
        // as-is instead of being dimmed.
        if (!SystemParameters.HighContrast && foreground is SolidColorBrush { Color: var resolved })
        {
            color = PlayerForegroundPolicy.ToSpectrumForeground(resolved);
            if (color == _spectrumForegroundColor)
                return;
            brush = CreateFrozenBrush(color.Value);
        }
        else
        {
            brush = foreground;
        }

        if (color == _spectrumForegroundColor && (color is not null || ReferenceEquals(brush, _spectrumForegroundBrush)))
            return;

        _spectrumForegroundColor = color;
        _spectrumForegroundBrush = brush;
        foreach (var state in _spectrumBars)
            state.Bar.Background = brush;
        foreach (var column in _spectrumPixelColumns)
        {
            foreach (var dot in column)
                dot.Background = brush;
        }

        if (_spectrumWaveform is not null)
            _spectrumWaveform.Fill = brush;
    }

    /// <summary>频谱当前应使用的画刷；自动前景尚未解析时退回 XAML 里的默认画刷。 / Brush the spectrum should use; falls back to the XAML default until an automatic foreground is resolved.</summary>
    private Brush ResolveSpectrumBrush() => _spectrumForegroundBrush ?? (Brush)FindResource("TaskbarSpectrumBrush");

    /// <summary>
    /// 按当前设置重建频谱视觉树。柱数与样式相同则直接返回，因此每个采样周期调用它是安全的。
    /// Rebuilds the spectrum visual tree for the current settings. It returns immediately while the bar count and style are
    /// unchanged, so calling it once per sampling cycle is safe.
    /// </summary>
    internal void ConfigureSpectrum()
    {
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        var contentHeight = SpectrumPresentationPolicy.ResolveContentHeightDip(settings);
        if (_appliedSpectrumStyle == settings.Style &&
            _appliedSpectrumBandCount == settings.BandCount &&
            Math.Abs(_appliedSpectrumContentHeight - contentHeight) < 0.01)
        {
            return;
        }

        _appliedSpectrumStyle = settings.Style;
        _appliedSpectrumBandCount = settings.BandCount;
        _appliedSpectrumContentHeight = contentHeight;
        _spectrumBars.Clear();
        _spectrumPixelColumns.Clear();
        _spectrumWaveform = null;
        _spectrumWaveformSegment = null;
        // 重建意味着样式或柱数变了：已有的显示值属于旧的形状，因此计时器停下、两边都从零开始，
        // 新的形状从静止长出来，而不是先跳到旧值再跟随。
        // A rebuild means the style or the bar count changed: the existing displayed levels belong to the old shape, so the timer
        // stops and both sides restart from zero. The new shape then grows out of silence instead of jumping to the old values
        // and following from there.
        StopSpectrumFrameTimer();
        Array.Clear(_targetSpectrum, 0, _targetSpectrum.Length);
        Array.Clear(_displayedSpectrum, 0, _displayedSpectrum.Length);
        TaskbarSpectrum.Children.Clear();
        TaskbarSpectrum.Width = SpectrumPresentationPolicy.CalculateContentWidthDip(settings.BandCount);
        // 横轴尺寸由设置决定；几何只保证它装得进悬停表面（表面高度取文字区悬停块与"内容 + 两侧留白"的较大者）。
        // The cross-axis size comes from the settings; the geometry only makes sure it fits inside the hover surface, whose height is the
        // larger of the text hover block and the content plus its padding.
        TaskbarSpectrum.Height = contentHeight;

        switch (settings.Style)
        {
            case SpectrumStyle.Waveform:
                _spectrumWaveform = new System.Windows.Shapes.Path
                {
                    Fill = ResolveSpectrumBrush(),
                    Opacity = 0.95,
                    IsHitTestVisible = false
                };
                TaskbarSpectrum.Children.Add(_spectrumWaveform);
                BuildSpectrumWaveformGeometry(UpdateWaveformOutline(settings), settings);
                break;
            case SpectrumStyle.PixelBars:
                BuildSpectrumPixelColumns(settings.BandCount, contentHeight);
                break;
            default:
                BuildSpectrumBars(settings.BandCount, settings.Style, contentHeight);
                break;
        }

        // 样式或柱数变化后必须重画一次，否则新的视觉树会停在初始的最小高度上，直到下一次采样到来。
        // A style or bar-count change must be painted once here, otherwise the fresh tree sits at its initial stub height
        // until the next sample arrives.
        DrawSmoothedSpectrum(settings);
        if (!SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(settings.Style))
            ApplyBarSpectrum(ReadOnlySpan<float>.Empty, settings);
    }

    /// <summary>
    /// 把当前采样的频段值应用到任务栏静置层频谱。频段数量由调用方按设置裁剪，因此这里只按现有的视觉子元素绘制。
    /// Applies the current sampled band values to the taskbar rest-layer spectrum. The caller trims the band count to the
    /// settings, so this only draws into the visuals that exist.
    /// </summary>
    /// <param name="bands">归一化频段值（0–1）；空表示静音。/ Normalized band values, 0–1; empty means silence.</param>
    public void ApplySpectrum(ReadOnlySpan<float> bands)
    {
        // 这里再确认一次视觉树与设置一致：首次采样可能早于宿主的设置应用，没有这一步就会往空的画布上画。
        // The tree is re-checked here so it always matches the settings: the first sample can arrive before the host applies
        // its settings, and without this step it would be drawn into an empty canvas.
        ConfigureSpectrum();
        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        if (!SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(settings.Style))
        {
            // 柱状图与对称柱状图仍由 WPF 的 ScaleY 动画插值：采样只负责重设目标，帧与帧之间由合成线程补齐。
            // The bar styles keep interpolating through the WPF ScaleY animation: a sample only retargets, and the composition
            // thread fills in every frame in between.
            StopSpectrumFrameTimer();
            ApplyBarSpectrum(bands, settings);
            return;
        }

        CaptureSpectrumTargets(bands);
        if (!CurrentMotion.UseContinuousMotion)
        {
            // 动效被降级时不做时间跟随，直接把显示值落到采样值上——与柱状图在该档位下的行为一致。
            // When motion is downgraded there is no follower: displayed levels snap onto the samples, matching what the bar
            // styles already do at that tier.
            SnapSpectrumTargets();
            StopSpectrumFrameTimer();
            DrawSmoothedSpectrum(settings);
            return;
        }

        StartSpectrumFrameTimer();
    }

    /// <summary>记录本次采样的目标值；采样频率由设置决定，通常远低于屏幕刷新率。 / Records the sampled targets; the sampling rate is user-configured and usually far below the display refresh rate.</summary>
    private void CaptureSpectrumTargets(ReadOnlySpan<float> bands)
    {
        var count = Math.Min(bands.Length, _appliedSpectrumBandCount);
        Array.Clear(_targetSpectrum, 0, _targetSpectrum.Length);
        for (var index = 0; index < count; index++)
            _targetSpectrum[index] = bands[index];
    }

    private void SnapSpectrumTargets() =>
        Array.Copy(_targetSpectrum, _displayedSpectrum, _displayedSpectrum.Length);

    /// <summary>
    /// 按真实经过时间把显示值向目标推进一步并重画。跟随是帧率无关的，因此高刷屏不会让频谱跑得更快。
    /// Advances displayed levels one step towards their targets using real elapsed time, then repaints. The follower is
    /// frame-rate independent, so a high-refresh display does not make the spectrum move faster.
    /// </summary>
    private void AdvanceSpectrumFrame()
    {
        var motion = CurrentMotion;
        if (!motion.UseContinuousMotion)
        {
            SnapSpectrumTargets();
            DrawSmoothedSpectrum(SettingsManager.Current.SpectrumComponent.Normalize());
            StopSpectrumFrameTimer();
            return;
        }

        var timestamp = Stopwatch.GetTimestamp();
        // 首帧没有上一帧可比：按名义间隔推进，而不是把计时器空闲的那段时间也算进去，否则恢复播放时频谱会瞬间跳到目标。
        // The first frame has no predecessor: advance by the nominal interval instead of folding in however long the timer was
        // idle, which would otherwise make the spectrum jump straight onto its target when playback resumes.
        var elapsed = _lastSpectrumFrameTimestamp == 0
            ? SpectrumFrameInterval
            : Stopwatch.GetElapsedTime(_lastSpectrumFrameTimestamp, timestamp);
        _lastSpectrumFrameTimestamp = timestamp;

        var settled = true;
        for (var index = 0; index < _appliedSpectrumBandCount && index < _displayedSpectrum.Length; index++)
        {
            _displayedSpectrum[index] = SpectrumPresentationPolicy.AdvanceDisplayedLevel(
                _displayedSpectrum[index],
                _targetSpectrum[index],
                elapsed,
                motion.FastDuration);
            if (settled && !SpectrumPresentationPolicy.IsDisplayedLevelSettled(_displayedSpectrum[index], _targetSpectrum[index]))
                settled = false;
        }

        DrawSmoothedSpectrum(SettingsManager.Current.SpectrumComponent.Normalize());

        // 追上目标就停表：静音或稳定状态下不该留着一个每帧唤醒 Dispatcher 的计时器。
        // Stop once the follower has caught up: a silent or steady spectrum must not keep a timer waking the dispatcher every
        // frame.
        if (settled)
            StopSpectrumFrameTimer();
    }

    private void DrawSmoothedSpectrum(SpectrumComponentSettings settings)
    {
        switch (settings.Style)
        {
            case SpectrumStyle.PixelBars:
                DrawPixelSpectrum(settings);
                break;
            case SpectrumStyle.Waveform:
                DrawWaveformSpectrum(settings);
                break;
        }
    }

    private void StartSpectrumFrameTimer()
    {
        EnsureSpectrumUnloadHook();
        if (_spectrumFrameTimer is null)
        {
            _spectrumFrameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = SpectrumFrameInterval };
            _spectrumFrameTimer.Tick += (_, _) => AdvanceSpectrumFrame();
        }

        if (_spectrumFrameTimer.IsEnabled)
            return;

        _lastSpectrumFrameTimestamp = 0;
        _spectrumFrameTimer.Start();
    }

    /// <summary>
    /// 停止逐帧跟随并复位时间戳。窗口卸载时必须调用：计时器持有控件引用，留着它就会在窗口关闭后继续唤醒 Dispatcher。
    /// Stops the per-frame follower and resets its timestamp. The unloaded hook must call this: the timer holds a reference to
    /// the control and would otherwise keep waking the dispatcher after the window closes.
    /// </summary>
    private void StopSpectrumFrameTimer()
    {
        _spectrumFrameTimer?.Stop();
        _lastSpectrumFrameTimestamp = 0;
    }

    private void EnsureSpectrumUnloadHook()
    {
        if (_spectrumUnloadHooked)
            return;

        _spectrumUnloadHooked = true;
        Unloaded += (_, _) => StopSpectrumFrameTimer();
    }

    private void BuildSpectrumBars(int bandCount, SpectrumStyle style, double contentHeight)
    {
        var initialScale = SpectrumPresentationPolicy.MinimumBarHeightDip / Math.Max(1, contentHeight);
        var symmetric = SpectrumPresentationPolicy.IsSymmetric(style);
        var barStyle = (Style)FindResource("TaskbarSpectrumBar");
        var foreground = ResolveSpectrumBrush();
        for (var index = 0; index < bandCount; index++)
        {
            var scale = new ScaleTransform(1, initialScale);
            var bar = new Border
            {
                Style = barStyle,
                Background = foreground,
                Width = SpectrumPresentationPolicy.BarWidthDip,
                Height = contentHeight,
                // 对称柱状图以垂直中点为原点，因此音量升高时同时向上下延伸；贴底柱状图仍从底边向上长。
                // The symmetric style scales about the vertical centre so a louder band grows both up and down, while the
                // bottom-anchored style keeps growing from the bottom edge.
                RenderTransformOrigin = symmetric ? new Point(0.5, 0.5) : new Point(0.5, 1),
                RenderTransform = scale,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            Canvas.SetTop(bar, 0);
            TaskbarSpectrum.Children.Add(bar);
            _spectrumBars.Add(new SpectrumBarState(bar, scale));
        }
    }

    private void BuildSpectrumPixelColumns(int bandCount, double contentHeight)
    {
        var pixelStyle = (Style)FindResource("TaskbarSpectrumPixel");
        var foreground = ResolveSpectrumBrush();
        var dotCount = SpectrumPresentationPolicy.ResolvePixelDotCount(contentHeight);
        for (var index = 0; index < bandCount; index++)
        {
            var column = new List<Border>(dotCount);
            for (var dotIndex = 0; dotIndex < dotCount; dotIndex++)
            {
                var dot = new Border
                {
                    Style = pixelStyle,
                    Background = foreground,
                    Width = SpectrumPresentationPolicy.BarWidthDip,
                    Height = SpectrumPresentationPolicy.PixelDotHeightDip,
                    Opacity = UnlitPixelOpacity,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
                Canvas.SetTop(dot, SpectrumPresentationPolicy.ResolvePixelDotTopDip(dotIndex, contentHeight));
                TaskbarSpectrum.Children.Add(dot);
                column.Add(dot);
            }

            _spectrumPixelColumns.Add(column);
        }
    }

    private void ApplyBarSpectrum(ReadOnlySpan<float> bands, SpectrumComponentSettings settings)
    {
        var motion = CurrentMotion;
        for (var index = 0; index < _spectrumBars.Count; index++)
        {
            var value = index < bands.Length ? bands[index] : 0;
            var targetScale = SpectrumPresentationPolicy.ResolveBarScale(
                value,
                settings.SensitivityPercent,
                SpectrumPresentationPolicy.ResolveContentHeightDip(settings));
            ApplySpectrumBarTarget(_spectrumBars[index], targetScale, motion);
        }
    }

    /// <summary>Owns one bar's visual and submitted animation target; rebuilding the visual also resets deduplication.</summary>
    internal sealed class SpectrumBarState(Border bar, ScaleTransform scale)
    {
        internal Border Bar { get; } = bar;
        internal ScaleTransform Scale { get; } = scale;
        internal (double Scale, bool Continuous, TimeSpan Duration)? Target { get; set; }
    }

    internal static void ApplySpectrumBarTarget(SpectrumBarState state, double targetScale, MotionProfile motion)
    {
        var target = (targetScale, motion.UseContinuousMotion, motion.FastDuration);
        if (state.Target == target)
            return;

        state.Target = target;
        if (!motion.UseContinuousMotion)
        {
            state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            state.Scale.ScaleY = targetScale;
            return;
        }

        // A repeated sample leaves the current clock running to completion, rather than restarting its easing curve.
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            To = targetScale,
            Duration = motion.FastDuration,
            EasingFunction = CreateEaseOut()
        }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 按显示值点亮像素方块。等级来自已经跟随过的显示值，因此方块本身仍是整块开关（像素感保留），
    /// 但等级随时间连续移动，不再每秒跳二十次。
    /// Lights pixel blocks from the displayed levels. The level comes from the followed value, so blocks still switch as whole
    /// units and keep the pixel look, while the level itself moves continuously instead of jumping twenty times a second.
    /// </summary>
    private void DrawPixelSpectrum(SpectrumComponentSettings settings)
    {
        for (var index = 0; index < _spectrumPixelColumns.Count; index++)
        {
            var value = index < _appliedSpectrumBandCount ? _displayedSpectrum[index] : 0;
            var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(
                value,
                settings.SensitivityPercent,
                SpectrumPresentationPolicy.ResolveContentHeightDip(settings));
            var column = _spectrumPixelColumns[index];
            for (var dotIndex = 0; dotIndex < column.Count; dotIndex++)
            {
                var opacity = dotIndex < lit ? 1 : UnlitPixelOpacity;
                if (Math.Abs(column[dotIndex].Opacity - opacity) > 0.0001)
                    column[dotIndex].Opacity = opacity;
            }
        }
    }

    /// <summary>
    /// 用显示值改写波形轮廓。点是原地改写的：几何对象与点集在样式重建时一次建好，逐帧只写坐标，
    /// 因此每帧没有新的几何分配，也没有重新测量。
    /// Rewrites the waveform outline from the displayed levels. The points are updated in place: the geometry and its point set
    /// are built once when the style is rebuilt, and each frame only writes coordinates, so no geometry is allocated and nothing
    /// is re-measured per frame.
    /// </summary>
    private void DrawWaveformSpectrum(SpectrumComponentSettings settings)
    {
        if (_spectrumWaveform is null || _spectrumWaveformSegment is null)
            return;

        var outline = UpdateWaveformOutline(settings);
        if (outline.Length == 0)
        {
            _spectrumWaveform.Data = null;
            return;
        }

        var points = _spectrumWaveformSegment.Points;
        if (points.Count != outline.Length)
        {
            // 点集长度只由柱数决定；不一致说明设置与视觉树脱节，此时重建几何而不是留下半截波形。
            // The point count depends only on the bar count; a mismatch means the settings and the tree are out of step, so the
            // geometry is rebuilt rather than left as a half-drawn waveform.
            BuildSpectrumWaveformGeometry(outline, settings);
            return;
        }

        for (var index = 0; index < outline.Length; index++)
            points[index] = new Point(outline[index].X, outline[index].Y);
    }

    private ReadOnlySpan<SpectrumPoint> UpdateWaveformOutline(SpectrumComponentSettings settings)
    {
        var count = SpectrumPresentationPolicy.WriteWaveformOutline(
            _displayedSpectrum.AsSpan(0, settings.BandCount), settings.SensitivityPercent,
            SpectrumPresentationPolicy.ResolveContentHeightDip(settings), _waveformOutline);
        return _waveformOutline.AsSpan(0, count);
    }

    private void BuildSpectrumWaveformGeometry(ReadOnlySpan<SpectrumPoint> outline, SpectrumComponentSettings settings)
    {
        if (_spectrumWaveform is null || outline.Length == 0)
            return;

        var points = new PointCollection(outline.Length);
        foreach (var point in outline)
            points.Add(new Point(point.X, point.Y));

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        var segment = new PolyLineSegment { Points = points };
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _spectrumWaveformSegment = segment;
        _spectrumWaveform.Data = geometry;
    }
}
