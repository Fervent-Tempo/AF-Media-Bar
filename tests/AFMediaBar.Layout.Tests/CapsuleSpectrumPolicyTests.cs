using System;
using System.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 胶囊波形的采样节流与暂停/无采集时的压平：采样周期由 <c>RefreshRateHz</c> 决定，暂停或无采集时波形压成一条居中的静止细线。
/// The capsule waveform's sampling throttle and its flattening while paused or without capture: the sampling period comes from
/// <c>RefreshRateHz</c>, and a pause or a missing capture flattens the waveform into one stationary line through the centre.
///
/// 节流做成纯函数是因为帧回调必须自己数时间：任务书红线 5 不许为波形新起 <c>DispatcherTimer</c>，唯一帧源是
/// <c>CompositionTarget.Rendering</c>，而它的帧间隔随显示器刷新率变化，因此"够一个采样周期了没有"必须能单独测。
/// The throttle is a pure function because the frame callback has to count the time itself: red line 5 forbids a new
/// <c>DispatcherTimer</c> for the waveform, the only frame source is <c>CompositionTarget.Rendering</c>, and its frame interval follows the
/// monitor refresh rate — so "has a whole sampling period elapsed" has to be testable on its own.
/// </summary>
[TestClass]
public sealed class CapsuleSpectrumPolicyTests
{
    /// <summary>
    /// 采样周期 = 1000 / 刷新率，并且刷新率越界时先夹到持久化区间（5–30 Hz），不会算出零周期或除零。
    /// The sampling period is a thousand over the refresh rate, and an out-of-range rate is clamped into the persisted range (five to thirty
    /// hertz) first, so no zero period and no division by zero can come out.
    /// </summary>
    [TestMethod]
    public void SampleIntervalFollowsTheConfiguredRefreshRate()
    {
        Assert.AreEqual(50, CapsuleSpectrumPolicy.ResolveSampleIntervalMilliseconds(20), 0.001);
        Assert.AreEqual(200, CapsuleSpectrumPolicy.ResolveSampleIntervalMilliseconds(5), 0.001);
        Assert.AreEqual(1000.0 / 30, CapsuleSpectrumPolicy.ResolveSampleIntervalMilliseconds(30), 0.001);
        // 越界值先夹取：0 Hz 会除以零，1000 Hz 会让每帧都采样。
        // Out-of-range values clamp first: zero hertz would divide by zero and a thousand hertz would sample every frame.
        Assert.AreEqual(200, CapsuleSpectrumPolicy.ResolveSampleIntervalMilliseconds(0), 0.001);
        Assert.AreEqual(1000.0 / 30, CapsuleSpectrumPolicy.ResolveSampleIntervalMilliseconds(1000), 0.001);
    }

    /// <summary>
    /// 累加器在每个采样周期上只放行一次，中间帧一律不再采样（这就是"在帧回调里自行节流"）。
    /// The accumulator lets exactly one sample through per period and none in between, which is what "throttle inside the frame callback" means.
    /// </summary>
    [TestMethod]
    public void AccumulatorFiresOncePerSamplingPeriod()
    {
        var accumulated = 0d;

        // 20 Hz ⇒ 50 ms 一个周期：16 ms 的帧要跑满四帧（64 ms，封顶到 50）才放行一次。
        // At twenty hertz a period is fifty milliseconds, so sixteen-millisecond frames need four of them to let one sample through.
        accumulated = CapsuleSpectrumPolicy.Accumulate(accumulated, 16, 20);
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldSample(accumulated, 20));
        accumulated = CapsuleSpectrumPolicy.Accumulate(accumulated, 16, 20);
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldSample(accumulated, 20));
        accumulated = CapsuleSpectrumPolicy.Accumulate(accumulated, 16, 20);
        Assert.AreEqual(48, accumulated, 0.001);
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldSample(accumulated, 20));

        accumulated = CapsuleSpectrumPolicy.Accumulate(accumulated, 16, 20);
        // 累加封顶在一个周期上：48 + 16 只记到 50。
        // The accumulation is capped at one period, so forty-eight plus sixteen only counts up to fifty.
        Assert.AreEqual(50, accumulated, 0.001);
        Assert.IsTrue(CapsuleSpectrumPolicy.ShouldSample(accumulated, 20));

        // 消费一帧（调用方清零）之后不再放行。
        // After the caller consumes it by clearing the accumulator, nothing fires again.
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldSample(0, 20));
    }

    /// <summary>
    /// 一次长停顿（掉帧、系统睡眠、进程被挂起）后只补采一次，不会攒出一串积压让恢复那一瞬间连采好几帧。
    /// A long stall — a dropped frame, system sleep, a suspended process — produces exactly one sample, never a backlog that would make the moment
    /// of resumption sample several times in a row.
    /// </summary>
    [TestMethod]
    public void AStallBacklogCollapsesIntoASingleSample()
    {
        var accumulated = CapsuleSpectrumPolicy.Accumulate(0, 5000, 20);
        Assert.AreEqual(50, accumulated, 0.001);
        Assert.IsTrue(CapsuleSpectrumPolicy.ShouldSample(accumulated, 20));
    }

    /// <summary>
    /// 非法的帧耗时既不会把累加器变成 NaN，也不会拖慢采样：它按 0 记。
    /// An invalid frame duration neither turns the accumulator into NaN nor slows sampling down: it counts as zero.
    /// </summary>
    [TestMethod]
    public void InvalidElapsedOrAccumulatedValuesCountAsZero()
    {
        // 累加值非法时按 0 起算，但本帧真实耗时要照常记进去（丢掉它会让掉帧那一帧平白少算一段时间）。
        // An invalid accumulator restarts from zero while this frame's real elapsed still counts — dropping it would silently lose time on the
        // very frame that produced the invalid value.
        Assert.AreEqual(16, CapsuleSpectrumPolicy.Accumulate(double.NaN, 16, 20), 0.001);
        Assert.AreEqual(0, CapsuleSpectrumPolicy.Accumulate(0, double.NaN, 20), 0.001);
        Assert.AreEqual(0, CapsuleSpectrumPolicy.Accumulate(double.NaN, double.PositiveInfinity, 20), 0.001);
        Assert.AreEqual(0, CapsuleSpectrumPolicy.Accumulate(-100, -100, 20), 0.001);
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldSample(double.NaN, 20));
    }

    /// <summary>
    /// 暂停或没有采集数据时压平：这两种情况走同一条路，因此"断连/无采集显示细线"与"暂停压平"不会各写一份。
    /// Flattening happens while paused or without capture: both take one path, so "disconnected or without capture shows a line" and "a pause
    /// flattens" are never two separate implementations.
    /// </summary>
    [TestMethod]
    public void FlattenHappensWithoutPlaybackOrWithoutCapture()
    {
        Assert.IsFalse(CapsuleSpectrumPolicy.ShouldFlatten(playing: true, captureAvailable: true));
        Assert.IsTrue(CapsuleSpectrumPolicy.ShouldFlatten(playing: false, captureAvailable: true));
        Assert.IsTrue(CapsuleSpectrumPolicy.ShouldFlatten(playing: true, captureAvailable: false));
        Assert.IsTrue(CapsuleSpectrumPolicy.ShouldFlatten(playing: false, captureAvailable: false));
    }

    /// <summary>
    /// 波形列宿主的尺寸由**频谱设置**决定：柱数决定宽度（柱宽与柱距固定）、内容区高度决定高度，两者都乘缩放系数。
    ///
    /// 这是"频谱调整不会应用到灵动岛上"那条报障的回归线：列宽与列高是固定 DIP，只能由代码写，因此它们必须能从设置解算出来，
    /// 而不是在窗口里按当时的设置算一次就再也不看设置。
    /// The waveform column host's size comes from the **spectrum settings**: the band count decides the width (bar width and gap are fixed) and
    /// the content height decides the height, both scaled.
    ///
    /// This is the regression line for the report that "spectrum adjustments do not reach the dynamic island": the column's width and height are
    /// fixed DIP that only code can write, so they have to be resolvable from the settings instead of being computed once from whatever the
    /// settings happened to say at that moment.
    /// </summary>
    [TestMethod]
    public void HostSizeFollowsTheBandCountAndTheContentHeight()
    {
        // 本机基线：11 段 ⇒ 内容宽 42 DIP；内容区高 22 DIP。
        // The baseline on this machine: eleven bands give a forty-two DIP content width and a twenty-two DIP content height.
        var eleven = new SpectrumComponentSettings(11, 20, 100) { ContentHeightDip = 22 };
        var baseline = CapsuleSpectrumPolicy.ResolveHostSizeDip(eleven, 1);
        Assert.AreEqual(42, baseline.Width, 0.001);
        Assert.AreEqual(22, baseline.Height, 0.001);

        // 缩放系数作用在内容区尺寸上（列宽是固定 DIP，缩放只能由代码写）。
        // The scale factor applies to the content size, because the column width is fixed DIP that only code can scale.
        var scaled = CapsuleSpectrumPolicy.ResolveHostSizeDip(eleven, 1.2889);
        Assert.AreEqual(42 * 1.2889, scaled.Width, 0.001);
        Assert.AreEqual(22 * 1.2889, scaled.Height, 0.001);

        // 段数变多 ⇒ 宿主变宽，宽度就是 CalculateContentWidthDip（柱宽与柱距固定，宽度只由段数决定）。
        // More bands widen the host, and the width is CalculateContentWidthDip: bar width and gap are fixed, so the width follows the count alone.
        var twentyFour = eleven with { BandCount = 24 };
        var wider = CapsuleSpectrumPolicy.ResolveHostSizeDip(twentyFour, 1);
        Assert.AreEqual(SpectrumPresentationPolicy.CalculateContentWidthDip(24), wider.Width, 0.001);
        Assert.IsTrue(wider.Width > baseline.Width, "24 段必须比 11 段宽 / twenty-four bands have to be wider than eleven");

        // **只改内容区高度也必须体现出来**：这正是报障里"改了设置岛没反应"最容易被漏掉的一半（段数没变，缓冲与点数都没变）。
        // **Changing only the content height has to show up too**: this is the half of the report that is easiest to miss, because the band count,
        // the buffers, and the point count are all unchanged.
        var taller = eleven with { ContentHeightDip = 30 };
        Assert.AreEqual(30, CapsuleSpectrumPolicy.ResolveHostSizeDip(taller, 1).Height, 0.001);
        Assert.AreNotEqual(
            baseline.Height,
            CapsuleSpectrumPolicy.ResolveHostSizeDip(taller, 1).Height,
            "内容区高度改了，宿主高度就必须跟着改 / a content-height change has to move the host's height");

        // 越界或非法的设置先归一化：不会给出 NaN、负数或零尺寸。
        // Out-of-range and invalid settings normalize first, so no NaN, negative, or zero size can come out.
        var wild = new SpectrumComponentSettings(999, 999, 999) { ContentHeightDip = double.NaN };
        var size = CapsuleSpectrumPolicy.ResolveHostSizeDip(wild, 1);
        Assert.IsTrue(size.Width > 0 && double.IsFinite(size.Width), $"宽度非法：{size.Width}");
        Assert.IsTrue(size.Height > 0 && double.IsFinite(size.Height), $"高度非法：{size.Height}");
        // 缩放系数非法时按 1（与 CapsuleIslandMetrics 的规则一致）。
        // An invalid scale counts as one, matching CapsuleIslandMetrics' rule.
        Assert.AreEqual(baseline, CapsuleSpectrumPolicy.ResolveHostSizeDip(eleven, double.NaN));
        Assert.AreEqual(baseline, CapsuleSpectrumPolicy.ResolveHostSizeDip(eleven, 0));
    }

    /// <summary>
    /// 岛的律动形态跟随 <c>SpectrumComponent.Style</c> 四态（用户裁定，覆盖任务书 §1 原裁定）。
    /// The island's animation shape follows the four <c>SpectrumComponent.Style</c> states (the user's ruling, superseding task book §1).
    /// </summary>
    [TestMethod]
    public void ShapeFollowsTheStyleInFourStates()
    {
        Assert.AreEqual(CapsuleSpectrumShape.Bars, CapsuleSpectrumPolicy.ResolveShape(SpectrumStyle.Bars));
        Assert.AreEqual(CapsuleSpectrumShape.Waveform, CapsuleSpectrumPolicy.ResolveShape(SpectrumStyle.Waveform));
        Assert.AreEqual(CapsuleSpectrumShape.MirroredBars, CapsuleSpectrumPolicy.ResolveShape(SpectrumStyle.MirroredBars));
        Assert.AreEqual(CapsuleSpectrumShape.PixelBars, CapsuleSpectrumPolicy.ResolveShape(SpectrumStyle.PixelBars));

        // 未定义的取值回落到柱状图：不抛异常，也不会留下一个空槽。
        // An undefined value falls back to bars: no exception and no empty slot.
        Assert.AreEqual(CapsuleSpectrumShape.Bars, CapsuleSpectrumPolicy.ResolveShape((SpectrumStyle)99));
    }

    /// <summary>
    /// 每种样式的"压平"形态（控制器裁定，沿用任务栏既有的静音语义）：波形 → 1 DIP 细线；柱状/对称柱 → 最小柱；像素柱 → 单个方块。
    /// Each style's flattened shape (the controller's ruling, reusing the taskbar's existing silence semantics): the waveform becomes a one-DIP
    /// line, bars and mirrored bars become the minimum bar, and the pixel chart lights a single block.
    /// </summary>
    [TestMethod]
    public void FlatShapeFollowsTheStyle()
    {
        Assert.AreEqual(CapsuleSpectrumFlatShape.Line, CapsuleSpectrumPolicy.ResolveFlatShape(CapsuleSpectrumShape.Waveform));
        Assert.AreEqual(CapsuleSpectrumFlatShape.MinimumBars, CapsuleSpectrumPolicy.ResolveFlatShape(CapsuleSpectrumShape.Bars));
        Assert.AreEqual(CapsuleSpectrumFlatShape.MinimumBars, CapsuleSpectrumPolicy.ResolveFlatShape(CapsuleSpectrumShape.MirroredBars));
        Assert.AreEqual(CapsuleSpectrumFlatShape.SinglePixel, CapsuleSpectrumPolicy.ResolveFlatShape(CapsuleSpectrumShape.PixelBars));
    }

    /// <summary>
    /// 四种样式共用**同一个**波形列尺寸：切样式时文字槽的宽度不许跳变（控制器第 5 条）。
    /// All four styles share **one** waveform column size, so switching styles never makes the text slot jump (the controller's item five).
    /// </summary>
    [TestMethod]
    public void HostSizeDoesNotDependOnTheStyle()
    {
        var expected = CapsuleSpectrumPolicy.ResolveHostSizeDip(
            new SpectrumComponentSettings(11, 20, 100) { Style = SpectrumStyle.Bars, ContentHeightDip = 22 },
            1.2889);

        foreach (var style in new[]
                 {
                     SpectrumStyle.Bars, SpectrumStyle.Waveform, SpectrumStyle.MirroredBars, SpectrumStyle.PixelBars
                 })
        {
            var size = CapsuleSpectrumPolicy.ResolveHostSizeDip(
                new SpectrumComponentSettings(11, 20, 100) { Style = style, ContentHeightDip = 22 },
                1.2889);
            Assert.AreEqual(expected, size, $"样式 {style} 改变了波形列尺寸 / style {style} changed the waveform column size");
        }
    }

    /// <summary>
    /// 零显示值（暂停、无采集，以及**播放中但没有音频包**）在四种样式下都必须给出**可见**的最小形状，而不是零面积的退化轮廓。
    ///
    /// 最后那一种正是控制器第 ② 条裁定：任务书只写了"暂停 / 无采集"两态，漏了"GetSpectrum 返回 true 但缓冲全是 0"这一态；
    /// 它对用户而言和暂停是同一个观感（没有律动），因此走同一条压平路径。波形的零振幅轮廓恰好是零面积，必须改画细线。
    /// Zero displayed levels (paused, no capture, and **playing with no audio packets**) all have to produce a **visible** minimum shape in every
    /// style rather than a zero-area degenerate outline.
    ///
    /// That last case is exactly the controller's ruling two: the task book only described "paused" and "no capture" and left out "GetSpectrum
    /// answers true with an all-zero buffer", which reads to the user as the same thing — no animation — so it takes the same flattening path. A
    /// waveform's zero-amplitude outline happens to have zero area, so it has to be drawn as the line instead.
    /// </summary>
    [TestMethod]
    public void SilentLevelsStillProduceTheMinimumVisibleShapePerStyle()
    {
        var settings = new SpectrumComponentSettings(11, 20, 400) { ContentHeightDip = 22 };
        var contentHeight = SpectrumPresentationPolicy.ResolveContentHeightDip(settings);
        var displayed = new float[11];
        Assert.IsTrue(CapsuleSpectrumPolicy.IsSilent(displayed, displayed.Length));

        // Waveform：1 DIP 居中细线。
        // Waveform: the one-DIP centred line.
        var line = CapsuleSpectrumPolicy.ResolveFlatLineRect(
            SpectrumPresentationPolicy.CalculateContentWidthDip(settings.BandCount),
            contentHeight);
        Assert.AreEqual(CapsuleSpectrumPolicy.FlatLineHeightDip, line.Height, 0.001);
        Assert.AreEqual(contentHeight / 2, line.Y + line.Height / 2, 0.001);

        // Bars / MirroredBars：最小柱高 3 DIP（ResolveBarScale 在 0 档给的就是它）。
        // Bars and mirrored bars: the three-DIP minimum bar, which is what ResolveBarScale answers at level zero.
        foreach (var style in new[] { SpectrumStyle.Bars, SpectrumStyle.MirroredBars })
        {
            var shape = CapsuleSpectrumPolicy.ResolveShape(style);
            Assert.AreEqual(CapsuleSpectrumFlatShape.MinimumBars, CapsuleSpectrumPolicy.ResolveFlatShape(shape));
            var scale = SpectrumPresentationPolicy.ResolveBarScale(0, settings.SensitivityPercent, contentHeight);
            Assert.AreEqual(
                SpectrumPresentationPolicy.MinimumBarHeightDip,
                scale * contentHeight,
                0.001,
                "静音时柱高必须正好是最小柱高 / a silent bar has to be exactly the minimum bar height");
        }

        // PixelBars：只点亮最下方 1 个方块。
        // Pixel bars: exactly one block stays lit at the bottom.
        Assert.AreEqual(CapsuleSpectrumFlatShape.SinglePixel, CapsuleSpectrumPolicy.ResolveFlatShape(CapsuleSpectrumShape.PixelBars));
        Assert.AreEqual(1, SpectrumPresentationPolicy.ResolveLitPixelCount(0, settings.SensitivityPercent, contentHeight));
    }

    /// <summary>
    /// 有任何一个频段非零就不算静音：否则一个很轻的音符也会被压成细线，而"静音"与"有声音但没有包"必须分得开。
    /// 非有限值（NaN/∞）按 0 处理，因此它只会让波形落到细线上，绝不会把 NaN 传进几何。
    /// Any non-zero band ends the silent state: otherwise a very quiet note would flatten into the line too, and "silence" has to stay distinguishable
    /// from "playing with no packets". A non-finite value (NaN or infinity) counts as zero, so it can only put the waveform on its line and can never
    /// feed NaN into the geometry.
    /// </summary>
    [TestMethod]
    public void AnyNonZeroBandEndsTheSilentState()
    {
        var bands = new float[11];
        bands[3] = 0.02f;
        Assert.IsFalse(CapsuleSpectrumPolicy.IsSilent(bands, bands.Length));

        // 只统计传入的段数：第 3 段之外的非零值不算数。
        // Only the requested band count is inspected, so the non-zero value beyond it does not count.
        Assert.IsTrue(CapsuleSpectrumPolicy.IsSilent(bands, 3));

        Assert.IsTrue(CapsuleSpectrumPolicy.IsSilent([], 0));
        Assert.IsTrue(CapsuleSpectrumPolicy.IsSilent([float.NaN, float.PositiveInfinity, 0f], 3));
        Assert.IsFalse(CapsuleSpectrumPolicy.IsSilent([0f, 0f, 0.5f], 3));
    }

    /// <summary>
    /// 压平后的形状是一条**居中**的细线：高度约 1 DIP，纵向中心落在内容区中线，横向铺满内容区宽度。
    /// The flattened shape is one **centred** line: about one DIP tall, vertically centred in the content area, and spanning its full width.
    /// </summary>
    [TestMethod]
    public void FlatLineIsCentredAndAboutOneDipTall()
    {
        // 本机：11 段 ⇒ 内容宽 42 DIP，内容高 22 DIP。
        // On this machine: eleven bands give a forty-two DIP content width and a twenty-two DIP content height.
        var rect = CapsuleSpectrumPolicy.ResolveFlatLineRect(42, 22);

        Assert.AreEqual(0, rect.X, 0.001);
        Assert.AreEqual(42, rect.Width, 0.001);
        Assert.AreEqual(1, rect.Height, 0.001);
        // 居中：上下留白相同，细线的中线正好在内容区中线上。
        // Centred: the margins above and below match, and the line's centre is the content area's centre.
        Assert.AreEqual(10.5, rect.Y, 0.001);
        Assert.AreEqual(22 / 2.0, rect.Y + rect.Height / 2, 0.001);
    }

    /// <summary>
    /// 压平后的形状是**静止**的：同样的设置每帧算出完全相同的矩形，因此暂停时多帧像素完全一致（验收判据之一）。
    /// The flattened shape is **stationary**: the same settings resolve to exactly the same rectangle every frame, which is what makes the
    /// pixels identical across frames while paused — one of the acceptance criteria.
    /// </summary>
    [TestMethod]
    public void FlatLineIsIdenticalOnEveryFrame()
    {
        var first = CapsuleSpectrumPolicy.ResolveFlatLineRect(42, 22);
        for (var frame = 0; frame < 30; frame++)
            Assert.AreEqual(first, CapsuleSpectrumPolicy.ResolveFlatLineRect(42, 22));
    }

    /// <summary>
    /// 尺寸不可用（布局尚未跑完、设置异常）时给出零高度而不是 NaN 或负高度：调用方只是画不出东西，不会崩形。
    /// An unusable size — the layout has not run yet, the settings are odd — yields a zero height rather than NaN or a negative one: the caller
    /// merely paints nothing instead of breaking the shape.
    /// </summary>
    [TestMethod]
    public void FlatLineSurvivesUnusableGeometry()
    {
        foreach (var (width, height) in new[]
                 {
                     (double.NaN, double.NaN),
                     (0d, 0d),
                     (-5d, -5d),
                     (42d, 0.5),
                     (double.PositiveInfinity, double.PositiveInfinity)
                 })
        {
            var rect = CapsuleSpectrumPolicy.ResolveFlatLineRect(width, height);
            Assert.IsTrue(double.IsFinite(rect.X), "x 必须是有限值 / x has to be finite");
            Assert.IsTrue(double.IsFinite(rect.Y), "y 必须是有限值 / y has to be finite");
            Assert.IsTrue(double.IsFinite(rect.Width), "宽度必须是有限值 / the width has to be finite");
            Assert.IsTrue(double.IsFinite(rect.Height), "高度必须是有限值 / the height has to be finite");
            Assert.IsTrue(rect.Width >= 0 && rect.Height >= 0, "尺寸不允许为负 / the size never goes negative");
        }
    }
}
