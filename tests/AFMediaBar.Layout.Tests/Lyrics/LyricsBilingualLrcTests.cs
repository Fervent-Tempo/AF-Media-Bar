// 双语 LRC（原文、译文、音译占同时间戳的多行）的解析回归：合并成一行，顺序不能反。
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>双语 LRC 的成组行合并，以及不该触发合并的几种写法。</summary>
[TestClass]
public sealed class LyricsBilingualLrcTests
{
    [TestMethod]
    public void PairedLinesBecomeOneLineWithTranslation()
    {
        const string payload = """
            [ti:三原色]
            [00:07.09]どこかで途切れた物語
            [00:07.09]故事不知在哪里中断
            [00:11.05]僕らもう一度その先へ
            [00:11.05]我们再一次走向故事的延续
            """;
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("どこかで途切れた物語", document.Lines[0].Text);
        Assert.AreEqual("故事不知在哪里中断", document.Lines[0].Translation);
        Assert.AreEqual(7.09, document.Lines[0].Start, 0.001);
        Assert.AreEqual(11.05, document.Lines[0].End, 0.001);
        Assert.AreEqual("僕らもう一度その先へ", document.Lines[1].Text);
        Assert.AreEqual("我们再一次走向故事的延续", document.Lines[1].Translation);
    }

    [TestMethod]
    public void TripletsCarryTranslationAndRomanization()
    {
        const string payload = """
            [00:07.09]どこかで途切れた物語
            [00:07.09]故事不知在哪里中断
            [00:07.09]dokoka de togireta monogatari
            [00:11.05]僕らもう一度その先へ
            [00:11.05]我们再一次走向故事的延续
            [00:11.05]bokura mou ichido sono saki e
            """;
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("どこかで途切れた物語", document.Lines[0].Text);
        Assert.AreEqual("故事不知在哪里中断", document.Lines[0].Translation);
        Assert.AreEqual("dokoka de togireta monogatari", document.Lines[0].Romanization);
    }

    [TestMethod]
    public void RomanizationWrittenBeforeTranslationIsReordered()
    {
        // 罗马音是拉丁转写，译文不是；顺序反了要按这个特征换回来。
        const string payload = """
            [00:07.09]どこかで途切れた物語
            [00:07.09]dokoka de togireta monogatari
            [00:07.09]故事不知在哪里中断
            [00:11.05]僕らもう一度その先へ
            [00:11.05]bokura mou ichido sono saki e
            [00:11.05]我们再一次走向故事的延续
            """;
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("故事不知在哪里中断", document.Lines[0].Translation);
        Assert.AreEqual("dokoka de togireta monogatari", document.Lines[0].Romanization);
    }

    [TestMethod]
    public void OrdinaryLinesKeepNoTranslation()
    {
        const string payload = "[00:01.00]First\n[00:03.00]Second\n[00:05.00]Third";
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(3, document.Lines.Count);
        Assert.IsTrue(document.Lines.All(line => line.Translation is null && line.Romanization is null));
    }

    [TestMethod]
    public void OccasionalRepeatedTimestampStaysAsTwoLines()
    {
        // 十行里只有一组重复：更像长句拆行或对唱重叠，不能当译文。
        const string payload = """
            [00:01.00]A
            [00:03.00]B
            [00:05.00]C
            [00:07.00]D
            [00:07.00]D2
            [00:09.00]E
            [00:11.00]F
            [00:13.00]G
            [00:15.00]H
            [00:17.00]I
            """;
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(10, document.Lines.Count);
        Assert.IsTrue(document.Lines.All(line => line.Translation is null));
    }

    [TestMethod]
    public void FourLinesSharingOneTimestampStaySeparate()
    {
        // 四行及以上分不清各自归属，不猜。
        const string payload = """
            [00:01.00]原文
            [00:01.00]译文
            [00:01.00]音译
            [00:01.00]再来一行
            [00:03.00]次句
            """;
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(5, document.Lines.Count);
        Assert.IsTrue(document.Lines.All(line => line.Translation is null));
    }

    [TestMethod]
    public void TranslatedTextFromSeparateFileStillApplies()
    {
        const string payload = "[00:01.00]Hello\n[00:03.00]World";
        const string translation = "[00:01.00]你好\n[00:03.00]世界";
        var document = LyricsTextParser.Parse(payload, translation);
        Assert.AreEqual("你好", document.Lines[0].Translation);
        Assert.AreEqual("世界", document.Lines[1].Translation);
    }

    [TestMethod]
    public void SeparateTranslationFileWinsOverEmbedded()
    {
        // 同目录的 .tlyric 是显式指定的译文来源，比文件内嵌的那份优先。
        const string payload = "[00:07.09]原文\n[00:07.09]内嵌译文";
        const string translation = "[00:07.09]外部译文";
        var document = LyricsTextParser.Parse(payload, translation);
        Assert.AreEqual("原文", document.Lines[0].Text);
        Assert.AreEqual("外部译文", document.Lines[0].Translation);
    }

    [TestMethod]
    public void WindowsLineEndingsStillSplit()
    {
        var payload = "[00:01.00]原文一\r\n[00:01.00]译文一\r\n[00:03.00]原文二\r\n[00:03.00]译文二";
        var document = LyricsTextParser.Parse(payload);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("原文一", document.Lines[0].Text);
        Assert.AreEqual("译文一", document.Lines[0].Translation);
        Assert.AreEqual("原文二", document.Lines[1].Text);
        Assert.AreEqual("译文二", document.Lines[1].Translation);
    }

    [TestMethod]
    public void AssDialoguesSharingOneStartTimeMergeAsBilingual()
    {
        // 双语 LRC 转成 ASS 后就是两条起始时间相同的 Dialogue，转换工具不会给译文加任何标记。
        // 真实样例里原文那条的 End 甚至早于 Start（转换工具拿下一行的时间戳当结束时间），所以只认 Start。
        const string ass = """
            [Script Info]
            Title: 三原色

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:07.09,0:00:07.06,Default,,0,0,0,,どこかで途切れた物語
            Dialogue: 0,0:00:07.09,0:00:11.03,Default,,0,0,0,,故事不知在哪里中断
            Dialogue: 0,0:00:11.05,0:00:11.03,Default,,0,0,0,,僕らもう一度その先へ
            Dialogue: 0,0:00:11.05,0:00:15.00,Default,,0,0,0,,我们再一次走向故事的延续
            """;
        var lrc = SubtitleTextConverter.TryConvert(ass);
        var document = LyricsTextParser.Parse(lrc);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("どこかで途切れた物語", document.Lines[0].Text);
        Assert.AreEqual("故事不知在哪里中断", document.Lines[0].Translation);
        Assert.AreEqual("僕らもう一度その先へ", document.Lines[1].Text);
        Assert.AreEqual("我们再一次走向故事的延续", document.Lines[1].Translation);
    }

    [TestMethod]
    public void SrtCuesSharingOneStartTimeMergeAsBilingual()
    {
        const string srt = """
            1
            00:00:07,090 --> 00:00:11,050
            どこかで途切れた物語

            2
            00:00:07,090 --> 00:00:11,050
            故事不知在哪里中断

            3
            00:00:11,050 --> 00:00:15,020
            僕らもう一度その先へ

            4
            00:00:11,050 --> 00:00:15,020
            我们再一次走向故事的延续
            """;
        var lrc = SubtitleTextConverter.TryConvert(srt);
        var document = LyricsTextParser.Parse(lrc);
        Assert.AreEqual(2, document.Lines.Count);
        Assert.AreEqual("どこかで途切れた物語", document.Lines[0].Text);
        Assert.AreEqual("故事不知在哪里中断", document.Lines[0].Translation);
    }
}
