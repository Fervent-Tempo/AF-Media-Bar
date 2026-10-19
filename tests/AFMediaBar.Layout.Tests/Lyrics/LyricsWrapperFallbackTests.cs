// 验证损坏包装不吞掉混合载荷中的正文、译文或音译，全部使用本地文本。
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>损坏歌词包装与译文、音译扫描回退的回归验证。</summary>
[TestClass]
public sealed class LyricsWrapperFallbackTests
{
    [DataTestMethod]
    [DataRow("[00:01.00]Hello", LyricsRawTypes.Lrc)]
    [DataRow("[1000,1000](1000,500,0)Hello", LyricsRawTypes.Yrc)]
    [DataRow("[1000,1000]Hello(1000,500)", LyricsRawTypes.Qrc)]
    public void BrokenQrcMarkerStillScansValidLines(string line, LyricsRawTypes type)
    {
        var payload = "<Lyric_1 LyricContent=\"unterminated\n" + line;
        Assert.AreEqual(type, LyricsFormatDetector.Detect(payload));
        var document = LyricsTextParser.Parse(payload, filterInfoLines: false);
        Assert.AreEqual(1, document.Lines.Count);
        Assert.AreEqual("Hello", document.Lines[0].Text);
        Assert.AreEqual(1d, document.Lines[0].Start);
    }

    [TestMethod]
    public void ValidQrcWrapperPreservesNewlinesAndEntities()
    {
        const string text = "<Qrc><Lyric_1 LyricContent=\"[1000,1000]A&amp;B(1000,500)\n[2000,1000]Next(2000,500)\"/></Qrc>";
        Assert.AreEqual(LyricsRawTypes.QrcFull, LyricsFormatDetector.Detect(text));
        var document = LyricsTextParser.Parse(text, filterInfoLines: false);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("A&B", document.Lines[0].Text);
    }

    [TestMethod]
    public void BrokenWrappersInTranslationsUseTheSameFallback()
    {
        const string marker = "<Lyric_1 LyricContent=\"broken\n";
        var document = LyricsTextParser.Parse("[00:01.00]Hello",
            marker + "[00:01.00]译文", marker + "[00:01.00]Roman", filterInfoLines: false);
        Assert.AreEqual("译文", document.Lines[0].Translation);
        Assert.AreEqual("Roman", document.Lines[0].Romanization);
        Assert.AreEqual(0, LyricsTextParser.Parse(marker).Lines.Count);
    }

    [TestMethod]
    public void EmptyValidWrapperCanFallBackOnceToEmbeddedTimestampedLines()
    {
        const string text = "<Qrc><Lyric_1 LyricContent=\"\"/>\n[00:01.00]Hello\n</Qrc>";
        Assert.AreEqual(1, LyricsTextParser.Parse(text, filterInfoLines: false).Lines.Count);
    }
}
