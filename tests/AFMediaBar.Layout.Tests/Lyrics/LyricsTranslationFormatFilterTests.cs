// 验证译文格式清理只去掉来源附加的标记（行首斜杠、首尾成对括号），中间文字保留，并且只在显示层生效、不影响歌词文档。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>译文行只去掉来源附加的格式标记，保留正文。</summary>
[TestClass]
public sealed class LyricsTranslationFormatFilterTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 22, 0, 30, 0, TimeSpan.Zero);

    [DataTestMethod]
    [DataRow("【一个人是艰辛的 于是两人牵起手】", "一个人是艰辛的 于是两人牵起手")]
    [DataRow("〖总是在不知不觉中兜圈子的话语〗", "总是在不知不觉中兜圈子的话语")]
    [DataRow("  【前后有空白也算整行】  ", "前后有空白也算整行")]
    [DataRow("【外层【嵌套】也只去最外层】", "外层【嵌套】也只去最外层")]
    [DataRow("/ 曾经沉睡着（曾经诞生）", "曾经沉睡着（曾经诞生）")]
    [DataRow("/  只有斜杠标记  ", "只有斜杠标记")]
    [DataRow("/ 【带斜杠又带括号】", "带斜杠又带括号")]
    [DataRow("【/ 括号里再带斜杠】", "括号里再带斜杠")]
    public void MarkersAreRemovedAndTextIsKept(string source, string expected) =>
        Assert.AreEqual(expected, LyricsTranslationFormatFilter.Clean(source));

    [TestMethod]
    public void NothingLeftBecomesExplicitBlank()
    {
        // 用空格区分已清空的行与缺失文本。
        Assert.AreEqual(LyricsTranslationFormatFilter.HiddenText, LyricsTranslationFormatFilter.Clean("【】"));
        Assert.AreEqual(LyricsTranslationFormatFilter.HiddenText, LyricsTranslationFormatFilter.Clean("〖 〗"));
        Assert.AreEqual(LyricsTranslationFormatFilter.HiddenText, LyricsTranslationFormatFilter.Clean("/ 【】"));
        Assert.AreEqual(LyricsTranslationFormatFilter.HiddenText, LyricsTranslationFormatFilter.Clean("/ "));
    }

    [DataTestMethod]
    [DataRow("普通的一句歌词")]
    [DataRow("【只有左边括号")]
    [DataRow("只有右边括号】")]
    [DataRow("【括号类型不配对〗")]
    [DataRow("开头不是括号【中间才有】")]
    [DataRow("【】后面还有正文")]
    [DataRow("(半角括号不算)")]
    [DataRow("/cause I love you")]
    [DataRow("/")]
    [DataRow("【")]
    [DataRow("")]
    public void EverythingElseStaysUntouched(string source)
    {
        var result = LyricsTranslationFormatFilter.Clean(source);
        Assert.AreEqual(source, result);
        if (!string.IsNullOrEmpty(source))
            Assert.IsTrue(ReferenceEquals(source, result), "没有需要去掉的标记时必须返回同一个实例。");
    }

    [TestMethod]
    public void ProjectionCleansFormattingOnlyWhenTheSettingIsOn()
    {
        var wrapped = new LyricLine(0, 10, "/ 【一个人是艰辛的 于是两人牵起手】")
        {
            Translation = "〖总是在不知不觉中兜圈子的话语〗"
        };
        var wrappedNext = new LyricLine(10, 20, "/ 曾经沉睡着（曾经诞生）");
        var snapshot = Snapshot([wrapped, wrappedNext], position: 2);

        var on = LyricsPresentationProjector.Project(
            snapshot,
            new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true, LyricsBracketSymbolsHidden = true },
            _now);
        Assert.AreEqual("一个人是艰辛的 于是两人牵起手", on.Current, "斜杠与括号一起去掉，正文保留。");
        Assert.AreEqual("曾经沉睡着（曾经诞生）", on.Next, "行首斜杠同样去掉。");

        var off = LyricsPresentationProjector.Project(
            snapshot,
            new AppSettings { LyricsEnabled = true, TwoLineLyricsEnabled = true, LyricsBracketSymbolsHidden = false },
            _now);
        Assert.AreEqual("/ 【一个人是艰辛的 于是两人牵起手】", off.Current);
        Assert.AreEqual("/ 曾经沉睡着（曾经诞生）", off.Next);

        // 歌词文档本身一个字符都不该被改。
        Assert.AreEqual("/ 【一个人是艰辛的 于是两人牵起手】", wrapped.Text);
        Assert.AreEqual("〖总是在不知不觉中兜圈子的话语〗", wrapped.Translation);
    }

    [DataTestMethod]
    [DataRow("/ 你好", "/ 你", "好")]
    [DataRow("【你好】", "【你", "好】")]
    [DataRow("/ 【你好】", "/ 【你", "好】")]
    public void ProjectionUsesVisibleWordWeightsWithoutChangingSourceTimings(string text, string firstWord, string secondWord)
    {
        LyricWord[] words = [new(0, 1, firstWord), new(1, 3, secondWord)];
        var line = new LyricLine(0, 3, text) { Words = words };
        var display = LyricsTranslationFormatFilter.Apply(line);
        for (var index = 0; index < words.Length; index++)
        {
            Assert.AreEqual(words[index].Start, display.Words[index].Start);
            Assert.AreEqual(words[index].End, display.Words[index].End);
        }
        var frame = LyricsPresentationProjector.Project(Snapshot([line], 1), new AppSettings(), _now);
        Assert.AreEqual("你好", frame.Current);
        Assert.AreEqual(0.5, frame.WordScanProgress!.Value, 0.000001);
        Assert.AreEqual(text, line.Text);
        CollectionAssert.AreEqual(words, line.Words.ToArray());
    }

    [TestMethod]
    public void SyllableLyricsKeepTheirTimelineAndWeighOnlyVisibleCharacters()
    {
        // 标记跨音节时同步清理文本；时间戳保持不变，进度与不带标记的歌词一致。
        LyricWord[] words = [new(0, 1, "/ 【一"), new(1, 2, "个人"), new(2, 3, "是艰】")];
        var marked = new LyricLine(0, 3, "/ 【一个人是艰】") { Words = words };
        var plain = new LyricLine(0, 3, "一个人是艰")
        {
            Words = [new(0, 1, "一"), new(1, 2, "个人"), new(2, 3, "是艰")]
        };

        var cleaned = LyricsTranslationFormatFilter.Apply(marked);
        Assert.AreEqual("一个人是艰", cleaned.Text);
        Assert.AreEqual("一", cleaned.Words[0].Text);
        Assert.AreEqual("个人", cleaned.Words[1].Text);
        Assert.AreEqual("是艰", cleaned.Words[2].Text);
        for (var index = 0; index < words.Length; index++)
        {
            Assert.AreEqual(words[index].Start, cleaned.Words[index].Start, "音节起点不能被改。");
            Assert.AreEqual(words[index].End, cleaned.Words[index].End, "音节终点不能被改。");
        }

        var cleanedProgress = LyricHighlightPolicy.ResolveProgress(cleaned, 2.5);
        var plainProgress = LyricHighlightPolicy.ResolveProgress(plain, 2.5);
        Assert.IsNotNull(cleanedProgress);
        Assert.AreEqual(plainProgress!.Value, cleanedProgress!.Value, 1e-9, "擦亮进度必须按清理后的可见字符算。");
        Assert.AreEqual("/ 【一个人是艰】", marked.Text);
        Assert.AreEqual("/ 【一", words[0].Text);
    }

    private static MediaSnapshot Snapshot(IReadOnlyList<LyricLine> lines, double position) => MediaSnapshot.Disconnected with
    {
        IsConnected = true,
        Lyrics = new LyricsResult("test", new LyricDocument(lines, LyricsSyncType.LineSynced, "test")),
        Position = position,
        Duration = 30
    };
}
