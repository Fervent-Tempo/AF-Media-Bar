using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ExtraFeaturePolicyTests
{
    [TestMethod]
    public void SourceAllowListIsApplicationScopedAndCaseInsensitive()
    {
        Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("anything", SmtcSourceFilterSettings.Default));
        var enabled = new SmtcSourceFilterSettings(true, [" Chrome.EXE "]).Normalize();
        Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("chrome.exe", enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("msedge.exe", enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed(null, enabled));
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("chrome.exe", new SmtcSourceFilterSettings(true, [])));
    }

    [TestMethod]
    public void QuickLaunchNormalizationDropsInvalidAndDuplicateTargets()
    {
        var normalized = new QuickLaunchSettings([
            new QuickLaunchEntry("a", "One", QuickLaunchTargetKind.Executable, @"C:\\Music\\player.exe"),
            new QuickLaunchEntry("b", "Duplicate", QuickLaunchTargetKind.Executable, @"c:\\music\\PLAYER.exe"),
            new QuickLaunchEntry("c", "Invalid", QuickLaunchTargetKind.Executable, " ")
        ]).Normalize();
        Assert.AreEqual(1, normalized.Entries!.Count);
        Assert.AreEqual("One", normalized.Entries[0].DisplayName);
    }

    [TestMethod]
    public void ComponentSettingsClampAndPerformanceKeepsOneMetric()
    {
        // 柱数下限是 9：频谱宽度由柱数决定，更少的柱子会让「柱数决定尺寸」这条关系失去意义。
        // The bar-count minimum is nine: the spectrum width follows the count, so fewer bars would defeat the very
        // size relationship the count expresses.
        Assert.AreEqual(new SpectrumComponentSettings(9, 30, 400), new SpectrumComponentSettings(0, 99, 999).Normalize());
        Assert.AreEqual(
            SpectrumStyle.Bars,
            new SpectrumComponentSettings(9, 20, 100) { Style = (SpectrumStyle)99 }.Normalize().Style);
        var performance = new PerformanceComponentSettings([], 1, true).Normalize();
        CollectionAssert.AreEqual(new[] { MetricKind.SystemMemory }, performance.Metrics!.ToArray());
        Assert.AreEqual(500, performance.RefreshIntervalMilliseconds);

        // 采样间隔吸附到 0.5 秒网格再夹取，因此界面读数与滑杆位置永远一致。
        // The sampling interval snaps onto the 0.5-second grid before clamping, so the reading and the slider position
        // always agree.
        Assert.AreEqual(2500, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(2700));
        Assert.AreEqual(2000, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(1800));
        Assert.AreEqual(500, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(120));
        Assert.AreEqual(5000, PerformanceComponentSettings.SnapRefreshIntervalMilliseconds(90000));
        Assert.IsTrue(PerformanceComponentSettings.Default.OpenTaskManagerOnClick);
    }

    [TestMethod]
    public void SpectrumBandEdgesStayOrderedAndSpanTheAudibleRange()
    {
        foreach (var bandCount in new[]
                 {
                     SpectrumComponentSettings.MinimumBandCount,
                     12,
                     SpectrumComponentSettings.MaximumBandCount
                 })
        {
            var edges = SpectrumBandPolicy.CreateBandEdges(bandCount);
            Assert.AreEqual(bandCount + 1, edges.Length);
            Assert.AreEqual(SpectrumBandPolicy.FirstBandEdgeHz, edges[0]);
            Assert.AreEqual(SpectrumBandPolicy.LastBandEdgeHz, edges[^1]);
            for (var index = 1; index < edges.Length; index++)
            {
                Assert.IsTrue(edges[index] > edges[index - 1], $"{bandCount} 柱时频段边界必须严格递增。");
            }
        }

        Assert.AreEqual(
            SpectrumComponentSettings.MinimumBandCount,
            SpectrumBandPolicy.CreateBandEdges(1).Length - 1,
            "越界的柱数必须先被夹取。");
    }

    [TestMethod]
    public void SpectrumWidthGrowsWithTheBarCountAndKeepsTheBarWidthFixed()
    {
        var nine = SpectrumPresentationPolicy.CalculateContentWidthDip(SpectrumComponentSettings.MinimumBandCount);
        var twentyFour = SpectrumPresentationPolicy.CalculateContentWidthDip(SpectrumComponentSettings.MaximumBandCount);

        Assert.AreEqual(9 * SpectrumPresentationPolicy.BarWidthDip + 8 * SpectrumPresentationPolicy.BarGapDip, nine, 0.001);
        Assert.AreEqual(24 * SpectrumPresentationPolicy.BarWidthDip + 23 * SpectrumPresentationPolicy.BarGapDip, twentyFour, 0.001);
        Assert.IsTrue(twentyFour > nine);
        Assert.AreEqual(
            nine + SpectrumPresentationPolicy.SurfacePaddingDip * 2,
            SpectrumPresentationPolicy.CalculateSurfaceWidthDip(SpectrumComponentSettings.MinimumBandCount),
            0.001);
        Assert.AreEqual(0, SpectrumPresentationPolicy.ResolveBarLeftDip(0));
        Assert.AreEqual(SpectrumPresentationPolicy.BarPitchDip, SpectrumPresentationPolicy.ResolveBarLeftDip(1));
    }

    [TestMethod]
    public void SpectrumBarAndPixelLevelsKeepAVisibleStubWhenSilent()
    {
        var height = SpectrumComponentSettings.DefaultContentHeightDip;
        var silent = SpectrumPresentationPolicy.ResolveBarScale(0, 100, height);
        var loudest = SpectrumPresentationPolicy.ResolveBarScale(1, 100, height);
        Assert.AreEqual(SpectrumPresentationPolicy.MinimumBarHeightDip / height, silent, 0.0001);
        Assert.AreEqual(1, loudest, 0.0001);
        Assert.IsTrue(
            SpectrumPresentationPolicy.ResolveBarScale(0.5f, 400, height) >=
            SpectrumPresentationPolicy.ResolveBarScale(0.5f, 100, height));

        var dots = SpectrumPresentationPolicy.ResolvePixelDotCount(height);
        var columnHeight = SpectrumPresentationPolicy.ResolvePixelColumnHeightDip(height);
        Assert.AreEqual(1, SpectrumPresentationPolicy.ResolveLitPixelCount(0, 100, height));
        Assert.AreEqual(dots, SpectrumPresentationPolicy.ResolveLitPixelCount(1, 100, height));
        Assert.IsTrue(columnHeight <= height);
        var topPadding = (height - columnHeight) / 2;
        Assert.AreEqual(
            topPadding,
            SpectrumPresentationPolicy.ResolvePixelDotTopDip(dots - 1, height),
            0.001,
            "一列像素必须垂直居中：最上方方块的留白等于整列居中后的上下偏移。");
        Assert.AreEqual(
            height - topPadding,
            SpectrumPresentationPolicy.ResolvePixelDotTopDip(0, height) + SpectrumPresentationPolicy.PixelDotHeightDip,
            0.001);

        // 高度是设置项：更高的内容区给出更多方块与更高的柱子，而宽度仍然只由柱数决定。
        // The height is a setting: a taller content area yields more blocks and taller bars while the width still follows the bar count alone.
        var taller = SpectrumComponentSettings.MaximumContentHeightDip;
        Assert.IsTrue(SpectrumPresentationPolicy.ResolvePixelDotCount(taller) > dots);
        Assert.IsTrue(SpectrumPresentationPolicy.ResolvePixelColumnHeightDip(taller) > columnHeight);
        Assert.AreEqual(
            SpectrumPresentationPolicy.CalculateContentWidthDip(12),
            SpectrumPresentationPolicy.CalculateContentWidthDip(12));
    }

    [TestMethod]
    public void WaveformOutlineIsClosedAndSymmetricAboutTheCentre()
    {
        var height = SpectrumComponentSettings.DefaultContentHeightDip;
        var bands = new[] { 0f, 0.25f, 0.5f, 1f, 0.75f, 0.2f, 0.9f, 0.1f, 0.4f };
        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(bands, 100, height);

        Assert.IsTrue(outline.Length > bands.Length * 2, "波形必须在控制点之间插入细分点。");
        Assert.AreEqual(0, outline[0].X, 0.001);
        Assert.AreEqual(
            SpectrumPresentationPolicy.CalculateContentWidthDip(bands.Length),
            outline[(outline.Length / 2) - 1].X,
            0.001,
            "上缘必须从左边界一直画到右边界。");
        foreach (var point in outline)
        {
            Assert.IsTrue(point.Y >= 0 && point.Y <= height);
        }

        // 上缘自左向右、下缘镜像回左：任意序号的点与其镜像点必须关于中线严格对称。
        // The upper edge runs left to right and the lower edge mirrors back left, so a point and its mirror must be
        // exactly symmetric about the centre line.
        var half = outline.Length / 2;
        for (var index = 0; index < half; index++)
        {
            var lower = outline[half + (half - 1 - index)];
            Assert.AreEqual(outline[index].X, lower.X, 0.001);
            Assert.AreEqual(
                height,
                outline[index].Y + lower.Y,
                0.001,
                "波形上下两半必须关于垂直中线对称。");
        }

        Assert.AreEqual(0, SpectrumPresentationPolicy.CreateWaveformOutline([0.5f], 100, height).Length);
        Assert.IsTrue(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.Waveform));
        Assert.IsTrue(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.MirroredBars));
        Assert.IsFalse(SpectrumPresentationPolicy.IsSymmetric(SpectrumStyle.Bars));

        // 轮廓长度只由柱数决定：控制端据此一次分配点集、之后逐帧原地改写，因此这条契约必须锁住。
        // The outline length depends only on the bar count: the control allocates the point set once and rewrites it in place
        // every frame, so this contract has to be locked down.
        foreach (var bandCount in new[] { SpectrumComponentSettings.MinimumBandCount, 16, SpectrumComponentSettings.MaximumBandCount })
        {
            var samples = new float[bandCount];
            Assert.AreEqual(
                SpectrumPresentationPolicy.CalculateWaveformPointCount(bandCount),
                SpectrumPresentationPolicy.CreateWaveformOutline(samples, 100, height).Length);
        }
    }

    /// <summary>
    /// 柱状图与对称柱状图把时间跟随交给 WPF 的 ScaleY 动画，像素柱状图与波形的视觉来自量化取值与路径点，
    /// 只能按帧推进；漏掉分类会让某一种样式退回每秒跳二十次的状态。
    /// The bar styles delegate their follower to the WPF ScaleY animation while the pixel chart and the waveform derive their
    /// visuals from quantized levels and path points and must be advanced per frame; a missing classification would drop one
    /// style back to jumping twenty times a second.
    /// </summary>
    [TestMethod]
    public void FrameDrivenSmoothingAppliesOnlyToTheStylesWithoutAnimatableProperties()
    {
        Assert.IsTrue(SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(SpectrumStyle.PixelBars));
        Assert.IsTrue(SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(SpectrumStyle.Waveform));
        Assert.IsFalse(SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(SpectrumStyle.Bars));
        Assert.IsFalse(SpectrumPresentationPolicy.NeedsFrameDrivenSmoothing(SpectrumStyle.MirroredBars));
    }

    /// <summary>
    /// 逐帧跟随必须收敛、单调且帧率无关；另外它与柱状图那条路径在同一个采样周期里走完的进度必须接近，
    /// 否则四种样式看起来会是两个速度。
    /// The frame follower must converge, stay monotonic, and be frame-rate independent; on top of that its progress after one
    /// sampling cycle has to stay close to the bar path's, otherwise the four styles read as two different speeds.
    /// </summary>
    [TestMethod]
    public void DisplayedLevelFollowerConvergesMonotonicallyAndMatchesTheBarAnimationResponse()
    {
        var settle = TimeSpan.FromMilliseconds(120);
        var frame = TimeSpan.FromMilliseconds(16);

        var rising = 0f;
        var previous = rising;
        for (var index = 0; index < 60; index++)
        {
            rising = SpectrumPresentationPolicy.AdvanceDisplayedLevel(rising, 1f, frame, settle);
            Assert.IsTrue(rising >= previous, "跟随不得过冲或回退。");
            previous = rising;
        }

        Assert.AreEqual(1f, rising, SpectrumPresentationPolicy.DisplayedLevelTolerance, "跟随必须收敛到目标。");
        Assert.IsTrue(
            SpectrumPresentationPolicy.IsDisplayedLevelSettled(rising, 1f),
            "收敛后必须可以判定为已落定，否则逐帧计时器永远不会停。");

        // 帧率无关：一次 32 ms 的推进必须等于两次 16 ms 的推进（同一段真实时间给同一个结果）。
        var once = SpectrumPresentationPolicy.AdvanceDisplayedLevel(0f, 1f, TimeSpan.FromMilliseconds(32), settle);
        var twice = SpectrumPresentationPolicy.AdvanceDisplayedLevel(
            SpectrumPresentationPolicy.AdvanceDisplayedLevel(0f, 1f, frame, settle), 1f, frame, settle);
        Assert.AreEqual(once, twice, 0.001, "跟随必须与帧率无关。");

        // 对齐柱状图：120 ms 的 PowerEase(3, EaseOut) 在一个 50 ms 采样周期后走完 1-(1-50/120)^3。
        var barProgress = 1 - Math.Pow(1 - 50d / 120d, 3);
        var followerProgress = SpectrumPresentationPolicy.AdvanceDisplayedLevel(
            0f, 1f, TimeSpan.FromMilliseconds(50), settle);
        Assert.AreEqual(barProgress, followerProgress, 0.02, "像素与波形的时间响应必须对齐柱状图的缓出动画。");

        // 越界与非法目标先被夹取或归零，因此它们与合法边界值给出完全相同的推进结果。
        // Out-of-range and invalid targets are clamped or zeroed first, so they advance exactly like the legal boundary value.
        Assert.AreEqual(
            SpectrumPresentationPolicy.AdvanceDisplayedLevel(0f, 1f, frame, settle),
            SpectrumPresentationPolicy.AdvanceDisplayedLevel(0f, 5f, frame, settle),
            "大于 1 的目标必须先被夹取。");
        Assert.AreEqual(0f, SpectrumPresentationPolicy.AdvanceDisplayedLevel(0f, float.NaN, frame, settle));
        // 没有可用时间（首帧、动效关闭或时长退化）时必须直接落到目标，而不是把中间值留在屏幕上。
        // With no usable time (first frame, motion disabled, or a degenerate duration) the follower must land on the target
        // instead of leaving an intermediate value on screen.
        Assert.AreEqual(1f, SpectrumPresentationPolicy.AdvanceDisplayedLevel(0.4f, 1f, TimeSpan.Zero, settle));
        Assert.AreEqual(1f, SpectrumPresentationPolicy.AdvanceDisplayedLevel(0.4f, 1f, frame, TimeSpan.Zero));
        // 首次进入该样式时显示值还没有历史，必须从目标开始，而不是从 0 缓慢爬升。
        // On first entering the style there is no history, so the follower starts at the target rather than crawling up from zero.
        Assert.AreEqual(0.6f, SpectrumPresentationPolicy.AdvanceDisplayedLevel(float.NaN, 0.6f, frame, settle));
    }

    [TestMethod]
    public void DeferredSelectionWrapsAndMetricsAdvanceEveryThirdSample()
    {
        Assert.AreEqual(2, DeferredCircularSelection.Move(0, 120, 3));
        Assert.AreEqual(0, DeferredCircularSelection.Move(2, -120, 3));
        Assert.AreEqual(0, MetricPresentationPolicy.Advance(0, 2, 3));
        Assert.AreEqual(1, MetricPresentationPolicy.Advance(0, 3, 3));
    }

    [TestMethod]
    public void MetricLeaseStartsWhenAReplayedSnapshotMakesTheComponentVisibleWithoutRenewingEverySnapshot()
    {
        Assert.AreEqual(
            MetricSubscriptionTransition.Subscribe,
            MetricPresentationPolicy.ResolveSubscriptionTransition(
                shouldSubscribe: true,
                hasSubscription: false,
                forceRenew: false));
        Assert.AreEqual(
            MetricSubscriptionTransition.None,
            MetricPresentationPolicy.ResolveSubscriptionTransition(
                shouldSubscribe: true,
                hasSubscription: true,
                forceRenew: false));
        Assert.AreEqual(
            MetricSubscriptionTransition.Renew,
            MetricPresentationPolicy.ResolveSubscriptionTransition(
                shouldSubscribe: true,
                hasSubscription: true,
                forceRenew: true));
        Assert.AreEqual(
            MetricSubscriptionTransition.Unsubscribe,
            MetricPresentationPolicy.ResolveSubscriptionTransition(
                shouldSubscribe: false,
                hasSubscription: true,
                forceRenew: false));
    }

    [TestMethod]
    public void DisconnectedWidthStillReservesPerformanceComponent()
    {
        var width = TaskbarExperiencePolicy.CalculateWidth(
            0, 44, 38, 4, false, false, false, false, false,
            TaskbarInformationDensity.Balanced, double.PositiveInfinity, 12,
            performanceVisible: true, performanceWidth: 74);
        Assert.AreEqual(44 + 12 + 74 + 4, width, 0.001);
    }
}
