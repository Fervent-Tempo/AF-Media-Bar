using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词间距纯策略：行距的高度夹取（绝不超出任务栏文字区）、字距标记的插入、锚定空档的连续缩放与间距感知宽度。
/// Pure lyric-spacing policy: height clamping for the line gap (never exceeding the taskbar's media-text area), gap-marker insertion,
/// continuous scaling of the anchor spacer, and the spacing-aware width.
/// </summary>
[TestClass]
public sealed class LyricsSpacingPolicyTests
{
    [TestMethod]
    public void LineGapNeverConsumesTheMinimumLineHeights()
    {
        // 可用 40、每行至少 15：最多能空出 10；配置的 8 保留，配置的 12 被压到 10。
        // With 40 available and a 15 minimum per line the gap may use at most 10: a configured 8 stays, a configured 12 is compressed to 10.
        Assert.AreEqual(8, LyricsSpacingPolicy.ResolveLineGapDip(8, 40, 15), 1e-9);
        Assert.AreEqual(10, LyricsSpacingPolicy.ResolveLineGapDip(12, 40, 15), 1e-9);
    }

    [TestMethod]
    public void LineGapFallsBackToZeroWithoutRoomOrUsableInputs()
    {
        // 可用高度装不下两行最低高度：行距归零，两行紧邻（而不是把文字挤出任务栏）。
        // The available height cannot hold two minimum lines: the gap becomes zero and the lines sit next to each other instead of
        // pushing text out of the taskbar.
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineGapDip(8, 28, 15), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineGapDip(-4, 40, 15), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineGapDip(double.NaN, 40, 15), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineGapDip(8, 0, 15), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineGapDip(8, 40, 0), 1e-9);
    }

    [TestMethod]
    public void LineHeightSplitsWhatIsLeftAfterTheGap()
    {
        Assert.AreEqual(20, LyricsSpacingPolicy.ResolveLineHeightDip(40, 0), 1e-9);
        Assert.AreEqual(16, LyricsSpacingPolicy.ResolveLineHeightDip(40, 8), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineHeightDip(0, 8), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.ResolveLineHeightDip(double.NaN, 8), 1e-9);
    }

    [TestMethod]
    public void MinimumLineHeightAddsLegibilityOverhead()
    {
        Assert.AreEqual(15, LyricsSpacingPolicy.ResolveMinimumLineHeightDip(12), 1e-9);
        Assert.AreEqual(
            LyricsSpacingPolicy.MinimumLineHeightOverheadDip,
            LyricsSpacingPolicy.ResolveMinimumLineHeightDip(double.NaN),
            1e-9);
    }

    [TestMethod]
    public void CharacterSpacingIsInsertedOnlyWhereCjkParticipates()
    {
        var marker = LyricsSpacingPolicy.GapMarker;
        Assert.AreEqual($"你{marker}好{marker}世{marker}界", LyricsSpacingPolicy.ApplyCharacterSpacing("你好世界"));
        // 中英交界插一次、中文之间插一次；英文单词内部保持原样。
        // One insertion at the CJK/Latin boundary and one between the CJK characters; Latin words stay intact.
        Assert.AreEqual($"hello{marker}你{marker}好", LyricsSpacingPolicy.ApplyCharacterSpacing("hello你好"));
        Assert.AreEqual("hello world 123", LyricsSpacingPolicy.ApplyCharacterSpacing("hello world 123"));
        // 已有空白的两侧不插，避免叠成双份空隙；全角标点属于 CJK。
        // Existing whitespace suppresses insertion on both sides so gaps never double up; full-width punctuation counts as CJK.
        Assert.AreEqual("你 好", LyricsSpacingPolicy.ApplyCharacterSpacing("你 好"));
        Assert.AreEqual($"你{marker}，{marker}好", LyricsSpacingPolicy.ApplyCharacterSpacing("你，好"));
    }

    [TestMethod]
    public void CharacterSpacingHandlesEmptyAndSingleCharacterTexts()
    {
        Assert.AreEqual(string.Empty, LyricsSpacingPolicy.ApplyCharacterSpacing(null));
        Assert.AreEqual(string.Empty, LyricsSpacingPolicy.ApplyCharacterSpacing(string.Empty));
        Assert.AreEqual("你", LyricsSpacingPolicy.ApplyCharacterSpacing("你"));
    }

    [TestMethod]
    public void SpacerPicksTheSmallestCandidateAndScalesItExactlyToTheTarget()
    {
        // 候选宽度与 SpacerCandidates 一一对应（hair → six-per-em → thin → four-per-em → three-per-em → en）。
        // Candidate widths match SpacerCandidates one to one (hair, six-per-em, thin, four-per-em, three-per-em, en).
        double[] widths = [0.5, 1.5, 3.0, 4.0, 5.0, 6.0];

        var spacer = LyricsSpacingPolicy.ResolveSpacer(2.5, widths);
        Assert.IsNotNull(spacer);
        Assert.AreEqual(LyricsSpacingPolicy.SpacerCandidates[2], spacer.Value.Glyph);
        Assert.AreEqual(2.5 / 3.0, spacer.Value.Scale, 1e-9);

        // 每一档都精确落回目标：锚定字符宽度 × 缩放比 = 目标；有候选不小于目标时缩放比 ≤ 1（只缩不放，行高不变）。
        // Every step lands exactly on its target: anchor width times scale equals the target, and the scale is at most one whenever a
        // candidate at or above the target exists (scaling down only, so the line height never changes).
        foreach (var target in new[] { 0.1, 0.6, 1.0, 1.2, 2.0, 2.4, 4.5, 5.9 })
        {
            var resolved = LyricsSpacingPolicy.ResolveSpacer(target, widths);
            Assert.IsNotNull(resolved);
            var index = LyricsSpacingPolicy.SpacerCandidates.ToList().IndexOf(resolved.Value.Glyph);
            Assert.AreEqual(target, widths[index] * resolved.Value.Scale, 1e-9);
            Assert.IsTrue(resolved.Value.Scale <= 1 + 1e-12, $"目标 {target} 不应需要放大锚定字符。");
        }
    }

    [TestMethod]
    public void SpacerSelectionIsMonotoneAcrossTheSliderRange()
    {
        double[] widths = [0.5, 1.5, 3.0, 4.0, 5.0, 6.0];
        var lastIndex = -1;
        for (var target = 0.1; target <= 6.0; target += 0.1)
        {
            var spacer = LyricsSpacingPolicy.ResolveSpacer(target, widths);
            Assert.IsNotNull(spacer);
            var index = LyricsSpacingPolicy.SpacerCandidates.ToList().IndexOf(spacer.Value.Glyph);
            Assert.IsTrue(index >= lastIndex, $"目标 {target:0.0} 选出的锚定字符不能比上一档更窄。");
            lastIndex = index;
        }
    }

    [TestMethod]
    public void SpacerFallsBackToTheWidestOrToNothing()
    {
        // 全部候选都小于目标：用最宽者放大，空隙仍精确等于目标（异常字体才会遇到）。
        // Every candidate is below the target: the widest is scaled up and still lands exactly on the target (only unusual fonts).
        var spacer = LyricsSpacingPolicy.ResolveSpacer(9.0, [0.5, 1.5, 3.0, 4.0, 5.0, 6.0]);
        Assert.IsNotNull(spacer);
        Assert.AreEqual(LyricsSpacingPolicy.SpacerCandidates[^1], spacer.Value.Glyph);
        Assert.AreEqual(9.0 / 6.0, spacer.Value.Scale, 1e-9);

        // 字体缺全部字形（零宽）或输入不完整：退化不插。
        // The font lacks every glyph (zero width) or the input is incomplete: degrade to no spacing.
        Assert.IsNull(LyricsSpacingPolicy.ResolveSpacer(2.0, [0, 0, 0, 0, 0, 0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSpacer(2.0, [1.0, 2.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSpacer(0, [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSpacer(double.NaN, [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSpacer(2.0, null));
    }

    [TestMethod]
    public void SpacingAwareWidthIsSegmentsPlusExactGaps()
    {
        // 假测量：每字符 10 DIP，足以验证"分段 + 空格数 × 目标"的组合。
        // Fake measurement: ten DIP per character, enough to verify the "segments plus gaps times target" combination.
        static double Measure(string segment) => segment.Length * 10.0;

        Assert.AreEqual(20, LyricsSpacingPolicy.MeasureSpacingAware("ab", 2.0, Measure), 1e-9);

        var spaced = LyricsSpacingPolicy.ApplyCharacterSpacing("你好");
        Assert.AreEqual(1, LyricsSpacingPolicy.CountGaps(spaced));
        Assert.AreEqual(20 + 2.5, LyricsSpacingPolicy.MeasureSpacingAware(spaced, 2.5, Measure), 1e-9);

        // 没有标记、空串与"整串都是标记"三种边界。
        // Three edges: no markers, an empty string, and a string made of markers only.
        var marker = LyricsSpacingPolicy.GapMarker;
        Assert.AreEqual(30, LyricsSpacingPolicy.MeasureSpacingAware("abc", 4.0, Measure), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.MeasureSpacingAware(null, 2.0, Measure), 1e-9);
        Assert.AreEqual(0, LyricsSpacingPolicy.MeasureSpacingAware(string.Empty, 2.0, Measure), 1e-9);
        Assert.AreEqual(3 * 2.5, LyricsSpacingPolicy.MeasureSpacingAware(new string(marker, 3), 2.5, Measure), 1e-9);

        // 空隙为 0 时退化为普通测量：标记按普通字符计入（与"不生成空档 Run、直接渲染原串"一致；
        // 生产路径上空隙为 0 的串本来就不带标记）。
        // A zero gap degrades to the plain measurement, where markers count as ordinary characters — consistent with rendering the string
        // as-is without spacer runs (production strings carry no markers while spacing is off).
        Assert.AreEqual(30, LyricsSpacingPolicy.MeasureSpacingAware(spaced, 0, Measure), 1e-9);
        Assert.AreEqual(1, LyricsSpacingPolicy.CountGaps(spaced));
    }
}
