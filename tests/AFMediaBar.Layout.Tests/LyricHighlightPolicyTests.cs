using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>逐字擦亮进度的纯策略测试。 / Pure policy tests for syllable highlight progress.</summary>
[TestClass]
public sealed class LyricHighlightPolicyTests
{
    private static LyricLine WordLine() => new(10, 12, "你好世界")
    {
        Words =
        [
            new LyricWord(10, 10.5, "你"),
            new LyricWord(10.5, 11, "好"),
            new LyricWord(11, 11.5, "世"),
            new LyricWord(11.5, 12, "界")
        ]
    };

    [TestMethod]
    public void HighlightSwitchOnlyChangesTheVisualLayer()
    {
        var enabled = ResolvePresentationState(highlightEnabled: true);
        var disabled = ResolvePresentationState(highlightEnabled: false);

        Assert.IsTrue(enabled.AdvanceTimeline);
        Assert.IsTrue(disabled.AdvanceTimeline);
        Assert.IsTrue(enabled.ShowHighlight);
        Assert.IsFalse(disabled.ShowHighlight);
    }

    [TestMethod]
    public void HighContrastHidesHighlightWithoutChangingTimelineMotion()
    {
        var presentation = ResolvePresentationState(highlightEnabled: true, highContrast: true);

        Assert.IsTrue(presentation.AdvanceTimeline);
        Assert.IsFalse(presentation.ShowHighlight);
    }

    [TestMethod]
    public void LineWithoutWindowHasNoProgress()
    {
        var line = new LyricLine(5, 5, "degenerate");
        Assert.IsNull(LyricHighlightPolicy.ResolveProgress(line, 5));
    }

    [TestMethod]
    public void LineLevelLyricsHaveNoWordProgress()
    {
        var line = new LyricLine(10, 20, "line");

        Assert.IsNull(LyricHighlightPolicy.ResolveProgress(line, 10));
        Assert.IsNull(LyricHighlightPolicy.ResolveProgress(line, 15));
        Assert.IsNull(LyricHighlightPolicy.ResolveProgress(line, 20));
    }

    [TestMethod]
    public void WordProgressLandsOnSyllableBoundaries()
    {
        var line = WordLine();

        Assert.AreEqual(0d, LyricHighlightPolicy.ResolveProgress(line, 10)!.Value, 0.0001);
        // 第一个音节结束时正好是 1/4。
        // Finishing the first syllable lands exactly on a quarter.
        Assert.AreEqual(0.25d, LyricHighlightPolicy.ResolveProgress(line, 10.5)!.Value, 0.0001);
        Assert.AreEqual(0.5d, LyricHighlightPolicy.ResolveProgress(line, 11)!.Value, 0.0001);
        Assert.AreEqual(1d, LyricHighlightPolicy.ResolveProgress(line, 12)!.Value, 0.0001);
    }

    [TestMethod]
    public void InsideOneSyllableProgressIsInterpolated()
    {
        var progress = LyricHighlightPolicy.ResolveProgress(WordLine(), 10.25);

        Assert.IsNotNull(progress);
        Assert.AreEqual(0.125d, progress!.Value, 0.0001);
    }

    [TestMethod]
    public void SingleUsableSyllableUsesItsOwnTiming()
    {
        var line = new LyricLine(10, 20, "one")
        {
            Words = [new LyricWord(10, 11, "one")]
        };

        Assert.AreEqual(0.5d, LyricHighlightPolicy.ResolveProgress(line, 10.5)!.Value, 0.0001);
        Assert.AreEqual(1d, LyricHighlightPolicy.ResolveProgress(line, 15)!.Value, 0.0001);
    }

    [TestMethod]
    public void NonFinitePositionHasNoProgress()
    {
        Assert.IsNull(LyricHighlightPolicy.ResolveProgress(new LyricLine(10, 20, "line"), double.NaN));
    }

    [TestMethod]
    public void ClipWidthScalesAndGuardsInvalidInput()
    {
        Assert.AreEqual(50d, LyricHighlightPolicy.ResolveClipWidth(0.5, 100), 0.0001);
        Assert.AreEqual(100d, LyricHighlightPolicy.ResolveClipWidth(2, 100), 0.0001);
        Assert.AreEqual(0d, LyricHighlightPolicy.ResolveClipWidth(-1, 100), 0.0001);
        Assert.AreEqual(0d, LyricHighlightPolicy.ResolveClipWidth(0.5, 0), 0.0001);
        Assert.AreEqual(0d, LyricHighlightPolicy.ResolveClipWidth(0.5, double.NaN), 0.0001);
    }

    private static LyricHighlightPolicy.PresentationState ResolvePresentationState(
        bool highlightEnabled,
        bool highContrast = false) =>
        LyricHighlightPolicy.ResolvePresentationState(
            highlightEnabled,
            hasCurrentLine: true,
            connected: true,
            playing: true,
            lyricsVisible: true,
            controlVisible: true,
            isAdvancePruned: false,
            highContrast,
            useContinuousMotion: true);
}
