using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>频谱几何空间中的一个采样点。 / One sample point in the spectrum's geometry space.</summary>
/// <param name="X">相对频谱内容区左边界的横向偏移（DIP）。/ Horizontal offset from the spectrum content's left edge, in DIP.</param>
/// <param name="Y">相对频谱内容区上边界的纵向偏移（DIP）。/ Vertical offset from the spectrum content's top edge, in DIP.</param>
public readonly record struct SpectrumPoint(double X, double Y);

/// <summary>
/// 任务栏频谱的几何与样式换算。柱宽、柱距固定，高度由设置给出，因此组件宽度完全由柱数决定——这正是柱数与尺寸相关的那条关系。
/// Geometry and style math for the taskbar spectrum. Bar width and gap are fixed while the height comes from the settings, so the component
/// width follows the bar count alone; that is exactly the size relationship the bar count is supposed to express.
/// </summary>
public static class SpectrumPresentationPolicy
{
    /// <summary>单根柱子的宽度（DIP）。 / Width of one bar in DIP.</summary>
    public const double BarWidthDip = 2;

    /// <summary>相邻柱子之间的净间距（DIP）。 / Clear gap between adjacent bars in DIP.</summary>
    public const double BarGapDip = 2;

    /// <summary>柱距（柱宽加净间距，DIP）。 / Bar pitch: bar width plus clear gap, in DIP.</summary>
    public const double BarPitchDip = BarWidthDip + BarGapDip;

    /// <summary>频谱悬停表面在内容区四周的留白（DIP）。 / Padding the spectrum hover surface adds around the content in DIP.</summary>
    public const double SurfacePaddingDip = 1;

    /// <summary>像素柱状图中单个方块的高度（DIP）。 / Height of one block in the pixel bar chart, in DIP.</summary>
    public const double PixelDotHeightDip = 2;

    /// <summary>像素柱状图中相邻方块之间的净间距（DIP）。 / Clear gap between blocks in the pixel bar chart, in DIP.</summary>
    public const double PixelDotGapDip = 1;

    /// <summary>柱状图与对称柱状图的最小柱高（DIP）；静音时只剩这一小段，用于保留槽位。 / Minimum bar height in DIP for the bar styles; only this stub remains while silent, which keeps the slot visible.</summary>
    public const double MinimumBarHeightDip = 3;

    /// <summary>波形每两个控制点之间插入的细分段数；频段很少时它决定曲线是否平滑。 / Interpolation segments per control-point gap; with few bands this is what makes the curve read as a waveform.</summary>
    public const int WaveformSegmentsPerGap = 6;

    /// <summary>设置里配置的频谱内容区横轴尺寸（DIP），也就是柱子能达到的最大高度。/ The spectrum content area's cross-axis size from the settings, in DIP, which is the tallest a bar can be.</summary>
    /// <param name="settings">频谱组件设置。/ Spectrum component settings.</param>
    public static double ResolveContentHeightDip(SpectrumComponentSettings settings) =>
        SpectrumComponentSettings.SnapContentHeightDip(settings.ContentHeightDip);

    /// <summary>波形在垂直中线上下各自允许的最大振幅（DIP）。 / Maximum amplitude of the waveform above and below its vertical centre, in DIP.</summary>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static double ResolveWaveformHalfHeightDip(double contentHeightDip) =>
        Math.Max(0, (contentHeightDip - 1) / 2);

    /// <summary>像素柱状图每列包含的方块数量。 / Number of blocks in each pixel column.</summary>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static int ResolvePixelDotCount(double contentHeightDip) =>
        Math.Max(1, (int)((contentHeightDip + PixelDotGapDip) / (PixelDotHeightDip + PixelDotGapDip)));

    /// <summary>像素柱状图一列的实际高度（DIP）。 / Actual height of one pixel column in DIP.</summary>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static double ResolvePixelColumnHeightDip(double contentHeightDip) =>
        ResolvePixelDotCount(contentHeightDip) * (PixelDotHeightDip + PixelDotGapDip) - PixelDotGapDip;

    /// <summary>频谱内容区的宽度（DIP）；柱数为一时没有柱距。 / Width of the spectrum content area in DIP; a single bar has no pitch to add.</summary>
    /// <param name="bandCount">柱数；越界时先被夹取。/ Bar count; clamped to the persisted range first.</param>
    public static double CalculateContentWidthDip(int bandCount)
    {
        var count = SpectrumBandPolicy.ClampBandCount(bandCount);
        return count * BarWidthDip + (count - 1) * BarGapDip;
    }

    /// <summary>频谱悬停表面的宽度（DIP），等于内容宽度加两侧留白。 / Width of the spectrum hover surface in DIP: content width plus the padding on both sides.</summary>
    /// <param name="bandCount">柱数；越界时先被夹取。/ Bar count; clamped to the persisted range first.</param>
    public static double CalculateSurfaceWidthDip(int bandCount) =>
        CalculateContentWidthDip(bandCount) + SurfacePaddingDip * 2;

    /// <summary>第 index 根柱子相对内容区左边界的偏移（DIP）。 / Offset of the bar at the given index from the content's left edge, in DIP.</summary>
    /// <param name="index">从零开始的柱序号。/ Zero-based bar index.</param>
    public static double ResolveBarLeftDip(int index) => Math.Max(0, index) * BarPitchDip;

    /// <summary>
    /// 把归一化音量换算成柱子的纵向缩放比例。静音时返回最小柱高对应的比例而不是零，暂停时才会留下固定的一小段。
    /// Converts a normalized level into a vertical bar scale. Silence maps to the minimum-height ratio rather than zero, so a
    /// paused spectrum keeps a visible stub instead of an empty slot.
    /// </summary>
    /// <param name="value">采样器给出的归一化音量（0–1）。/ Normalized level from the sampler, 0–1.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static double ResolveBarScale(float value, int sensitivityPercent, double contentHeightDip)
    {
        var level = Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f);
        var height = Math.Max(MinimumBarHeightDip, contentHeightDip);
        return (MinimumBarHeightDip + level * (height - MinimumBarHeightDip)) / height;
    }

    /// <summary>
    /// 把归一化音量换算成像素柱点亮的方块数量。至少点亮一个方块，理由与柱状图保留最小柱高相同。
    /// Converts a normalized level into the number of lit pixel blocks. At least one block stays lit, for the same reason the
    /// bar styles keep a minimum bar height.
    /// </summary>
    /// <param name="value">采样器给出的归一化音量（0–1）。/ Normalized level from the sampler, 0–1.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static int ResolveLitPixelCount(float value, int sensitivityPercent, double contentHeightDip)
    {
        var level = Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f);
        var dots = ResolvePixelDotCount(contentHeightDip);
        var lit = (int)Math.Round(level * dots, MidpointRounding.AwayFromZero);
        return Math.Clamp(lit, 1, dots);
    }

    /// <summary>像素柱状图中第 dotIndex 个方块（自下而上）相对内容区上边界的偏移（DIP）。 / Offset from the content's top edge of the block at the given bottom-up index, in DIP.</summary>
    /// <param name="dotIndex">自下而上、从零开始的方块序号（0 是最下面一块）。/ Zero-based bottom-up block index, 0 being the lowest block.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    public static double ResolvePixelDotTopDip(int dotIndex, double contentHeightDip)
    {
        var dots = ResolvePixelDotCount(contentHeightDip);
        var columnHeight = ResolvePixelColumnHeightDip(contentHeightDip);
        var index = Math.Clamp(dotIndex, 0, dots - 1);
        var verticalOffset = (contentHeightDip - columnHeight) / 2;
        return verticalOffset + (dots - 1 - index) * (PixelDotHeightDip + PixelDotGapDip);
    }

    /// <summary>
    /// 生成波形样式的闭合轮廓：先沿上缘自左向右，再沿下缘镜像回左，形成一个围绕垂直中线对称的带状图形。
    /// Builds the closed outline of the waveform style: first along the upper edge from left to right, then mirrored back
    /// along the lower edge, producing a ribbon symmetric about the vertical centre line.
    /// </summary>
    /// <param name="bands">采样器给出的频段值。/ Band values from the sampler.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    /// <returns>轮廓点；频段少于两个时返回空数组，调用方应隐藏该样式。/ Outline points, or an empty array when fewer than two bands are available; callers hide the style in that case.</returns>
    public static SpectrumPoint[] CreateWaveformOutline(ReadOnlySpan<float> bands, int sensitivityPercent, double contentHeightDip)
    {
        if (bands.Length < 2)
            return [];
        var outline = new SpectrumPoint[GetWaveformOutlineLength(bands.Length)];
        WriteWaveformOutline(bands, sensitivityPercent, contentHeightDip, outline);
        return outline;
    }

    /// <summary>
    /// Writes a symmetric Catmull-Rom outline into caller-owned storage, without per-frame arrays.
    /// Nonfinite levels are silence; a nonfinite or negative height produces a flat outline at zero.
    /// </summary>
    /// <param name="bands">Normalized band samples; fewer than two produce no points.</param>
    /// <param name="sensitivityPercent">Sensitivity percentage.</param>
    /// <param name="contentHeightDip">Content height in DIP.</param>
    /// <param name="destination">Reusable output buffer; its unused suffix is preserved.</param>
    /// <returns>The number of written points.</returns>
    /// <exception cref="ArgumentException">The destination is too small; nothing is written.</exception>
    public static int WriteWaveformOutline(ReadOnlySpan<float> bands, int sensitivityPercent, double contentHeightDip,
        Span<SpectrumPoint> destination)
    {
        if (bands.Length < 2)
            return 0;
        var length = GetWaveformOutlineLength(bands.Length);
        if (destination.Length < length)
            throw new ArgumentException("The waveform destination is too small.", nameof(destination));

        var count = bands.Length;
        var height = double.IsFinite(contentHeightDip) ? Math.Max(0, contentHeightDip) : 0;
        var maximumHalfHeight = ResolveWaveformHalfHeightDip(height);
        var centre = height / 2;
        var step = CalculateContentWidthDip(count) / (count - 1);
        var segments = WaveformSegmentsPerGap * (count - 1);
        for (var segment = 0; segment < count - 1; segment++)
        {
            var p0 = ResolveHalfHeight(bands[Math.Max(0, segment - 1)], sensitivityPercent, maximumHalfHeight);
            var p1 = ResolveHalfHeight(bands[segment], sensitivityPercent, maximumHalfHeight);
            var p2 = ResolveHalfHeight(bands[segment + 1], sensitivityPercent, maximumHalfHeight);
            var p3 = ResolveHalfHeight(bands[Math.Min(count - 1, segment + 2)], sensitivityPercent, maximumHalfHeight);
            for (var stepIndex = 0; stepIndex < WaveformSegmentsPerGap; stepIndex++)
            {
                var t = stepIndex / (double)WaveformSegmentsPerGap;
                var y = 0.5 * ((2 * p1) +
                               (-p0 + p2) * t +
                               (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                               (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                var index = segment * WaveformSegmentsPerGap + stepIndex;
                var x = (segment + t) * step;
                var amplitude = Math.Clamp(y, 0, maximumHalfHeight);
                destination[index] = new SpectrumPoint(x, centre - amplitude);
                destination[length - 1 - index] = new SpectrumPoint(x, centre + amplitude);
            }
        }

        var finalHeight = ResolveHalfHeight(bands[count - 1], sensitivityPercent, maximumHalfHeight);
        destination[segments] = new SpectrumPoint((count - 1) * step, centre - finalHeight);
        destination[length - 1 - segments] = new SpectrumPoint((count - 1) * step, centre + finalHeight);
        return length;
    }

    private static int GetWaveformOutlineLength(int count) => checked(2 * (WaveformSegmentsPerGap * (count - 1) + 1));

    private static double ResolveHalfHeight(float value, int sensitivityPercent, double maximumHalfHeight) =>
        (float.IsFinite(value) ? Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f) : 0f) * maximumHalfHeight;

    /// <summary>频谱样式是否以垂直中线为对称轴（波形与对称柱状图）。 / Whether a spectrum style is symmetric about the vertical centre line (waveform and mirrored bars).</summary>
    /// <param name="style">频谱样式。/ Spectrum style.</param>
    public static bool IsSymmetric(SpectrumStyle style) =>
        style is SpectrumStyle.Waveform or SpectrumStyle.MirroredBars;

    /// <summary>
    /// 时间跟随的时间常数相对于动效「快速时长」的比例。取 0.25 是为了让跟随响应对齐柱状图那条路径：
    /// 柱子的 ScaleY 用 120 ms 的 PowerEase(3, EaseOut)，每隔一个采样周期（默认 20 Hz，即 50 ms）重设目标；
    /// 指数跟随在 τ = 30 ms 时，一个采样周期后走完约 81%，缓出曲线在同一时刻约 80%，两者相差不到两个百分点，
    /// 因此四种样式的响应速度一致，不会出现"柱状图跟得上、波形慢半拍"。
    /// Time constant of the temporal follower, as a fraction of the motion profile's fast duration. 0.25 aligns the follower
    /// with the bar path: bars animate ScaleY with a 120 ms PowerEase(3, EaseOut) retargeted once per sampling cycle (20 Hz by
    /// default, so every 50 ms). With τ = 30 ms the exponential has covered about 81% after one cycle while the ease-out curve
    /// has covered about 80% — under two points apart, so all four styles respond at the same speed instead of leaving the
    /// waveform visibly half a beat behind the bars.
    /// </summary>
    public const double TemporalSmoothingTimeConstantFraction = 0.25;

    /// <summary>
    /// 显示值离目标值小于该距离时直接落到目标值。指数跟随永远只是逼近，没有这一步计时器会为了 0.0001 的差距一直空转。
    /// Distance under which a displayed level snaps onto its target. An exponential follower only ever approaches, and without
    /// this snap the frame timer would keep spinning for a remaining gap of 0.0001.
    /// </summary>
    public const float DisplayedLevelTolerance = 0.002f;

    /// <summary>
    /// 该样式是否需要按帧推进显示值。柱状图与对称柱状图把跟随交给 WPF 的 ScaleY 动画（由合成线程插值），
    /// 像素柱状图与波形的视觉来自量化取值和路径点，没有可交给动画引擎的属性，只能在时间维度自己推进。
    /// Whether a style needs its displayed levels advanced per frame. The bar styles delegate the follower to the WPF ScaleY
    /// animation, which interpolates on the composition thread, while the pixel chart and the waveform derive their visuals from
    /// quantized levels and path points, leaving nothing for the animation engine to interpolate, so time is advanced here.
    /// </summary>
    /// <param name="style">频谱样式。/ Spectrum style.</param>
    public static bool NeedsFrameDrivenSmoothing(SpectrumStyle style) =>
        style is SpectrumStyle.Waveform or SpectrumStyle.PixelBars;

    /// <summary>
    /// 把显示值向采样目标推进一个时间步。帧率无关，因此同一段真实时间里无论跑了多少帧，结果都相同。
    /// Advances one displayed level towards its sampled target. The step is frame-rate independent, so the same real elapsed
    /// time produces the same result no matter how many frames were rendered.
    /// </summary>
    /// <param name="displayed">上一帧的显示值（0–1）。/ Displayed level of the previous frame, 0–1.</param>
    /// <param name="target">采样目标值（0–1）。/ Sampled target level, 0–1.</param>
    /// <param name="elapsed">距上一帧的真实经过时间。/ Real time elapsed since the previous frame.</param>
    /// <param name="settleDuration">动效策略给出的跟随时长，通常取快速时长。/ Follow duration from the motion profile, normally its fast duration.</param>
    /// <returns>本帧应显示的取值。/ Level to display for this frame.</returns>
    public static float AdvanceDisplayedLevel(float displayed, float target, TimeSpan elapsed, TimeSpan settleDuration)
    {
        var goal = float.IsFinite(target) ? Math.Clamp(target, 0f, 1f) : 0f;
        if (!float.IsFinite(displayed))
            return goal;

        // 没有时间可用（首帧、动效关闭或时长退化）时直接落到目标，而不是停在中间值上。
        // With no usable time (first frame, motion disabled, or a degenerate duration) snap to the target instead of parking at
        // an intermediate value.
        if (elapsed <= TimeSpan.Zero || settleDuration <= TimeSpan.Zero)
            return goal;

        var timeConstant = settleDuration.TotalSeconds * TemporalSmoothingTimeConstantFraction;
        if (timeConstant <= 0)
            return goal;

        var factor = 1 - Math.Exp(-elapsed.TotalSeconds / timeConstant);
        var next = displayed + (goal - displayed) * factor;
        return IsDisplayedLevelSettled((float)next, goal) ? goal : (float)next;
    }

    /// <summary>显示值是否已经可以认为落在目标上。/ Whether a displayed level can be considered to have landed on its target.</summary>
    /// <param name="displayed">显示值。/ Displayed level.</param>
    /// <param name="target">目标值。/ Target level.</param>
    public static bool IsDisplayedLevelSettled(float displayed, float target) =>
        Math.Abs(target - displayed) <= DisplayedLevelTolerance;

    /// <summary>
    /// 波形轮廓的点数。轮廓长度只由柱数决定，因此控制端可以一次分配点集、之后逐帧原地改写，
    /// 不必每帧新建几何对象。
    /// Number of points in a waveform outline. The count depends only on the bar count, so the control can allocate the point
    /// set once and rewrite it in place every frame instead of building a new geometry each time.
    /// </summary>
    /// <param name="bandCount">柱数。/ Bar count.</param>
    public static int CalculateWaveformPointCount(int bandCount)
    {
        var count = SpectrumBandPolicy.ClampBandCount(bandCount);
        return 2 * (WaveformSegmentsPerGap * (count - 1) + 1);
    }
}
