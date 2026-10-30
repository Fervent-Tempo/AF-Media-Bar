// 字幕转歌词：SRT / ASS 的时间轴归一化与标记清洗。
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SubtitleTextConverterTests
{
    [TestMethod]
    public void ConvertsSrtCuesAndDropsMarkup()
    {
        const string srt = """
            1
            00:00:01,500 --> 00:00:04,000
            <i>Hello</i>

            2
            00:00:05,250 --> 00:00:08,000
            first line
            second line
            """;

        var lrc = SubtitleTextConverter.TryConvert(srt);

        // 序号行不进正文，同一条字幕的多行合并成一行，HTML 标记去掉。
        Assert.AreEqual("[00:01.50]Hello\n[00:05.25]first line second line", lrc);
    }

    [TestMethod]
    public void ConvertsAssDialogueAndSkipsComments()
    {
        const string ass = """
            [Script Info]
            Title: test

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,not lyrics
            Dialogue: 0,0:00:01.50,0:00:04.00,Default,,0,0,0,,{\fad(200,200)}Hello\Nworld
            Dialogue: 0,0:01:02.34,0:00:05.00,Default,,0,0,0,,second, with comma
            """;

        var lrc = SubtitleTextConverter.TryConvert(ass);

        // 样式块与软换行被清掉；Text 是最后一个字段，里面的逗号属于文本而不是分隔符。
        Assert.AreEqual("[00:01.50]Hello world\n[01:02.34]second, with comma", lrc);
    }

    [TestMethod]
    public void ReturnsNullForTextWithoutCues()
    {
        Assert.IsNull(SubtitleTextConverter.TryConvert("[ti:光]\n[00:01.00]光"));
    }
}
