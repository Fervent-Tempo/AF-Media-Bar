using System.Windows;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 岛侧频谱的呈现形态：<c>SpectrumComponent.Style</c> 四态各自的几何族。
/// The island's spectrum presentation shape: the geometry family each of the four <c>SpectrumComponent.Style</c> states uses.
///
/// 用户已裁定岛**跟随**该设置（覆盖任务书 §1"固定为连续波形"那条原裁定），因此四种样式在岛上都要有真实的几何，
/// 而不是一律画波形。
/// The user has ruled that the island **follows** that setting (superseding task book §1's "always a continuous waveform"), so all four styles need
/// real geometry on the island instead of everything being drawn as a waveform.
/// </summary>
public enum CapsuleSpectrumShape
{
    /// <summary>底部起柱：复用 <c>ResolveBarScale</c> 的柱高比例，柱子贴内容区底边。/ Bottom-anchored bars, reusing ResolveBarScale's height ratio with each bar on the content area's bottom edge.</summary>
    Bars = 0,

    /// <summary>连续波形：复用 <c>CreateWaveformOutline</c> 的对称闭合轮廓。/ The continuous waveform, reusing CreateWaveformOutline's symmetric closed outline.</summary>
    Waveform = 1,

    /// <summary>对称柱：同一套柱高比例，但以内容区垂直中线为原点向上下同时生长。/ Mirrored bars: the same height ratio growing both ways from the content area's vertical centre.</summary>
    MirroredBars = 2,

    /// <summary>像素柱：复用 <c>ResolveLitPixelCount</c> / <c>ResolvePixelDotTopDip</c> 的方块列。/ The pixel chart, reusing ResolveLitPixelCount's and ResolvePixelDotTopDip's block columns.</summary>
    PixelBars = 3
}

/// <summary>
/// 一种样式在"没有任何显示值"时呈现什么。控制器裁定：沿用任务栏既有的静音语义，不要自创。
/// What a style shows when it has no displayed level at all. The controller's ruling: reuse the taskbar's existing silence semantics, invent nothing.
/// </summary>
public enum CapsuleSpectrumFlatShape
{
    /// <summary>一条居中的静止细线（波形）。/ One stationary centred line, for the waveform.</summary>
    Line = 0,

    /// <summary>最小柱（柱状图与对称柱状图）：<c>ResolveBarScale</c> 在 0 档给出的就是 <c>MinimumBarHeightDip</c>。/ The minimum bar (bars and mirrored bars): ResolveBarScale at level zero already answers MinimumBarHeightDip.</summary>
    MinimumBars = 1,

    /// <summary>最下方一个方块（像素柱）：<c>ResolveLitPixelCount</c> 的下限。/ A single block at the bottom, the pixel chart's floor from ResolveLitPixelCount.</summary>
    SinglePixel = 2
}

/// <summary>
/// 胶囊波形的采样节流与压平判定（编号 107 的纯逻辑部分）。
/// The capsule waveform's sampling throttle and its flattening decision, the pure-logic half of item 107.
///
/// 岛**只有一条帧源**（<c>CompositionTarget.Rendering</c>，红线 5），因此采样周期不能靠 <c>DispatcherTimer</c> 计时：
/// 由帧回调把本帧真实经过的毫秒数累加进来，够一个 <c>RefreshRateHz</c> 周期才去调一次 <c>GetSpectrum</c>，
/// 中间帧用 <see cref="SpectrumPresentationPolicy.AdvanceDisplayedLevel"/> 把 20 Hz 的采样摊平成逐帧的平滑。
/// The island has **one frame source only** (<c>CompositionTarget.Rendering</c>, red line 5), so the sampling period cannot be timed by a
/// <c>DispatcherTimer</c>: the frame callback accumulates this frame's real elapsed milliseconds, one <c>GetSpectrum</c> call goes out per
/// <c>RefreshRateHz</c> period, and the frames in between spread a twenty-hertz sample into per-frame smoothness through
/// <see cref="SpectrumPresentationPolicy.AdvanceDisplayedLevel"/>.
///
/// 四种样式**都不**交给 WPF 的 <c>BeginAnimation</c>/<c>ScaleY</c> 插值（那等于引入第二个推进者）：柱高、方块与轮廓一律由帧回调按
/// <see cref="SpectrumPresentationPolicy.AdvanceDisplayedLevel"/> 平滑后的显示值直接写几何，因此任务栏的
/// <c>NeedsFrameDrivenSmoothing</c>（柱状图交给动画引擎）在岛上**不适用**，这里一律逐帧推进。
/// None of the four styles delegates to WPF's <c>BeginAnimation</c>/<c>ScaleY</c> (that would be a second advancer): bar heights, blocks, and the
/// outline are all written from the per-frame smoothed displayed levels directly, so the taskbar's <c>NeedsFrameDrivenSmoothing</c> — which hands the
/// bar styles to the animation engine — deliberately does **not** apply on the island, where everything advances per frame.
/// </summary>
public static class CapsuleSpectrumPolicy
{
    /// <summary>
    /// 该样式在岛上画哪一种几何。未定义的取值回落到柱状图（与 <c>SpectrumComponentSettings.Normalize</c> 的回落一致），
    /// 因此一个坏掉的设置值不会让岛留下空槽，也不会抛异常。
    /// Which geometry that style draws on the island. An undefined value falls back to bars, matching SpectrumComponentSettings.Normalize, so a broken
    /// settings value can neither leave an empty slot nor throw.
    /// </summary>
    /// <param name="style">设置里的频谱样式。/ The spectrum style from the settings.</param>
    public static CapsuleSpectrumShape ResolveShape(SpectrumStyle style) => style switch
    {
        SpectrumStyle.Waveform => CapsuleSpectrumShape.Waveform,
        SpectrumStyle.MirroredBars => CapsuleSpectrumShape.MirroredBars,
        SpectrumStyle.PixelBars => CapsuleSpectrumShape.PixelBars,
        _ => CapsuleSpectrumShape.Bars,
    };

    /// <summary>
    /// 该几何在"零显示值"时的压平形态（控制器裁定）：波形 → 细线，柱状/对称柱 → 最小柱，像素柱 → 单个方块。
    ///
    /// 三种形态都不是"隐藏"，也不是"冻结最后一帧"：柱状与像素柱的最小量本来就是既有静音语义的自然结果，
    /// 只有波形需要额外画一条线——它在零振幅下的闭合轮廓是**零面积**的，什么都不画。
    /// Which flattened shape that geometry takes with zero displayed levels (the controller's ruling): a line for the waveform, the minimum bar for bars
    /// and mirrored bars, and a single block for the pixel chart.
    ///
    /// None of the three is "hidden" and none is a frozen last frame: the bar and pixel minima fall out of the existing silence semantics on their own,
    /// and only the waveform needs a line drawn for it — its closed outline at zero amplitude has **zero area** and paints nothing.
    /// </summary>
    /// <param name="shape">已解出的几何。/ The resolved geometry.</param>
    public static CapsuleSpectrumFlatShape ResolveFlatShape(CapsuleSpectrumShape shape) => shape switch
    {
        CapsuleSpectrumShape.Waveform => CapsuleSpectrumFlatShape.Line,
        CapsuleSpectrumShape.PixelBars => CapsuleSpectrumFlatShape.SinglePixel,
        _ => CapsuleSpectrumFlatShape.MinimumBars,
    };

    /// <summary>
    /// 显示值是否全是零（静音）。零振幅在波形样式下是一条零面积的退化轮廓，因此调用方必须改画细线；柱状与像素柱则各自落到最小柱与单个方块。
    ///
    /// 非有限值（NaN/∞）按 0 处理：它只会让波形落到细线上，绝不会把 NaN 传进几何（<c>Math.Clamp(NaN, 0, 1)</c> 仍然是 NaN）。
    /// 这一条覆盖了控制器第 ② 条裁定——"播放中但 GetSpectrum 返回 true 且缓冲全是 0"与暂停是同一个观感，走同一条压平路径。
    /// Whether every displayed level is zero (silence). Zero amplitude is a zero-area degenerate outline in the waveform style, so the caller has to
    /// draw the line instead, while the bar and pixel styles land on the minimum bar and the single block by themselves.
    ///
    /// A non-finite value (NaN or infinity) counts as zero: it can only put the waveform on its line and can never feed NaN into the geometry
    /// (<c>Math.Clamp(NaN, 0, 1)</c> is still NaN). This covers the controller's ruling two — "playing while GetSpectrum answers true with an all-zero
    /// buffer" reads exactly like a pause and takes the same flattening path.
    /// </summary>
    /// <param name="displayedLevels">已经跟随过的显示值。/ The followed displayed levels.</param>
    /// <param name="bandCount">当前段数；只统计这么多项。/ The current band count; only that many entries are inspected.</param>
    public static bool IsSilent(ReadOnlySpan<float> displayedLevels, int bandCount)
    {
        var count = Math.Min(Math.Max(bandCount, 0), displayedLevels.Length);
        for (var index = 0; index < count; index++)
        {
            var level = displayedLevels[index];
            if (float.IsFinite(level) && Math.Abs(level) > SpectrumPresentationPolicy.DisplayedLevelTolerance)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 频谱内容区的基准尺寸（DIP，未乘缩放）：宽度由柱数决定，高度取设置里的内容区高度。
    ///
    /// **与样式无关**：四种样式共用同一个内容区宽度，因此切换样式时文字槽的宽度不会跳变（控制器第 5 条）。
    /// The spectrum content area's baseline size in DIP, before scaling: the width follows the band count and the height comes from the settings.
    ///
    /// **Independent of the style**: all four styles share one content width, so switching styles never makes the text slot's width jump (the
    /// controller's item five).
    /// </summary>
    /// <param name="settings">频谱组件设置（先归一化）。/ The spectrum component settings, normalized first.</param>
    public static Size ResolveContentSizeDip(SpectrumComponentSettings settings)
    {
        var normalized = settings.Normalize();
        return new Size(
            SpectrumPresentationPolicy.CalculateContentWidthDip(normalized.BandCount),
            SpectrumPresentationPolicy.ResolveContentHeightDip(normalized));
    }

    /// <summary>
    /// 压平后那条静止细线的高度（DIP）。约 1 DIP 是"一条线"与"一根柱子"的分界：再粗就会读成静音的柱状图残根，
    /// 而任务书 §4.4 要的是**细线**而不是隐藏、也不是冻结最后一帧。
    /// The height of the stationary line a flattened waveform becomes, in DIP. About one DIP is where a line stops reading as a bar's silent
    /// stub, and task book §4.4 asks for a **line** rather than a hidden shape or a frozen last frame.
    /// </summary>
    public const double FlatLineHeightDip = 1;

    /// <summary>
    /// 一个采样周期有多少毫秒（<c>1000 / RefreshRateHz</c>）；刷新率越界时先夹到持久化区间，因此不会除以零。
    /// How many milliseconds one sampling period lasts (<c>1000 / RefreshRateHz</c>); an out-of-range rate clamps into the persisted range first,
    /// so no division by zero can happen.
    /// </summary>
    /// <param name="refreshRateHz">设置里的刷新率（Hz）。/ The configured refresh rate in hertz.</param>
    public static double ResolveSampleIntervalMilliseconds(int refreshRateHz) =>
        1000.0 / Math.Clamp(
            refreshRateHz,
            SpectrumComponentSettings.MinimumRefreshRateHz,
            SpectrumComponentSettings.MaximumRefreshRateHz);

    /// <summary>
    /// 把本帧耗时累加进采样累加器，并**封顶在一个周期上**。
    ///
    /// 封顶是必要的：掉帧、系统睡眠或进程被挂起会让一次累加进来好几秒，若原样保留，恢复后的接下来几帧会帧帧都去采样
    /// （看起来就是波形突然连跳几下）。
    /// Accumulates this frame's elapsed time and **caps the total at one period**.
    ///
    /// The cap matters: a dropped frame, system sleep, or a suspended process can add several seconds at once, and keeping that backlog would make
    /// the next several frames sample every single frame, which reads as the waveform jumping a few times in a row.
    /// </summary>
    /// <param name="accumulatedMilliseconds">上一帧的累加值。/ The accumulator from the previous frame.</param>
    /// <param name="elapsedMilliseconds">本帧真实经过的毫秒数（来自合成器）。/ This frame's real elapsed milliseconds, from the compositor.</param>
    /// <param name="refreshRateHz">设置里的刷新率（Hz）。/ The configured refresh rate in hertz.</param>
    public static double Accumulate(double accumulatedMilliseconds, double elapsedMilliseconds, int refreshRateHz)
    {
        var accumulated = double.IsFinite(accumulatedMilliseconds) && accumulatedMilliseconds > 0
            ? accumulatedMilliseconds
            : 0;
        var elapsed = double.IsFinite(elapsedMilliseconds) && elapsedMilliseconds > 0
            ? elapsedMilliseconds
            : 0;

        return Math.Min(accumulated + elapsed, ResolveSampleIntervalMilliseconds(refreshRateHz));
    }

    /// <summary>
    /// 累加值够一个采样周期了没有；够则调用方采一次并把累加器清零。
    /// Whether the accumulator has reached one sampling period; when it has, the caller samples once and clears the accumulator.
    /// </summary>
    /// <param name="accumulatedMilliseconds">当前累加值。/ The current accumulator.</param>
    /// <param name="refreshRateHz">设置里的刷新率（Hz）。/ The configured refresh rate in hertz.</param>
    public static bool ShouldSample(double accumulatedMilliseconds, int refreshRateHz) =>
        double.IsFinite(accumulatedMilliseconds) &&
        accumulatedMilliseconds >= ResolveSampleIntervalMilliseconds(refreshRateHz);

    /// <summary>
    /// 没有显示值时该画什么：暂停、断连（<c>IsPlaying</c> 为假）、采集不可用（<c>GetSpectrum</c> 返回 false），
    /// 以及**播放中但缓冲全是 0**（控制器第 ② 条裁定）四种输入都走这一条路。
    /// What to draw with no displayed level: a pause, a disconnection (<c>IsPlaying</c> false), an unusable capture (<c>GetSpectrum</c> answering false),
    /// and **playing with an all-zero buffer** (the controller's ruling two) all take this one path.
    ///
    /// 四种输入合并成一条判定，是为了让"断连时显示细线"与"暂停时压平"不可能各写一份而慢慢分叉；任务书 §4.4 对前两者的要求完全一致，
    /// 而第三、第四种对用户而言是同一个观感（没有律动），因此按同一语义补齐。
    /// The four inputs are one decision so that "show a line while disconnected" and "flatten while paused" can never drift apart into two
    /// implementations; task book §4.4 asks exactly the same thing of the first two, and the third and fourth read to the user as the same thing — no
    /// animation — so they are filled in under the same semantics.
    /// </summary>
    /// <param name="playing">当前媒体会话是否正在播放。/ Whether the media session is playing.</param>
    /// <param name="captureAvailable">最近一次采集是否可用。/ Whether the most recent capture was usable.</param>
    public static bool ShouldFlatten(bool playing, bool captureAvailable) => !playing || !captureAvailable;

    /// <summary>
    /// 波形列宿主的尺寸（DIP）= 内容区基准尺寸 × 缩放。
    ///
    /// 列宽与列高都是固定 DIP，只能由代码写（XAML 里写的 42×22 只在 11 段、内容区 22 DIP、缩放 1 时对），因此它们必须**每次从设置解算**。
    /// 使用者把结果写进宿主容器，再让内容层用同一个缩放系数做 RenderTransform——几何本身始终留在内容区的局部坐标里，
    /// 缩放不重算任何一点。
    /// The waveform column host's size in DIP: the baseline content size times the scale.
    ///
    /// Both the column's width and its height are fixed DIP that only code can write (the 42×22 in XAML is only correct at eleven bands, a
    /// twenty-two DIP content height, and scale one), so they have to be **solved from the settings every time**. The caller writes the result into
    /// the host container and has the content layer apply the same scale as a RenderTransform, which keeps the geometry in the content area's local
    /// coordinates and never recomputes a point for a scale.
    /// </summary>
    /// <param name="settings">频谱组件设置（先归一化：越界值夹取、非法的内容区高度回落到默认）。/ The spectrum component settings, normalized first: out-of-range values clamp and an invalid content height falls back to the default.</param>
    /// <param name="scale">灵动岛的缩放系数；非有限或非正数按 1（与 <c>CapsuleIslandMetrics</c> 同一规则）。/ The island's scale factor; a non-finite or non-positive value counts as one, the same rule CapsuleIslandMetrics uses.</param>
    public static Size ResolveHostSizeDip(SpectrumComponentSettings settings, double scale)
    {
        var content = ResolveContentSizeDip(settings);
        var factor = double.IsFinite(scale) && scale > 0 ? scale : 1;
        return new Size(content.Width * factor, content.Height * factor);
    }

    /// <summary>
    /// 压平后那条细线的几何（频谱内容区的局部坐标：X 相对内容区左缘、Y 相对上缘，与
    /// <see cref="SpectrumPresentationPolicy.CreateWaveformOutline"/> 同一坐标系）。
    ///
    /// 它就是一条横向铺满、纵向居中、约 1 DIP 高的矩形，因此**与采样值无关**：暂停时每帧算出同一个矩形，多帧像素因此完全一致
    /// （验收判据"暂停后多帧 hash 相同"），恢复播放后 <c>CreateWaveformOutline</c> 的轮廓再把这条线换成真正的起伏。
    /// The flattened line's geometry, in the spectrum content area's local coordinates (X from the content's left edge and Y from its top, the same
    /// space <see cref="SpectrumPresentationPolicy.CreateWaveformOutline"/> uses).
    ///
    /// It is one rectangle spanning the content width, vertically centred, about one DIP tall, and therefore **independent of the samples**: while
    /// paused every frame resolves to the same rectangle, which is what makes the pixels identical across frames (the acceptance criterion
    /// "identical hashes after a pause"), and on resumption the outline replaces the line with a real waveform again.
    /// </summary>
    /// <param name="contentWidthDip">频谱内容区宽度（DIP）；不可用时按 0（画不出东西，而不是画出错的东西）。/ The spectrum content width in DIP; unusable values count as zero, painting nothing rather than something wrong.</param>
    /// <param name="contentHeightDip">频谱内容区高度（DIP）。/ The spectrum content height in DIP.</param>
    public static Rect ResolveFlatLineRect(double contentWidthDip, double contentHeightDip)
    {
        var width = double.IsFinite(contentWidthDip) && contentWidthDip > 0 ? contentWidthDip : 0;
        var height = double.IsFinite(contentHeightDip) && contentHeightDip > 0 ? contentHeightDip : 0;
        var lineHeight = Math.Min(FlatLineHeightDip, height);
        return new Rect(0, Math.Max(0, (height - lineHeight) / 2), width, lineHeight);
    }
}
