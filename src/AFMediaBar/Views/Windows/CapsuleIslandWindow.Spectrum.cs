using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛的律动频谱（编号 107）：左侧封面 + 右侧律动，<c>IsPlaying</c> 时逐帧起伏，暂停或无采集时按样式压平。
/// The capsule island's animated spectrum (item 107): a cover on the left and the animation on the right, rising and falling per frame while
/// <c>IsPlaying</c> and flattening per style while paused or without capture.
///
/// **形态跟随 <c>SpectrumComponent.Style</c> 四态**（用户裁定，覆盖任务书 §1"固定为连续波形"那条原裁定）：柱状图、连续波形、
/// 对称柱状图、像素柱，几何一律复用 <see cref="SpectrumPresentationPolicy"/> 里任务栏用的那一套（柱高比例、点亮方块数、方块纵向位置、
/// 对称轮廓），只把"谁推进"换成岛自己的帧回调。
/// **The shape follows the four <c>SpectrumComponent.Style</c> states** (the user's ruling, superseding task book §1's "always a continuous
/// waveform"): bars, the continuous waveform, mirrored bars, and the pixel chart, all reusing the very geometry the taskbar uses from
/// <see cref="SpectrumPresentationPolicy"/> (height ratio, lit-block count, block vertical position, symmetric outline) and changing only who
/// advances them.
///
/// 三件事都在这里，且都不新起帧源（红线 5）：
///  · **节流**：由 <see cref="CapsuleIslandWindow.Animation"/> 的合成帧回调把本帧真实耗时交给 <see cref="CapsuleSpectrumPolicy"/>，
///    够一个 <c>RefreshRateHz</c> 周期才调一次 <see cref="AudioMonitorService.GetSpectrum"/>；
///  · **平滑**：中间帧用 <see cref="SpectrumPresentationPolicy.AdvanceDisplayedLevel"/>（帧率无关的指数跟随，与任务栏同一份实现）
///    把 20 Hz 的采样摊开。**四种样式都走它**——柱状图在任务栏是交给 WPF 的 <c>ScaleY</c> 动画插值的，岛侧一律自己逐帧写几何，
///    因为再多一个推进者就等于第二条帧源；
///  · **几何**：柱子与方块按段数建一次、逐帧只写 <c>ScaleY</c> 或 <c>Opacity</c>；波形轮廓的点集长度只由段数决定，
///    一致就原地改写坐标、不一致才重建（接缝 S5）。
/// All three live here and none of them starts a frame source (red line 5):
///  · **Throttle**: the compositor callback in <see cref="CapsuleIslandWindow.Animation"/> hands this frame's real elapsed time to
///    <see cref="CapsuleSpectrumPolicy"/>, and <see cref="AudioMonitorService.GetSpectrum"/> is only called once per <c>RefreshRateHz</c> period;
///  · **Smoothing**: the frames in between spread a twenty-hertz sample through <see cref="SpectrumPresentationPolicy.AdvanceDisplayedLevel"/> (the
///    frame-rate-independent exponential follower, the same implementation the taskbar uses). **All four styles go through it**: on the taskbar the
///    bar styles are interpolated by WPF's ScaleY animation, while the island always writes its geometry per frame, because a second advancer would
///    be a second frame source;
///  · **Geometry**: bars and blocks are created once per band and only their ScaleY or Opacity is written afterwards; the waveform outline's point
///    count follows the band count alone, so coordinates are rewritten in place while it matches and the geometry is rebuilt only when it does not
///    (seam S5).
///
/// 数据源是**构造函数注入的容器单例**（<see cref="AudioMonitorService"/>）：自己 <c>new</c> 一个会另开一份 WASAPI 回环采集（接缝 S9）。
/// The data source is the **container singleton injected through the constructor** (<see cref="AudioMonitorService"/>): constructing one here would
/// open a second WASAPI loopback capture (seam S9).
/// </summary>
public partial class CapsuleIslandWindow
{
    /// <summary>
    /// 像素柱未点亮方块的 opacity。**必须与任务栏 <c>TaskBarMediaControl.Spectrum.cs</c> 的 <c>UnlitPixelOpacity</c> 相等**：
    /// 两处受各自文件边界限制无法共享同一常量，改一处就要改另一处，否则两块表面上的"没亮"看起来会不一样。
    /// The opacity of an unlit pixel block. **It has to equal <c>UnlitPixelOpacity</c> in the taskbar's <c>TaskBarMediaControl.Spectrum.cs</c>**: the
    /// two cannot share one constant because of their file boundaries, so changing one means changing the other, or "not lit" would look different on
    /// the two surfaces.
    /// </summary>
    private const double UnlitPixelOpacity = 0.16;

    /// <summary>容器单例的音频采集：<c>GetSpectrum</c> 返回 false 表示采集不可用，此时按细线处理。/ The container's audio capture singleton: a false from <c>GetSpectrum</c> means no capture is available, which the waveform answers with its flat line.</summary>
    private readonly AudioMonitorService _audioMonitorService;

    /// <summary>本帧要跟随到的采样目标（归一化频段值）。/ This frame's sampled targets, normalized band values.</summary>
    private float[] _capsuleSpectrumTargets = [];

    /// <summary>已经跟随过的显示值：几何由它算出，因此屏幕上看到的是平滑过的频谱而不是采样本身。/ The followed display values the geometry is built from, so what is on screen is the smoothed spectrum rather than the raw sample.</summary>
    private float[] _capsuleSpectrumDisplayed = [];

    /// <summary>当前缓冲区对应的段数；段数变化时缓冲区、几何、柱子与细线矩形一起重建。/ The band count the current buffers belong to; a change rebuilds the buffers, geometry, bars, and flat line together.</summary>
    private int _capsuleSpectrumBandCount;

    /// <summary>采样节流累加器（毫秒）：帧回调自己数时间，够一个周期才采一次。/ The sampling throttle accumulator in milliseconds: the frame callback counts the time itself and samples once per period.</summary>
    private double _capsuleSpectrumAccumulatedMilliseconds;

    /// <summary>最近一次采集是否可用；不可用时四种样式都压平。/ Whether the most recent capture was usable; when it is not, all four styles flatten.</summary>
    private bool _capsuleSpectrumCaptureAvailable;

    /// <summary>波形几何的折线段，逐帧原地改写它的点集；为 null 表示下一次绘制要重建几何。/ The waveform geometry's polyline segment, whose point set is rewritten in place every frame; null means the next draw rebuilds the geometry.</summary>
    private PolyLineSegment? _capsuleSpectrumSegment;

    /// <summary>压平后的细线几何（与采样无关，因此暂停期间每帧完全相同）。/ The flattened line's geometry, independent of the samples and therefore identical on every frame while paused.</summary>
    private RectangleGeometry? _capsuleSpectrumFlatLine;

    /// <summary>柱状 / 对称柱 / 像素柱用的方块（前景 `Fill` 的刷新要遍历它们）。/ The blocks the bar, mirrored-bar, and pixel styles use, which the foreground refresh walks.</summary>
    private readonly List<Shape> _capsuleSpectrumShapes = [];

    /// <summary>柱子与其缩放变换：柱高由 <c>ScaleY</c> 直接写（**不是** WPF 动画），因此每帧只改一个 double。/ Each bar with its scale transform: the height is written straight into ScaleY (never a WPF animation), so a frame changes one double.</summary>
    private readonly List<(Rectangle Bar, ScaleTransform Scale)> _capsuleSpectrumBarVisuals = [];

    /// <summary>像素柱的方块列（自下而上）。/ The pixel chart's block columns, bottom-up.</summary>
    private readonly List<List<Rectangle>> _capsuleSpectrumPixelColumns = [];

    /// <summary>当前视觉树对应的样式/段数/内容区高度：任一变化都要重建方块。/ The style, band count, and content height the current visual tree belongs to; any change rebuilds the blocks.</summary>
    private SpectrumStyle _capsuleSpectrumVisualStyle = SpectrumStyle.Bars;
    private int _capsuleSpectrumVisualBandCount = -1;
    private double _capsuleSpectrumVisualContentHeight = double.NaN;

    /// <summary>
    /// 推进频谱一帧：先按 <c>RefreshRateHz</c> 决定这一帧要不要采样，再决定起伏还是压平，最后重画。
    /// Advances the spectrum by one frame: first decide whether this frame samples from <c>RefreshRateHz</c>, then whether it rises and falls or
    /// flattens, and finally repaint.
    /// </summary>
    /// <param name="elapsedMilliseconds">本帧真实经过的毫秒数（来自合成器）。/ This frame's real elapsed milliseconds, from the compositor.</param>
    private void AdvanceCapsuleSpectrum(double elapsedMilliseconds)
    {
        if (_isClosing)
            return;

        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        var bandCount = SpectrumBandPolicy.ClampBandCount(settings.BandCount);
        EnsureCapsuleSpectrumBuffers(bandCount);
        EnsureCapsuleSpectrumVisuals(settings, bandCount);

        var playing = _snapshot.IsConnected && _snapshot.IsPlaying;

        // 节流：只在播放期间采样，且够一个周期才采一次。暂停时完全不调 GetSpectrum——采集也用不着为一条静止细线跑。
        // Throttle: sample only while playing, and only once a period has elapsed. While paused GetSpectrum is not called at all, because a
        // stationary line does not need capture running either.
        if (playing)
        {
            _capsuleSpectrumAccumulatedMilliseconds = CapsuleSpectrumPolicy.Accumulate(
                _capsuleSpectrumAccumulatedMilliseconds,
                elapsedMilliseconds,
                settings.RefreshRateHz);

            if (CapsuleSpectrumPolicy.ShouldSample(_capsuleSpectrumAccumulatedMilliseconds, settings.RefreshRateHz))
            {
                _capsuleSpectrumAccumulatedMilliseconds = 0;
                _capsuleSpectrumCaptureAvailable = _audioMonitorService.GetSpectrum(_capsuleSpectrumTargets, bandCount);
            }
        }

        // 暂停、断连或采集不可用：同一条路，按样式压平（不是隐藏，也不是冻结最后一帧）。
        // A pause, a disconnection, or an unusable capture share one path and flatten per style — never a hidden shape and never a frozen last frame.
        if (CapsuleSpectrumPolicy.ShouldFlatten(playing, _capsuleSpectrumCaptureAvailable))
        {
            FlattenCapsuleSpectrum(settings, bandCount);
            return;
        }

        var motion = MotionPolicy.ResolveCurrent();
        if (!motion.UseContinuousMotion)
        {
            // 动效被降级时不做时间跟随，直接把显示值落到采样值上——与任务栏在该档位下的行为一致。
            // When motion is downgraded there is no follower: the displayed values snap onto the samples, matching what the taskbar does at this
            // tier.
            Array.Copy(_capsuleSpectrumTargets, _capsuleSpectrumDisplayed, bandCount);
        }
        else
        {
            var elapsed = TimeSpan.FromMilliseconds(elapsedMilliseconds);
            for (var index = 0; index < bandCount; index++)
            {
                _capsuleSpectrumDisplayed[index] = SpectrumPresentationPolicy.AdvanceDisplayedLevel(
                    _capsuleSpectrumDisplayed[index],
                    _capsuleSpectrumTargets[index],
                    elapsed,
                    motion.FastDuration);
            }
        }

        DrawCapsuleSpectrum(settings, bandCount);
    }

    /// <summary>
    /// 频谱设置变更后重新应用一次（样式 / 柱数 / 内容区高度 / 灵敏度 / 刷新率）：落一次波形列几何，并立刻按新设置重画。
    ///
    /// **这一段必须存在**：几何（列宽/列高）只在形变或缩放变化时才会被写，而这个方法之外没有任何东西会在设置页改了频谱组件之后
    /// 把它重新落一次；而频谱本身只在**播放中**才由帧循环推进，所以暂停时改设置连重画都不会发生。两者合起来就是
    /// "频谱调整不会应用到灵动岛上"这条报障的根因。
    /// Re-applies the spectrum settings after a change (style, band count, content height, sensitivity, refresh rate): it lands the column's geometry
    /// once and repaints immediately from the new settings.
    ///
    /// **This has to exist**: the geometry (column width and height) is only written on a morph or a scale change, and nothing outside this method
    /// would land it again once the spectrum component has been edited on the settings page; and the spectrum itself is only advanced by the frame
    /// loop **while playing**, so a settings change while paused would not even repaint. Together those two are the root cause of the report that
    /// "spectrum adjustments do not reach the dynamic island".
    /// </summary>
    public void ApplySpectrumSettings()
    {
        if (_isClosing)
            return;

        var settings = SettingsManager.Current.SpectrumComponent.Normalize();
        var bandCount = SpectrumBandPolicy.ClampBandCount(settings.BandCount);
        EnsureCapsuleSpectrumBuffers(bandCount);
        // 样式换了要换一套方块（柱子 ↔ 像素块），且像素块的数量由内容区高度决定。
        // A style change swaps one set of blocks for another (bars for pixel blocks), and the block count follows the content height.
        EnsureCapsuleSpectrumVisuals(settings, bandCount);

        // 段数没变而只改了内容区高度时，缓冲与点数都不变，因此上面那次调用不会重写几何；这里显式再落一次。
        // 写入点仍然只有 ApplyCapsuleViewSize 一处（裁定 10）。
        // A content-height-only change leaves the buffers and the point count untouched, so the call above does not rewrite the geometry; this lands
        // it explicitly. ApplyCapsuleViewSize remains the only place that writes it (ruling 10).
        ApplyCapsuleViewSize();

        // 立刻重画：暂停时按新样式压平，播放中按新的样式/灵敏度/段数起伏。
        // Repaint at once: while paused it flattens under the new style, and while playing it follows the new style, sensitivity, and band count.
        var playing = _snapshot.IsConnected && _snapshot.IsPlaying;
        if (CapsuleSpectrumPolicy.ShouldFlatten(playing, _capsuleSpectrumCaptureAvailable))
            FlattenCapsuleSpectrum(settings, bandCount);
        else
            DrawCapsuleSpectrum(settings, bandCount);
    }

    /// <summary>
    /// 压平：把采样目标与显示值一起清零，然后走**同一条绘制路径**。
    ///
    /// 清零不是"冻结最后一帧"，而是让四种样式各自落到既有的静音语义上：波形落到 1 DIP 居中细线（它的零振幅闭合轮廓是零面积，
    /// <see cref="CapsuleSpectrumPolicy.IsSilent"/> 那一支会改画细线），柱状与对称柱落到 <c>MinimumBarHeightDip</c>（3 DIP）的最小柱，
    /// 像素柱落到最下方 1 个方块。因此这里**不需要**第二套"压平"实现，也就不会与播放态慢慢分叉。
    /// Flattening: the sampled targets and the displayed values are both cleared, and then the **same draw path** runs.
    ///
    /// Clearing is not "freeze the last frame": it makes each style land on the existing silence semantics — the waveform on its one-DIP centred line
    /// (its zero-amplitude closed outline has no area, and the <see cref="CapsuleSpectrumPolicy.IsSilent"/> branch draws the line instead), the bar and
    /// mirrored-bar styles on the <c>MinimumBarHeightDip</c> minimum bar, and the pixel chart on a single bottom block. That is why no second
    /// "flatten" implementation exists here and why it cannot slowly drift away from the playing path.
    /// </summary>
    /// <param name="settings">已归一化的频谱设置。/ The normalized spectrum settings.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void FlattenCapsuleSpectrum(SpectrumComponentSettings settings, int bandCount)
    {
        Array.Clear(_capsuleSpectrumTargets, 0, bandCount);
        Array.Clear(_capsuleSpectrumDisplayed, 0, bandCount);
        DrawCapsuleSpectrum(settings, bandCount);
    }

    /// <summary>
    /// 按设置里的样式重画一帧。
    /// Repaints one frame using the style from the settings.
    ///
    /// 四种样式共用这一条入口，是因为"压平"就是"显示值全为零时的那一帧"：柱状与像素柱在零值下自然落到最小柱与单个方块，只有波形需要
    /// 一个额外的分支（零振幅轮廓是零面积，什么都不画），这一条同时兑现了控制器第 ② 条裁定——"播放中但没有音频包"与暂停视觉一致。
    /// All four styles share this one entry point because "flattened" is simply "the frame where every displayed level is zero": the bar and pixel
    /// styles land on the minimum bar and the single block by themselves, and only the waveform needs an extra branch (its zero-amplitude outline has
    /// no area and paints nothing). That same branch also delivers the controller's ruling two — "playing with no audio packets" looks exactly like a
    /// pause.
    /// </summary>
    /// <param name="settings">已归一化的频谱设置（样式、灵敏度与内容区高度都从它取）。/ The normalized settings, which supply the style, sensitivity, and content height.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void DrawCapsuleSpectrum(SpectrumComponentSettings settings, int bandCount)
    {
        switch (CapsuleSpectrumPolicy.ResolveShape(settings.Style))
        {
            case CapsuleSpectrumShape.Waveform:
                if (CapsuleSpectrumPolicy.IsSilent(_capsuleSpectrumDisplayed, bandCount))
                {
                    InstallCapsuleSpectrumFlatLine(settings, bandCount);
                    return;
                }

                DrawCapsuleWaveform(settings, bandCount);
                break;

            case CapsuleSpectrumShape.PixelBars:
                DrawCapsulePixels(settings, bandCount);
                break;

            default:
                DrawCapsuleBars(settings, bandCount);
                break;
        }
    }

    /// <summary>
    /// 把那条静止细线挂上波形 <c>Path</c>（一条横向铺满、纵向居中、约 1 DIP 高的矩形，与采样无关）。
    ///
    /// 已经是这条细线时不再重复挂几何：暂停期间因此完全没有写操作，多帧像素逐帧完全一致（验收判据）。
    /// Installs the stationary line on the waveform Path: one rectangle spanning the content width, vertically centred, about one DIP tall, and
    /// independent of the samples.
    ///
    /// Once the line is already installed nothing is assigned again, so a pause performs no writes at all and the pixels are identical frame to frame
    /// (an acceptance criterion).
    /// </summary>
    /// <param name="settings">已归一化的频谱设置。/ The normalized spectrum settings.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void InstallCapsuleSpectrumFlatLine(SpectrumComponentSettings settings, int bandCount)
    {
        var content = CapsuleSpectrumPolicy.ResolveContentSizeDip(settings);
        var rect = CapsuleSpectrumPolicy.ResolveFlatLineRect(content.Width, content.Height);

        if (_capsuleSpectrumFlatLine is null)
        {
            _capsuleSpectrumFlatLine = new RectangleGeometry(rect);
            // 折线段随之作废：恢复播放时由 DrawCapsuleWaveform 重建几何。
            // The polyline is invalidated with it: DrawCapsuleWaveform rebuilds the geometry when playback resumes.
            _capsuleSpectrumSegment = null;
        }
        else if (!_capsuleSpectrumFlatLine.Rect.Equals(rect))
        {
            _capsuleSpectrumFlatLine.Rect = rect;
        }

        if (!ReferenceEquals(CapsuleSpectrumWaveform.Data, _capsuleSpectrumFlatLine))
            CapsuleSpectrumWaveform.Data = _capsuleSpectrumFlatLine;
    }

    /// <summary>
    /// 用跟随过的显示值重画波形轮廓。轮廓点由 <see cref="SpectrumPresentationPolicy.CreateWaveformOutline"/> 生成（上下缘镜像，
    /// 严格对称），点数与折线段一致时**原地改写坐标**，不一致才重建几何。
    /// Repaints the waveform outline from the followed display values. The outline comes from
    /// <see cref="SpectrumPresentationPolicy.CreateWaveformOutline"/> (the lower edge mirrors the upper one, exactly symmetric); matching point
    /// counts **rewrite coordinates in place** and only a mismatch rebuilds the geometry.
    /// </summary>
    /// <param name="settings">已归一化的频谱设置（灵敏度与内容区高度都从它取）。/ The normalized settings, which supply the sensitivity and the content height.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void DrawCapsuleWaveform(SpectrumComponentSettings settings, int bandCount)
    {
        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(
            _capsuleSpectrumDisplayed.AsSpan(0, bandCount),
            settings.SensitivityPercent,
            SpectrumPresentationPolicy.ResolveContentHeightDip(settings));

        if (outline.Length == 0)
        {
            // 段数少于两个时 CreateWaveformOutline 给空数组：兜住它，落到细线上，而不是留下 NaN 或崩掉的形状。
            // With fewer than two bands CreateWaveformOutline answers an empty array: catch it and land on the line rather than leaving NaN or a
            // broken shape behind.
            InstallCapsuleSpectrumFlatLine(settings, bandCount);
            return;
        }

        var segment = _capsuleSpectrumSegment;
        if (segment is null || segment.Points.Count != outline.Length)
        {
            BuildCapsuleSpectrumGeometry(outline);
            return;
        }

        var points = segment.Points;
        for (var index = 0; index < outline.Length; index++)
            points[index] = new Point(outline[index].X, outline[index].Y);
    }

    /// <summary>
    /// 按轮廓重建几何（只在段数变化或从细线恢复时发生）。几何与它的点集一次建好，之后逐帧只写坐标，因此每帧没有新的几何分配。
    /// Rebuilds the geometry from an outline, which only happens when the band count changes or the wave returns from its line. The geometry and its
    /// point set are built once and only coordinates are written afterwards, so no geometry is allocated per frame.
    /// </summary>
    /// <param name="outline">轮廓点（内容区局部坐标）。/ The outline points in the content area's local coordinates.</param>
    private void BuildCapsuleSpectrumGeometry(ReadOnlySpan<SpectrumPoint> outline)
    {
        if (outline.Length == 0)
            return;

        var points = new PointCollection(outline.Length);
        foreach (var point in outline)
            points.Add(new Point(point.X, point.Y));

        var segment = new PolyLineSegment { Points = points };
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        _capsuleSpectrumSegment = segment;
        _capsuleSpectrumFlatLine = null;
        CapsuleSpectrumWaveform.Data = geometry;
    }

    /// <summary>
    /// 写柱状图与对称柱状图一帧：柱高即 <c>ScaleY</c>，取自任务栏同一个 <see cref="SpectrumPresentationPolicy.ResolveBarScale"/>
    /// （静音时它给的就是最小柱高 3 DIP）。
    ///
    /// 柱子的基准高度是整块内容区高度，缩放原点在底边（柱状图）或垂直中线（对称柱状图）——与任务栏
    /// <c>BuildSpectrumBars</c> 的 <c>RenderTransformOrigin</c> 逐字相同，因此两种形态的比例完全一致。
    /// Writes one frame of the bar and mirrored-bar styles: the height is ScaleY, taken from the same
    /// <see cref="SpectrumPresentationPolicy.ResolveBarScale"/> the taskbar uses (at silence it already answers the three-DIP minimum bar).
    ///
    /// Each bar's baseline height is the whole content area, with the scale origin on its bottom edge (bars) or the vertical centre (mirrored bars) —
    /// word for word the RenderTransformOrigin the taskbar's BuildSpectrumBars sets, so the two shapes keep identical proportions.
    /// </summary>
    /// <param name="settings">已归一化的频谱设置。/ The normalized spectrum settings.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void DrawCapsuleBars(SpectrumComponentSettings settings, int bandCount)
    {
        var contentHeight = SpectrumPresentationPolicy.ResolveContentHeightDip(settings);
        for (var index = 0; index < _capsuleSpectrumBarVisuals.Count; index++)
        {
            var (_, scale) = _capsuleSpectrumBarVisuals[index];
            var value = index < bandCount && index < _capsuleSpectrumDisplayed.Length ? _capsuleSpectrumDisplayed[index] : 0;
            var target = SpectrumPresentationPolicy.ResolveBarScale(value, settings.SensitivityPercent, contentHeight);
            // 相等就一个字节都不写：暂停/静音时这里每帧算出同一个值，跳过写入让"多帧完全一致"成为构造上的事实。
            // Nothing is written while the value matches: a paused or silent frame computes the same number every time, and skipping the write makes
            // "identical across frames" true by construction.
            if (Math.Abs(scale.ScaleY - target) > 0.0001)
                scale.ScaleY = target;
        }
    }

    /// <summary>
    /// 写像素柱一帧：每列点亮 <see cref="SpectrumPresentationPolicy.ResolveLitPixelCount"/> 个方块（下限 1，因此静音时只亮最下面一个）。
    /// Writes one frame of the pixel style: each column lights <see cref="SpectrumPresentationPolicy.ResolveLitPixelCount"/> blocks (floored at one,
    /// so silence lights exactly the bottom block).
    /// </summary>
    /// <param name="settings">已归一化的频谱设置。/ The normalized spectrum settings.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void DrawCapsulePixels(SpectrumComponentSettings settings, int bandCount)
    {
        var contentHeight = SpectrumPresentationPolicy.ResolveContentHeightDip(settings);
        for (var index = 0; index < _capsuleSpectrumPixelColumns.Count; index++)
        {
            var column = _capsuleSpectrumPixelColumns[index];
            var value = index < bandCount && index < _capsuleSpectrumDisplayed.Length ? _capsuleSpectrumDisplayed[index] : 0;
            var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(value, settings.SensitivityPercent, contentHeight);
            for (var dotIndex = 0; dotIndex < column.Count; dotIndex++)
            {
                var opacity = dotIndex < lit ? 1 : UnlitPixelOpacity;
                if (Math.Abs(column[dotIndex].Opacity - opacity) > 0.0001)
                    column[dotIndex].Opacity = opacity;
            }
        }
    }

    /// <summary>
    /// 按当前样式建好这一套方块（只在样式、段数或内容区高度变化时发生）。
    ///
    /// 波形样式只需要那一枚 <c>Path</c>，因此这里把 <c>Canvas</c> 清空并整体收起；其余三种样式清空 <c>Path</c> 并显示 <c>Canvas</c>。
    /// 任何一次重建之后都重刷一次前景：新方块的 <c>Fill</c> 必须与文字/图标同源（<c>PlayerForegroundPolicy</c>，裁定 10）。
    /// Builds this style's set of blocks, which only happens when the style, the band count, or the content height changes.
    ///
    /// The waveform style needs nothing but that one Path, so the Canvas is emptied and collapsed; the other three empty the Path and show the Canvas.
    /// Every rebuild refreshes the foreground afterwards: a new block's Fill has to come from the same source as the text and icons
    /// (PlayerForegroundPolicy, ruling 10).
    /// </summary>
    /// <param name="settings">已归一化的频谱设置。/ The normalized spectrum settings.</param>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void EnsureCapsuleSpectrumVisuals(SpectrumComponentSettings settings, int bandCount)
    {
        var content = CapsuleSpectrumPolicy.ResolveContentSizeDip(settings);
        if (_capsuleSpectrumVisualStyle == settings.Style &&
            _capsuleSpectrumVisualBandCount == bandCount &&
            Math.Abs(_capsuleSpectrumVisualContentHeight - content.Height) < 0.01)
        {
            return;
        }

        _capsuleSpectrumVisualStyle = settings.Style;
        _capsuleSpectrumVisualBandCount = bandCount;
        _capsuleSpectrumVisualContentHeight = content.Height;

        _capsuleSpectrumShapes.Clear();
        _capsuleSpectrumBarVisuals.Clear();
        _capsuleSpectrumPixelColumns.Clear();
        CapsuleSpectrumBars.Children.Clear();

        var shape = CapsuleSpectrumPolicy.ResolveShape(settings.Style);
        var usesWaveform = shape == CapsuleSpectrumShape.Waveform;
        CapsuleSpectrumWaveform.Visibility = usesWaveform ? Visibility.Visible : Visibility.Collapsed;
        CapsuleSpectrumBars.Visibility = usesWaveform ? Visibility.Collapsed : Visibility.Visible;
        _capsuleSpectrumSegment = null;
        _capsuleSpectrumFlatLine = null;

        switch (shape)
        {
            case CapsuleSpectrumShape.Waveform:
                // 波形：几何由 DrawCapsuleWaveform 在建/改点时创建或改写。
                // The waveform: its geometry is created or rewritten by DrawCapsuleWaveform.
                break;

            case CapsuleSpectrumShape.PixelBars:
                BuildCapsulePixelColumns(bandCount, content.Height);
                break;

            default:
                BuildCapsuleBars(bandCount, shape == CapsuleSpectrumShape.MirroredBars, content.Height);
                break;
        }

        // 前景与文字/图标同源：新方块必须立刻拿到当前那一支颜色，否则样式切过来的一瞬间会是一块黑。
        // The foreground shares the text and icons' source: a new block has to take the current colour at once, or switching styles would show a black
        // block for a moment.
        ApplyPlayerForeground();
    }

    /// <summary>
    /// 建柱状图 / 对称柱状图的柱子：基准高度 = 整块内容区高度，渲染变换原点在底边或垂直中线，纵向缩放由每帧的显示值写。
    /// Builds the bar and mirrored-bar columns: baseline height is the whole content area, the render-transform origin sits on the bottom edge or the
    /// vertical centre, and the vertical scale is written from each frame's displayed value.
    /// </summary>
    /// <param name="bandCount">柱数。/ The bar count.</param>
    /// <param name="symmetric">是否以垂直中线为原点（对称柱状图）。/ Whether the origin is the vertical centre (the mirrored style).</param>
    /// <param name="contentHeight">内容区高度（DIP）。/ The content height in DIP.</param>
    private void BuildCapsuleBars(int bandCount, bool symmetric, double contentHeight)
    {
        // 初值就取静音档：样式刚切过来时不会先闪一根满高的柱子。
        // The initial value is the silent one, so a freshly switched style never flashes a full-height bar first.
        var initialScale = SpectrumPresentationPolicy.ResolveBarScale(0, 100, contentHeight);
        for (var index = 0; index < bandCount; index++)
        {
            var scale = new ScaleTransform(1, initialScale);
            var bar = new Rectangle
            {
                Width = SpectrumPresentationPolicy.BarWidthDip,
                Height = Math.Max(1, contentHeight),
                // 对称柱以垂直中点为原点，音量升高时同时向上下延伸；贴底柱仍从底边向上长。
                // The symmetric style scales about the vertical centre so a louder band grows both ways, while the bottom-anchored one grows from the
                // bottom edge.
                RenderTransformOrigin = symmetric ? new Point(0.5, 0.5) : new Point(0.5, 1),
                RenderTransform = scale,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bar, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
            Canvas.SetTop(bar, 0);
            CapsuleSpectrumBars.Children.Add(bar);
            _capsuleSpectrumBarVisuals.Add((bar, scale));
            _capsuleSpectrumShapes.Add(bar);
        }
    }

    /// <summary>
    /// 建像素柱的方块列：方块数量与纵向位置都由内容区高度决定（与任务栏 <c>BuildSpectrumPixelColumns</c> 同一套算式）。
    /// Builds the pixel style's block columns: both the block count and each block's vertical position come from the content height, using the same
    /// arithmetic the taskbar's BuildSpectrumPixelColumns does.
    /// </summary>
    /// <param name="bandCount">列数。/ The column count.</param>
    /// <param name="contentHeight">内容区高度（DIP）。/ The content height in DIP.</param>
    private void BuildCapsulePixelColumns(int bandCount, double contentHeight)
    {
        var dotCount = SpectrumPresentationPolicy.ResolvePixelDotCount(contentHeight);
        for (var index = 0; index < bandCount; index++)
        {
            var column = new List<Rectangle>(dotCount);
            for (var dotIndex = 0; dotIndex < dotCount; dotIndex++)
            {
                var dot = new Rectangle
                {
                    Width = SpectrumPresentationPolicy.BarWidthDip,
                    Height = SpectrumPresentationPolicy.PixelDotHeightDip,
                    Opacity = UnlitPixelOpacity,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, SpectrumPresentationPolicy.ResolveBarLeftDip(index));
                Canvas.SetTop(dot, SpectrumPresentationPolicy.ResolvePixelDotTopDip(dotIndex, contentHeight));
                CapsuleSpectrumBars.Children.Add(dot);
                column.Add(dot);
                _capsuleSpectrumShapes.Add(dot);
            }

            _capsuleSpectrumPixelColumns.Add(column);
        }
    }

    /// <summary>
    /// 保证两个缓冲区与当前段数一致，并在段数真的变了时把列宽按新段数重写一次。
    /// Keeps both buffers in step with the current band count and, when it really changed, rewrites the column width for the new count.
    ///
    /// 列宽的写入**只在 <see cref="ApplyCapsuleViewSize"/> 里**（裁定 10）：这里只是那次设置的变更路径——设置页改段数不一定触发一次
    /// 形变，而波形列的宽度是"柱数决定"的，不跟着改就会让频谱被裁掉或留出空白。
    /// The width itself is only ever written **inside <see cref="ApplyCapsuleViewSize"/>** (ruling 10): this is merely that setting's change path —
    /// editing the band count on the settings page does not necessarily trigger a morph, and the column's width follows the band count, so leaving it
    /// stale would clip the spectrum or leave a blank.
    /// </summary>
    /// <param name="bandCount">当前段数。/ The current band count.</param>
    private void EnsureCapsuleSpectrumBuffers(int bandCount)
    {
        if (_capsuleSpectrumBandCount == bandCount &&
            _capsuleSpectrumTargets.Length >= bandCount &&
            _capsuleSpectrumDisplayed.Length >= bandCount)
        {
            return;
        }

        _capsuleSpectrumBandCount = bandCount;
        _capsuleSpectrumTargets = new float[bandCount];
        _capsuleSpectrumDisplayed = new float[bandCount];
        // 段数变了：点数、柱子数量与细线宽度都跟着变，视觉树与几何一起作废（下一次绘制/压平会按新段数重建）。
        // The band count changed, so the point count, the bar count, and the line's width change with it; the visual tree and the geometry are both
        // invalidated and rebuilt on the next draw or flatten.
        _capsuleSpectrumSegment = null;
        _capsuleSpectrumFlatLine = null;
        _capsuleSpectrumVisualBandCount = -1;

        ApplyCapsuleViewSize();
    }
}
