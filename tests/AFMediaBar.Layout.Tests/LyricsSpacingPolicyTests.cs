using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词间距纯策略：行距的高度夹取（绝不超出任务栏文字区）与字距的插空、分隔符选择。
/// Pure lyric-spacing policy: height clamping for the line gap (never exceeding the taskbar's media-text area) plus the character
/// spacing insertion and separator selection.
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
        const string separator = "\u200A";
        Assert.AreEqual("你\u200A好\u200A世\u200A界", LyricsSpacingPolicy.ApplyCharacterSpacing("你好世界", separator));
        // 中英交界插一次、中文之间插一次；英文单词内部保持原样。
        // One insertion at the CJK/Latin boundary and one between the CJK characters; Latin words stay intact.
        Assert.AreEqual("hello\u200A你\u200A好", LyricsSpacingPolicy.ApplyCharacterSpacing("hello你好", separator));
        Assert.AreEqual("hello world 123", LyricsSpacingPolicy.ApplyCharacterSpacing("hello world 123", separator));
        // 已有空白的两侧不插，避免叠成双份空隙；全角标点属于 CJK。
        // Existing whitespace suppresses insertion on both sides so gaps never double up; full-width punctuation counts as CJK.
        Assert.AreEqual("你 好", LyricsSpacingPolicy.ApplyCharacterSpacing("你 好", separator));
        Assert.AreEqual("你\u200A，\u200A好", LyricsSpacingPolicy.ApplyCharacterSpacing("你，好", separator));    }

    [TestMethod]
    public void CharacterSpacingHandlesEmptyAndSingleCharacterTexts()
    {
        Assert.AreEqual(string.Empty, LyricsSpacingPolicy.ApplyCharacterSpacing(null, "\u200A"));
        Assert.AreEqual(string.Empty, LyricsSpacingPolicy.ApplyCharacterSpacing(string.Empty, "\u200A"));
        Assert.AreEqual("你", LyricsSpacingPolicy.ApplyCharacterSpacing("你", "\u200A"));
        Assert.AreEqual("你好", LyricsSpacingPolicy.ApplyCharacterSpacing("你好", string.Empty));
        Assert.AreEqual("你好", LyricsSpacingPolicy.ApplyCharacterSpacing("你好", null));
    }

    [TestMethod]
    public void SeparatorPicksTheSmallestWidthNotBelowTheTarget()
    {
        // 候选宽度与 SeparatorCandidates 顺序一一对应（hair → thin → four-per-em → three-per-em → en）。
        // Candidate widths match SeparatorCandidates in order (hair, thin, four-per-em, three-per-em, en).
        double[] widths = [0.5, 1.5, 3.0, 4.0, 5.0];
        Assert.AreEqual(LyricsSpacingPolicy.SeparatorCandidates[0], LyricsSpacingPolicy.ResolveSeparator(0.2, widths));
        Assert.AreEqual(LyricsSpacingPolicy.SeparatorCandidates[1], LyricsSpacingPolicy.ResolveSeparator(1.0, widths));
        Assert.AreEqual(LyricsSpacingPolicy.SeparatorCandidates[2], LyricsSpacingPolicy.ResolveSeparator(2.5, widths));
        Assert.AreEqual(LyricsSpacingPolicy.SeparatorCandidates[4], LyricsSpacingPolicy.ResolveSeparator(4.5, widths));
    }

    [TestMethod]
    public void SeparatorSelectionIsMonotoneAcrossTheWholeRange()
    {
        double[] widths = [0.5, 1.5, 3.0, 4.0, 5.0];
        var lastIndex = -1;
        for (var target = 0.2; target <= 6.0; target += 0.2)
        {
            var separator = LyricsSpacingPolicy.ResolveSeparator(target, widths);
            Assert.IsNotNull(separator);
            var index = LyricsSpacingPolicy.SeparatorCandidates.ToList().IndexOf(separator);
            Assert.IsTrue(index >= lastIndex, $"目标 {target:0.0} 选出的分隔符不能比上一档更窄。");
            lastIndex = index;
        }
    }

    [TestMethod]
    public void SeparatorFallsBackToTheWidestOrToNothing()
    {
        // 全部候选都小于目标：取最宽者，而不是永不命中。
        // Every candidate is below the target: the widest wins instead of nothing ever matching.
        Assert.AreEqual(
            LyricsSpacingPolicy.SeparatorCandidates[^1],
            LyricsSpacingPolicy.ResolveSeparator(9.0, [0.5, 1.5, 3.0, 4.0, 5.0]));
        // 字体缺全部字形（零宽）或输入不完整：退化不插。
        // The font lacks every glyph (zero width) or the input is incomplete: degrade to no spacing.
        Assert.IsNull(LyricsSpacingPolicy.ResolveSeparator(2.0, [0, 0, 0, 0, 0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSeparator(2.0, [1.0, 2.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSeparator(0, [1.0, 2.0, 3.0, 4.0, 5.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSeparator(double.NaN, [1.0, 2.0, 3.0, 4.0, 5.0]));
        Assert.IsNull(LyricsSpacingPolicy.ResolveSeparator(2.0, null));
    }
}
