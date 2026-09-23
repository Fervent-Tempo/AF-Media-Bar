using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsPresentationProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void LineSyncedLyricsNeverProduceWordScanProgress()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "first"),
            new LyricLine(10, 20, "second")
        ], position: 5);

        var frame = LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            LyricsSyllableHighlightEnabled = true
        }, Now);

        Assert.IsTrue(frame.IsVisible);
        Assert.IsNull(frame.WordScanProgress);
        Assert.AreEqual(0.5, frame.LineProgress, 0.0001);
    }

    [TestMethod]
    public void SyllableLyricsProduceRealWordScanProgress()
    {
        var line = new LyricLine(0, 10, "AB")
        {
            Words = [new LyricWord(0, 2, "A"), new LyricWord(2, 4, "B")]
        };
        var snapshot = Snapshot([line], position: 1);

        var frame = LyricsPresentationProjector.Project(snapshot, new AppSettings
        {
            LyricsEnabled = true,
            LyricsSyllableHighlightEnabled = true
        }, Now);

        Assert.AreEqual(0.25, frame.WordScanProgress!.Value, 0.0001);
    }

    [TestMethod]
    public void TranslationSelectionUsesTranslationPairLayout()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "原文") { Translation = "translation" },
            new LyricLine(10, 20, "下一句") { Translation = "next translation" }
        ], position: 2);
        var settings = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.Translation])
        };

        var frame = LyricsPresentationProjector.Project(snapshot, settings, Now);

        Assert.IsTrue(frame.TranslationMode);
        Assert.AreEqual("translation", frame.CurrentTranslation);
        Assert.AreEqual("next translation", frame.NextTranslation);
        Assert.AreEqual(string.Empty, frame.Next);
    }

    [TestMethod]
    public void NextLineSelectionUsesSingleLayout()
    {
        var snapshot = Snapshot([
            new LyricLine(0, 10, "first"),
            new LyricLine(10, 20, "second")
        ], position: 2);
        var settings = new AppSettings
        {
            LyricsEnabled = true,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings([LyricsSecondaryLineMode.NextLine])
        };

        var frame = LyricsPresentationProjector.Project(snapshot, settings, Now);

        Assert.IsFalse(frame.TranslationMode);
        Assert.AreEqual("second", frame.Next);
        Assert.AreEqual(string.Empty, frame.CurrentTranslation);
    }

    [TestMethod]
    public void DisabledLyricsProduceHiddenFrame()
    {
        var frame = LyricsPresentationProjector.Project(
            Snapshot([new LyricLine(0, 10, "first")], position: 2),
            new AppSettings { LyricsEnabled = false },
            Now);

        Assert.IsFalse(frame.IsVisible);
    }

    private static MediaSnapshot Snapshot(IReadOnlyList<LyricLine> lines, double position) => new(
        true,
        true,
        true,
        true,
        true,
        "title",
        "artist",
        "source",
        "Source",
        null,
        new LyricsResult("test", new LyricDocument(lines, LyricsSyncType.SyllableSynced, "test")),
        position,
        30,
        true,
        false,
        MediaRepeatMode.Off,
        1,
        Now);
}
