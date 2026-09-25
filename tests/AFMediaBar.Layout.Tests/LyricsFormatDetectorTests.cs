using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词格式识别的纯策略测试。
/// Pure policy tests for lyric format detection.
///
/// 这组断言同时锁住"本项目必须自己识别格式"这一决定：已引用的库版本无法识别这些输入。
/// These assertions also pin down the decision that this project detects formats itself: the referenced library version
/// cannot recognize these inputs.
/// </summary>
[TestClass]
public sealed class LyricsFormatDetectorTests
{
    [TestMethod]
    public void EachSupportedTextFormatIsRecognized()
    {
        Assert.AreEqual(LyricsRawTypes.Lrc, LyricsFormatDetector.Detect("[00:01.00]第一句\n[00:05.50]第二句"));
        Assert.AreEqual(LyricsRawTypes.Yrc, LyricsFormatDetector.Detect("[1000,2000](1000,500,0)你(1500,500,0)好"));
        Assert.AreEqual(LyricsRawTypes.Qrc, LyricsFormatDetector.Detect("[5000,1000]你(5000,400)好(5400,600)"));
        Assert.AreEqual(LyricsRawTypes.Krc, LyricsFormatDetector.Detect("[1000,2000]<0,500,0>你<500,500,0>好"));
        Assert.AreEqual(LyricsRawTypes.LyricifySyllable, LyricsFormatDetector.Detect("[0]你(1000,500)好(1500,500)"));
        Assert.AreEqual(LyricsRawTypes.LyricifyLines, LyricsFormatDetector.Detect("[1000,2000]第一句\n[5000,1500]第二句"));
    }

    [TestMethod]
    public void UnrecognizedAndEmptyInputStayUnknown()
    {
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect(null));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("   "));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("这不是歌词"));
        Assert.AreEqual(LyricsRawTypes.Unknown, LyricsFormatDetector.Detect("{\"unrelated\":true}"));
    }

    [TestMethod]
    public void MixedPayloadWithCreditJsonLinesIsStillRecognized()
    {
        // 网易云新版端点实测载荷：署名 JSON 行在前，逐字行在后。识别器不得因为开头是 { 就整首放弃。
        // The real payload from NetEase's new endpoint: credit JSON lines first, syllable lines after them. Detection must not
        // give up on the whole song merely because the first character is a brace.
        const string mixed =
            "{\"t\":0,\"c\":[{\"tx\":\"作词: \"},{\"tx\":\"Taylor Swift\"}]}\n" +
            "[120,2910](120,120,0)I (240,420,0)promise";

        Assert.AreEqual(LyricsRawTypes.Yrc, LyricsFormatDetector.Detect(mixed));
    }

    [TestMethod]
    public void XmlThatIsNotAWrapperStillFallsThroughToLineScanning()
    {
        Assert.AreEqual(
            LyricsRawTypes.Lrc,
            LyricsFormatDetector.Detect("<not-a-wrapper>\n[00:01.00]第一句歌词"));
    }

    [TestMethod]
    public void QrcFullXmlIsRecognizedAndUnwrapped()
    {
        const string xml =
            "<QrcInfos><LyricInfo><Lyric_1 LyricType=\"1\" LyricContent=\"[5000,1000]你(5000,400)好(5400,600)\" /></LyricInfo></QrcInfos>";

        Assert.AreEqual(LyricsRawTypes.QrcFull, LyricsFormatDetector.Detect(xml));
        Assert.AreEqual("[5000,1000]你(5000,400)好(5400,600)", LyricsFormatDetector.TryUnwrap(xml, LyricsRawTypes.QrcFull));
    }

    [TestMethod]
    public void QrcFullUnwrapKeepsTheNewlinesInsideTheAttribute()
    {
        // 回归（实测 QQ 音乐本地缓存）：LyricContent 属性里的字面换行若被 XML 属性值规范化折成空格，
        // 整首歌词会黏成一行、库只认出元数据头，最终一首有词的歌被判成未命中。
        // Regression (measured on the QQ Music local cache): when the literal newlines inside the LyricContent attribute are
        // folded into spaces by XML attribute-value normalization, the whole lyric glues into one line, the library reads the
        // metadata header only, and a track that does have lyrics ends up reported as a miss.
        const string xml =
            "<QrcInfos><LyricInfo><Lyric_1 LyricType=\"1\" LyricContent=\"[ti:Song]\n[ar:Artist]\n[1000,900]la(1000,400)la(1400,500)\n[2000,900]lo(2000,400)lo(2400,500)\" /></LyricInfo></QrcInfos>";

        Assert.AreEqual(LyricsRawTypes.QrcFull, LyricsFormatDetector.Detect(xml));
        var body = LyricsFormatDetector.TryUnwrap(xml, LyricsRawTypes.QrcFull);

        StringAssert.Contains(body, "\n");
        Assert.AreEqual(4, body!.Split('\n').Length);
    }

    [TestMethod]
    public void YrcFullJsonIsRecognizedAndUnwrapped()
    {
        const string json = "{\"yrc\":{\"lyric\":\"[1000,2000](1000,500,0)你(1500,500,0)好\"}}";

        Assert.AreEqual(LyricsRawTypes.YrcFull, LyricsFormatDetector.Detect(json));
        Assert.AreEqual("[1000,2000](1000,500,0)你(1500,500,0)好", LyricsFormatDetector.TryUnwrap(json, LyricsRawTypes.YrcFull));
    }

    [TestMethod]
    public void WrappedFormatsParseIntoLinesAfterUnwrapping()
    {
        var qrc = LyricsTextParser.Parse(
            "<QrcInfos><LyricInfo><Lyric_1 LyricType=\"1\" LyricContent=\"[5000,1000]你(5000,400)好(5400,600)\" /></LyricInfo></QrcInfos>",
            request: new LyricsRequest("Song", "Artist", "Album", 20, NetEaseSongId: null));

        Assert.AreEqual(1, qrc.Lines.Count);
        Assert.AreEqual("你好", qrc.Lines[0].Text);
        Assert.AreEqual(2, qrc.Lines[0].Words.Count);

        var yrc = LyricsTextParser.Parse(
            "{\"yrc\":{\"lyric\":\"[1000,2000](1000,500,0)你(1500,500,0)好\"}}",
            request: new LyricsRequest("Song", "Artist", "Album", 20, NetEaseSongId: null));

        Assert.AreEqual(1, yrc.Lines.Count);
        Assert.AreEqual("你好", yrc.Lines[0].Text);
        Assert.AreEqual(2, yrc.Lines[0].Words.Count);
    }
}
