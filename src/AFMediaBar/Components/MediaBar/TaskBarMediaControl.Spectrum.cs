using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的频谱呈现：柱状图保留合成线程 ScaleY 动画，其它样式用帧驱动跟随；峰值统一由轻量状态维护。
/// Spectrum presentation for the taskbar media control: bars keep the composition-thread ScaleY animation while the other styles
/// use a frame-driven follower; peak caps share one lightweight state.
/// </summary>
public partial class TaskBarMediaControl
{
    private SpectrumVisualSet? _spectrumVisuals;
    private readonly SpectrumBandFollower[] _spectrumFollowers = new SpectrumBandFollower[SpectrumComponentSettings.MaximumBandCount];
    private readonly SpectrumPeakTracker[] _spectrumPeakTrackers = new SpectrumPeakTracker[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _targetSpectrum = new float[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _frameLevels = new float[SpectrumComponentSettings.MaximumBandCount];
    private readonly float[] _framePeaks = new float[SpectrumComponentSettings.MaximumBandCount];
    private DispatcherTimer? _spectrumFrameTimer;
    private long _lastSpectrumFrameTimestamp;
    private bool _spectrumUnloadHooked;
    private int _appliedSpectrumBandCount = -1;
    private int _appliedSpectrumSensitivity;
    private double _appliedSpectrumContentHeight;
    private SpectrumStyle? _appliedSpectrumStyle;
    private Color? _spectrumForegroundColor;
    private Brush? _spectrumForegroundBrush;
    private SpectrumVisualTheme _spectrumTheme = SpectrumVisualThemePolicy.Resolve(false, true);

    /// <summary>逐帧跟随的间隔。 / Interval of the per-frame follower.</summary>
    private static readonly TimeSpan SpectrumFrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>帧步长上限；UI 卡顿后不能一次跳过整段动画。 / Frame-step cap so a UI stall cannot skip the whole animation at once.</summary>
    private static readonly TimeSpan MaximumFrameElapsed = TimeSpan.FromMilliseconds(50);

    /// <summary>当前柱数决定的任务栏频谱占用宽度（DIP）。 / Width the taskbar spectrum occupies for the current bar count, in DIP.</summary>
    private double SpectrumSurfaceWidth => _isVertical ? Math.Max(1, MainBorder.Width - 4) :
        SpectrumPresentationPolicy.CalculateSurfaceWidthDip(PresentationSettings.SpectrumComponent.Normalize().BandCount);

    private double ResolveSpectrumContentHeight(SpectrumComponentSettings settings)
    {
        var configured = SpectrumPresentationPolicy.ResolveContentHeightDip(settings);
        if (_isVertical)
            return Math.Min(configured, Math.Max(1, MainBorder.Width - 8));
        return IsBarPresentation
            ? Math.Min(configured, Math.Max(1, MainBorder.Height - _textBudget.PaddingDip * 2 - RestWidgetVerticalInset(TaskbarSpectrumHoverSurface)))
            : configured;
    }

    private SpectrumComponentSettings ResolveSpectrumPresentationSettings()
    {
        var settings = PresentationSettings.SpectrumComponent.Normalize();
        return _isVertical ? settings with
        {
            BandCount = Math.Min(16, settings.BandCount)
        } : settings;
    }

    /// <summary>
    /// 让频谱与媒体文字共用同一支自动前景。高对比度下使用系统画刷，且不额外压低主体透明度。
    /// Gives the spectrum the same automatic foreground as the media text. High contrast uses the system brush and does not
    /// additionally dim the main shapes.
    /// </summary>
    internal void ApplySpectrumForeground(Brush foreground, TimeSpan colorDuration = default)
    {
        Brush brush;
        Color? color = null;
        if (!SystemParameters.HighContrast && foreground is SolidColorBrush { Color: var resolved })
        {
            color = PlayerForegroundPolicy.ToSpectrumForeground(resolved);
            brush = color == _spectrumForegroundColor
                ? _spectrumForegroundBrush ?? new SolidColorBrush(color.Value)
                : TransitionColor("SpectrumForeground", new SolidColorBrush(color.Value), colorDuration);
        }
        else
        {
            brush = foreground;
        }

        var theme = SpectrumVisualThemePolicy.Resolve(SystemParameters.HighContrast, CurrentMotion.UseDecorativeEffects);
        if (color == _spectrumForegroundColor && theme == _spectrumTheme &&
            (color is not null || ReferenceEquals(brush, _spectrumForegroundBrush)))
            return;

        _spectrumForegroundColor = color;
        _spectrumForegroundBrush = brush;
        _spectrumTheme = theme;
        _spectrumVisuals?.ApplyForeground(brush, theme);
    }

    /// <summary>频谱当前应使用的画刷；自动前景尚未解析时退回 XAML 里的默认画刷。 / Brush the spectrum should use; falls back to the XAML default until an automatic foreground is resolved.</summary>
    private Brush ResolveSpectrumBrush() => _spectrumForegroundBrush ?? (Brush)FindResource("TaskbarSpectrumBrush");

    /// <summary>
    /// 按当前设置重建频谱视觉树。柱数、样式、灵敏度或内容高度相同则直接返回。
    /// Rebuilds the spectrum visual tree for the current settings. It returns immediately while the bar count, style, sensitivity,
    /// and content height are unchanged.
    /// </summary>
    internal void ConfigureSpectrum()
    {
        EnsureSpectrumUnloadHook();
        var settings = ResolveSpectrumPresentationSettings();
        var angle = _isVertical ? (_verticalTaskbarOnLeft ? 90d : -90d) : 0;
        if (TaskbarSpectrum.LayoutTransform is not RotateTransform rotation || rotation.Angle != angle)
            TaskbarSpectrum.LayoutTransform = new RotateTransform(angle);
        var contentHeight = ResolveSpectrumContentHeight(settings);
        var theme = SpectrumVisualThemePolicy.Resolve(SystemParameters.HighContrast, CurrentMotion.UseDecorativeEffects);
        var sameShape = _appliedSpectrumStyle == settings.Style &&
                        _appliedSpectrumBandCount == settings.BandCount &&
                        _appliedSpectrumSensitivity == settings.SensitivityPercent &&
                        Math.Abs(_appliedSpectrumContentHeight - contentHeight) < 0.01;
        if (sameShape && theme == _spectrumTheme)
        {
            return;
        }

        if (sameShape)
        {
            _spectrumTheme = theme;
            _spectrumVisuals?.ApplyForeground(ResolveSpectrumBrush(), theme);
            return;
        }

        _spectrumTheme = theme;
        _appliedSpectrumStyle = settings.Style;
        _appliedSpectrumBandCount = settings.BandCount;
        _appliedSpectrumSensitivity = settings.SensitivityPercent;
        _appliedSpectrumContentHeight = contentHeight;
        StopSpectrumFrameTimer();
        Array.Clear(_targetSpectrum);
        Array.Clear(_frameLevels);
        Array.Clear(_framePeaks);
        Array.Clear(_spectrumFollowers);
        Array.Clear(_spectrumPeakTrackers);
        _spectrumVisuals?.Clear();
        TaskbarSpectrum.Children.Clear();
        TaskbarSpectrum.Width = SpectrumPresentationPolicy.CalculateContentWidthDip(settings.BandCount);
        TaskbarSpectrum.Height = contentHeight;
        _spectrumVisuals = new SpectrumVisualSet(
            TaskbarSpectrum,
            settings.Style,
            settings.BandCount,
            contentHeight,
            ResolveSpectrumBrush(),
            _spectrumTheme,
            settings.SensitivityPercent,
            (Style?)FindResource("TaskbarSpectrumBar"),
            (Style?)FindResource("TaskbarSpectrumPixel"));
        _spectrumVisuals.Clear();
    }

    /// <summary>
    /// 把当前采样的频段值应用到任务栏频谱。频段数量由调用方按设置裁剪，因此这里只按现有视觉树绘制。
    /// Applies the current sampled band values to the taskbar spectrum. The caller trims the band count to the settings, so this
    /// only draws into the visuals that exist.
    /// </summary>
    /// <param name="bands">归一化频段值（0–1）；空表示静音或隐藏。/ Normalized band values, 0–1; empty means silence or hidden.</param>
    public void ApplySpectrum(ReadOnlySpan<float> bands)
    {
        ConfigureSpectrum();
        var settings = ResolveSpectrumPresentationSettings();
        if (!IsSpectrumComponentVisible || bands.Length == 0)
        {
            StopSpectrumFrameTimer();
            ClearSpectrumFollowers();
            _spectrumVisuals?.Clear();
            return;
        }
        // 空帧/隐藏必须在竖向聚合前判断，否则 AggregateSpectrum 会把空输入变成非空的零数组。
        if (_isVertical) bands = VerticalTaskbarPresentationPolicy.AggregateSpectrum(bands, settings.BandCount);

        var count = Math.Min(bands.Length, _appliedSpectrumBandCount);
        Array.Clear(_targetSpectrum);
        for (var index = 0; index < count; index++)
            _targetSpectrum[index] = float.IsFinite(bands[index]) ? Math.Clamp(bands[index], 0f, 1f) : 0f;

        var motion = CurrentMotion;
        if (!motion.UseContinuousMotion)
        {
            SnapSpectrumFollowers(count);
            DrawSpectrumFrame(count);
            StopSpectrumFrameTimer();
            return;
        }

        if (IsBarStyle(settings.Style))
        {
            _spectrumVisuals?.ApplyLevels(_targetSpectrum, animate: true, motion.FastDuration);
            for (var index = 0; index < count; index++)
                _spectrumPeakTrackers[index].Observe(_targetSpectrum[index]);
            DrawBarPeaks(count);
            if (AnyPeakUnsettled(count)) StartSpectrumFrameTimer();
            else StopSpectrumFrameTimer();
            return;
        }

        StartSpectrumFrameTimer();
        // 新样式/首帧立即画一次，避免在下一个 16 ms 帧之前停在清空状态。
        // Paint the first frame immediately so a style switch or the first sample does not sit in the cleared state until the next tick.
        AdvanceSpectrumFrame();
    }

    /// <summary>按真实经过时间推进跟随器或峰值，并重画。 / Advances followers or peaks by real elapsed time and repaints.</summary>
    private void AdvanceSpectrumFrame()
    {
        var motion = CurrentMotion;
        if (!motion.UseContinuousMotion || !IsSpectrumComponentVisible)
        {
            SnapSpectrumFollowers(_appliedSpectrumBandCount);
            DrawSpectrumFrame(_appliedSpectrumBandCount);
            StopSpectrumFrameTimer();
            return;
        }

        var timestamp = Stopwatch.GetTimestamp();
        var elapsed = _lastSpectrumFrameTimestamp == 0
            ? SpectrumFrameInterval
            : Stopwatch.GetElapsedTime(_lastSpectrumFrameTimestamp, timestamp);
        _lastSpectrumFrameTimestamp = timestamp;
        if (elapsed > MaximumFrameElapsed) elapsed = MaximumFrameElapsed;
        var count = _appliedSpectrumBandCount;

        if (IsBarStyle(_appliedSpectrumStyle ?? SpectrumStyle.Area))
        {
            for (var index = 0; index < count; index++)
                _spectrumPeakTrackers[index].Advance(_targetSpectrum[index], elapsed);
            DrawBarPeaks(count);
            if (!AnyPeakUnsettled(count)) StopSpectrumFrameTimer();
            return;
        }

        var settled = true;
        for (var index = 0; index < count; index++)
        {
            _spectrumFollowers[index].Advance(_targetSpectrum[index], elapsed, motion.FastDuration);
            _frameLevels[index] = _spectrumFollowers[index].Level;
            _framePeaks[index] = _spectrumFollowers[index].Peak;
            if (!_spectrumFollowers[index].IsSettled(_targetSpectrum[index])) settled = false;
        }

        _spectrumVisuals?.ApplyLevels(_frameLevels.AsSpan(0, count), animate: false);
        _spectrumVisuals?.ApplyPeaks(_framePeaks.AsSpan(0, count));
        if (settled) StopSpectrumFrameTimer();
    }

    private void DrawSpectrumFrame(int count)
    {
        if (IsBarStyle(_appliedSpectrumStyle ?? SpectrumStyle.Area))
        {
            _spectrumVisuals?.ApplyLevels(_targetSpectrum.AsSpan(0, count), animate: false);
            DrawBarPeaks(count);
            return;
        }

        for (var index = 0; index < count; index++)
        {
            _frameLevels[index] = _spectrumFollowers[index].Level;
            _framePeaks[index] = _spectrumFollowers[index].Peak;
        }
        _spectrumVisuals?.ApplyLevels(_frameLevels.AsSpan(0, count), animate: false);
        _spectrumVisuals?.ApplyPeaks(_framePeaks.AsSpan(0, count));
    }

    private void DrawBarPeaks(int count)
    {
        for (var index = 0; index < count; index++)
            _framePeaks[index] = _spectrumPeakTrackers[index].Peak;
        _spectrumVisuals?.ApplyPeaks(_framePeaks.AsSpan(0, count));
    }

    private bool AnyPeakUnsettled(int count)
    {
        for (var index = 0; index < count; index++)
            if (!_spectrumPeakTrackers[index].IsSettled(_targetSpectrum[index]))
                return true;
        return false;
    }

    private void SnapSpectrumFollowers(int count)
    {
        for (var index = 0; index < count; index++)
        {
            _spectrumFollowers[index].Snap(_targetSpectrum[index]);
            _spectrumPeakTrackers[index].Snap(_targetSpectrum[index]);
        }
    }

    private void ClearSpectrumFollowers()
    {
        Array.Clear(_targetSpectrum);
        Array.Clear(_frameLevels);
        Array.Clear(_framePeaks);
        Array.Clear(_spectrumFollowers);
        Array.Clear(_spectrumPeakTrackers);
    }

    private static bool IsBarStyle(SpectrumStyle style) =>
        style is SpectrumStyle.Bars or SpectrumStyle.MirroredBars;

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
    /// 停止逐帧跟随并复位时间戳。窗口卸载时必须调用：计时器持有控件引用，留着它会在窗口关闭后继续唤醒 Dispatcher。
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
}
