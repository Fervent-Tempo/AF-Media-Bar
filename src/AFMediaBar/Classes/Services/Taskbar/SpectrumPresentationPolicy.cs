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
    /// <summary>单根柱子的宽度（DIP）；圆角后呈胶囊形。 / Width of one bar in DIP; the radius makes it a capsule.</summary>
    public const double BarWidthDip = 3;

    /// <summary>相邻柱子之间的净间距（DIP）；柱距仍为 4，竖向柱数计算不变。 / Clear gap between adjacent bars in DIP; the pitch stays 4, so vertical band counts do not change.</summary>
    public const double BarGapDip = 1;

    /// <summary>柱体圆角（DIP）。 / Corner radius of a bar in DIP.</summary>
    public const double BarCornerRadiusDip = BarWidthDip / 2;

    /// <summary>峰值帽高度（DIP）。 / Height of a peak cap in DIP.</summary>
    public const double PeakCapHeightDip = 1.5;

    /// <summary>峰值帽圆角（DIP）。 / Corner radius of a peak cap in DIP.</summary>
    public const double PeakCapCornerRadiusDip = PeakCapHeightDip / 2;

    /// <summary>光点直径（DIP）。 / Diameter of a level dot in DIP.</summary>
    public const double DotDiameterDip = BarWidthDip;

    /// <summary>峰值点直径（DIP）。 / Diameter of a peak dot in DIP.</summary>
    public const double PeakDotDiameterDip = 1.5;

    /// <summary>柱距（柱宽加净间距，DIP）。 / Bar pitch: bar width plus clear gap, in DIP.</summary>
    public const double BarPitchDip = BarWidthDip + BarGapDip;

    /// <summary>频谱悬停表面在内容区四周的留白（DIP）。 / Padding the spectrum hover surface adds around the content in DIP.</summary>
    public const double SurfacePaddingDip = 1;

    /// <summary>像素柱状图中单个方块的高度（DIP）。 / Height of one block in the pixel bar chart, in DIP.</summary>
    public const double PixelDotHeightDip = 2;

    /// <summary>像素柱状图中相邻方块之间的净间距（DIP）。 / Clear gap between blocks in the pixel bar chart, in DIP.</summary>
    public const double PixelDotGapDip = 1;

    /// <summary>像素柱状图未点亮方块的默认相对不透明度；主题策略会按高对比度覆盖。 / Default opacity of unlit pixel blocks; the visual theme overrides it for high contrast.</summary>
    public const double DefaultUnlitPixelOpacity = 0.14;

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
    public static double CalculateContentWidthDip(int bandCount) =>
        CalculateBandContentWidth(SpectrumBandPolicy.ClampBandCount(bandCount));

    /// <summary>
    /// 按实际柱数计算内容宽度，不套用持久化最小值；竖向紧凑预览可能只有一柱。
    /// Calculates the content width from the actual band count without the persisted minimum; a compact vertical preview may have one band.
    /// </summary>
    public static double CalculateBandContentWidth(int bandCount)
    {
        var count = Math.Max(1, bandCount);
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
        // 此处使用已聚合的显示频段数；不再套用持久化设置的最小柱数。
        var step = (count * BarWidthDip + (count - 1) * BarGapDip) / (count - 1);
        var segments = WaveformSegmentsPerGap * (count - 1);
        WriteTopCurve(bands, sensitivityPercent, centre, maximumHalfHeight, step, destination[..(segments + 1)]);
        for (var index = 0; index <= segments; index++)
        {
            var point = destination[index];
            destination[length - 1 - index] = new SpectrumPoint(point.X, 2 * centre - point.Y);
        }

        return length;
    }

    /// <summary>
    /// 生成面积样式的闭合轮廓：左基线 → 平滑上缘 → 右基线。单柱时退化为一个顶边水平的矩形区域。
    /// Builds the closed area outline: left baseline, smooth top edge, right baseline. A single band degenerates into a rectangle
    /// with a flat top.
    /// </summary>
    /// <param name="bands">采样器给出的频段值。/ Band values from the sampler.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    /// <returns>轮廓点；没有频段时返回空数组。/ Outline points, or an empty array without bands.</returns>
    public static SpectrumPoint[] CreateAreaOutline(ReadOnlySpan<float> bands, int sensitivityPercent, double contentHeightDip)
    {
        if (bands.Length == 0)
            return [];
        var outline = new SpectrumPoint[CalculateAreaPointCount(bands.Length)];
        WriteAreaOutline(bands, sensitivityPercent, contentHeightDip, outline);
        return outline;
    }

    /// <summary>把面积轮廓写入调用方缓冲，逐帧不分配新数组。/ Writes the area outline into caller-owned storage without per-frame arrays.</summary>
    /// <param name="bands">采样器给出的频段值。/ Band values from the sampler.</param>
    /// <param name="sensitivityPercent">灵敏度百分比。/ Sensitivity in percent.</param>
    /// <param name="contentHeightDip">内容区高度（DIP）。/ Content height in DIP.</param>
    /// <param name="destination">可复用输出缓冲。/ Reusable output buffer.</param>
    /// <returns>写入点数。/ Number of written points.</returns>
    public static int WriteAreaOutline(ReadOnlySpan<float> bands, int sensitivityPercent, double contentHeightDip,
        Span<SpectrumPoint> destination)
    {
        if (bands.Length == 0)
            return 0;
        var length = CalculateAreaPointCount(bands.Length);
        if (destination.Length < length)
            throw new ArgumentException("The area destination is too small.", nameof(destination));

        var count = bands.Length;
        var height = double.IsFinite(contentHeightDip) ? Math.Max(0, contentHeightDip) : 0;
        if (count == 1)
        {
            var top = height - ResolveLevel(bands[0], sensitivityPercent) * height;
            destination[0] = new SpectrumPoint(0, height);
            destination[1] = new SpectrumPoint(0, top);
            destination[2] = new SpectrumPoint(BarWidthDip, top);
            destination[3] = new SpectrumPoint(BarWidthDip, height);
            return 4;
        }

        var step = (count * BarWidthDip + (count - 1) * BarGapDip) / (count - 1);
        var segments = WaveformSegmentsPerGap * (count - 1);
        destination[0] = new SpectrumPoint(0, height);
        WriteTopCurve(bands, sensitivityPercent, height, height, step, destination.Slice(1, segments + 1));
        destination[length - 1] = new SpectrumPoint((count - 1) * step, height);
        return length;
    }

    /// <summary>面积轮廓的点数；单柱为四个角点。 / Number of area points; a single band uses four corners.</summary>
    /// <param name="bandCount">柱数。/ Bar count.</param>
    public static int CalculateAreaPointCount(int bandCount)
    {
        var count = Math.Max(1, bandCount);
        return count == 1 ? 4 : WaveformSegmentsPerGap * (count - 1) + 3;
    }

    private static void WriteTopCurve(ReadOnlySpan<float> bands, int sensitivityPercent, double baseline,
        double amplitudeRange, double step, Span<SpectrumPoint> destination)
    {
        var count = bands.Length;
        var segments = WaveformSegmentsPerGap * (count - 1);
        for (var segment = 0; segment < count - 1; segment++)
        {
            var p0 = ResolveLevel(bands[Math.Max(0, segment - 1)], sensitivityPercent) * amplitudeRange;
            var p1 = ResolveLevel(bands[segment], sensitivityPercent) * amplitudeRange;
            var p2 = ResolveLevel(bands[segment + 1], sensitivityPercent) * amplitudeRange;
            var p3 = ResolveLevel(bands[Math.Min(count - 1, segment + 2)], sensitivityPercent) * amplitudeRange;
            for (var stepIndex = 0; stepIndex < WaveformSegmentsPerGap; stepIndex++)
            {
                var t = stepIndex / (double)WaveformSegmentsPerGap;
                var y = 0.5 * ((2 * p1) +
                               (-p0 + p2) * t +
                               (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                               (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                var index = segment * WaveformSegmentsPerGap + stepIndex;
                destination[index] = new SpectrumPoint((segment + t) * step, baseline - Math.Clamp(y, 0, amplitudeRange));
            }
        }

        destination[segments] = new SpectrumPoint(
            (count - 1) * step,
            baseline - ResolveLevel(bands[count - 1], sensitivityPercent) * amplitudeRange);
    }

    private static int GetWaveformOutlineLength(int count) => checked(2 * (WaveformSegmentsPerGap * (count - 1) + 1));

    private static double ResolveLevel(float value, int sensitivityPercent) =>
        float.IsFinite(value) ? Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f) : 0f;

    /// <summary>频谱样式是否以垂直中线为对称轴（波形与对称柱状图）。 / Whether a spectrum style is symmetric about the vertical centre line (waveform and mirrored bars).</summary>
    /// <param name="style">频谱样式。/ Spectrum style.</param>
    public static bool IsSymmetric(SpectrumStyle style) =>
        style is SpectrumStyle.Waveform or SpectrumStyle.MirroredBars;

    /// <summary>指数跟随的吸附容差；没有它计时器会为 0.0001 的差距一直空转。/ Snap tolerance of the exponential follower; without it the timer spins for a 0.0001 gap.</summary>
    public const float FollowerTolerance = 0.002f;

    /// <summary>上升时间常数相对动效快速时长的比例；约 40 ms，打击乐不会拖慢。/ Rise time constant as a fraction of the motion fast duration; about 40 ms keeps percussion responsive.</summary>
    public const double RiseTimeConstantFraction = 0.33;

    /// <summary>下降时间常数相对动效快速时长的比例；约 140 ms，回落比上升慢。/ Fall time constant as a fraction of the motion fast duration; about 140 ms makes falling slower than rising.</summary>
    public const double FallTimeConstantFraction = 1.17;

    /// <summary>峰值保持时长（秒）。 / Peak hold duration in seconds.</summary>
    public const float PeakHoldSeconds = 0.45f;

    /// <summary>峰值下落速度（单位/秒）。 / Peak fall speed in level units per second.</summary>
    public const float PeakFallPerSecond = 0.8f;

    /// <summary>上升时间常数（秒）。 / Rise time constant in seconds.</summary>
    public static double ResolveRiseTimeConstantSeconds(TimeSpan fastDuration) =>
        Math.Max(0, fastDuration.TotalSeconds) * RiseTimeConstantFraction;

    /// <summary>下降时间常数（秒）。 / Fall time constant in seconds.</summary>
    public static double ResolveFallTimeConstantSeconds(TimeSpan fastDuration) =>
        Math.Max(0, fastDuration.TotalSeconds) * FallTimeConstantFraction;

    /// <summary>
    /// 光点中心相对内容区上边界的纵向位置。静音时仍保留一个离底的槽位。
    /// Vertical centre of a level dot relative to the content's top edge; silence keeps a visible slot just above the bottom.
    /// </summary>
    public static double ResolveDotCenterYDip(float value, int sensitivityPercent, double contentHeightDip, double dotDiameterDip)
    {
        var level = float.IsFinite(value) ? Math.Clamp(value * sensitivityPercent / 100f, 0f, 1f) : 0f;
        var height = double.IsFinite(contentHeightDip) ? Math.Max(0, contentHeightDip) : 0;
        var radius = dotDiameterDip / 2;
        var usable = Math.Max(0, height - dotDiameterDip);
        return Math.Clamp(radius + (1 - level) * usable, Math.Min(radius, height / 2), Math.Max(height / 2, height - radius));
    }

    /// <summary>
    /// 峰值帽中心相对内容区上边界的纵向位置。对称样式上下各一个，普通柱状图只在顶部。
    /// Vertical centre of a peak cap relative to the content's top edge; symmetric styles get top and bottom caps, plain bars only the top.
    /// </summary>
    public static double ResolvePeakCapCenterYDip(float value, int sensitivityPercent, double contentHeightDip,
        double capHeightDip, bool symmetric, bool bottom)
    {
        var scale = ResolveBarScale(value, sensitivityPercent, contentHeightDip);
        var center = symmetric
            ? contentHeightDip * (bottom ? 1 + scale : 1 - scale) / 2
            : contentHeightDip * (1 - scale);
        return Math.Clamp(center - capHeightDip / 2, 0, Math.Max(0, contentHeightDip - capHeightDip));
    }

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
