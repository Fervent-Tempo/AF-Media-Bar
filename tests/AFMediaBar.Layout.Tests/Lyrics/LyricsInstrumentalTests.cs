// #267 回归：网易云纯音乐载荷（多行署名 + 纯音乐提示）不得把署名当作歌词显示。
// Regression for #267: a NetEase instrumental payload (credit lines plus the instrumental marker) must not show credits as lyrics.
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Providers.Web.Netease;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsInstrumentalTests
{
    /// <summary>真实网易云纯音乐载荷（song id 28151024）：3 行署名超过一半，随后是纯音乐提示。</summary>
    private const string InstrumentalPayload = """
        [00:00.00] 作曲 : 麻枝准
        [00:01.00] 编曲 : 折戸伸治
        [00:02.00] 制作人 : Key Sounds Label
        [00:05.00]纯音乐，请欣赏
        """;

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void InstrumentalCreditsOnlyAreConfirmedNoLyrics(bool filterInfoLines)
    {
        var result = Resolve(InstrumentalPayload, filterInfoLines);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsResultStatus.NoLyrics, result.Status);
        Assert.AreEqual(0, result.Document.Lines.Count, "纯音乐载荷不能把署名当作歌词。");
    }

    [TestMethod]
    public void LyricsBeforeCreditLinesAreNotCleared()
    {
        const string payload = """
            [00:00.00]第一句正文
            [00:01.00]第二句正文
            [00:02.00] 作曲 : 麻枝准
            [00:03.00]纯音乐，请欣赏
            """;

        var result = Resolve(payload, filterInfoLines: true);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsResultStatus.Available, result.Status, "有正文时不能确认无歌词。");
        CollectionAssert.Contains(result.Document.Lines.Select(line => line.Text).ToArray(), "第一句正文");
        CollectionAssert.Contains(result.Document.Lines.Select(line => line.Text).ToArray(), "第二句正文");
        CollectionAssert.DoesNotContain(result.Document.Lines.Select(line => line.Text).ToArray(), "纯音乐，请欣赏");
    }

    [TestMethod]
    public void InstrumentalConfirmationIsNotOverwrittenByLaterCreditOnlyCandidate()
    {
        // 新端点的行级字段常只有署名 JSON，且不带纯音乐提示；它不能覆盖另一端点已确认的纯音乐结果。
        // The new endpoint's line-level field often holds only credit JSON without the instrumental marker; it must not
        // override an instrumental result confirmed by the other endpoint.
        const string creditOnlyPayload = """
            [00:00.00] 作词 : 张三
            [00:01.00] 作曲 : 李四
            """;
        var request = new LyricsRequest("神圣之门", "麻枝准", string.Empty, 240d, null, true);
        var legacy = new LyricResult
        {
            Code = 200,
            Nolyric = false,
            Lrc = new Lyrics { Lyric = InstrumentalPayload }
        };
        var wordLevel = new LyricResult
        {
            Code = 200,
            Nolyric = false,
            Lrc = new Lyrics { Lyric = creditOnlyPayload }
        };

        var result = NetEaseLyricMerge.Resolve("Netease", legacy, wordLevel, request);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsResultStatus.NoLyrics, result.Status);
        Assert.AreEqual(0, result.Document.Lines.Count);
    }

    [DataTestMethod]
    [DataRow("[00:00.00] 作词 : 张三\n[00:01.00] 作曲 : 李四")]
    [DataRow("[00:00.00] 作词 : 张三\n[00:01.00] 作曲 : 李四\n[00:02.00]暂无歌词")]
    public void NonInstrumentalCreditsAreNotConfirmedNoLyrics(string payload)
    {
        var result = Resolve(payload, filterInfoLines: true);

        Assert.IsNotNull(result);
        Assert.AreNotEqual(LyricsResultStatus.NoLyrics, result.Status, "只有纯音乐标记可以确认无歌词。");
        Assert.IsTrue(result.Document.Lines.Count > 0, "没有纯音乐标记时保留既有署名兜底行为。");
    }

    [TestMethod]
    public void TransientPlaceholderDoesNotConfirmNoLyrics()
    {
        const string payload = """
            [00:00.00] 作曲 : 麻枝准
            [00:01.00] 编曲 : 折戸伸治
            [00:02.00]歌词制作中: 请稍候
            """;

        var result = Resolve(payload, filterInfoLines: true);

        Assert.IsNotNull(result);
        Assert.AreNotEqual(LyricsResultStatus.NoLyrics, result.Status, "临时状态不能被缓存为确认无歌词。");
        Assert.IsTrue(result.Document.Lines.Count > 0, "临时占位行只丢弃，不能把同一候选中的署名一并清空。");
    }

    private static LyricsResult? Resolve(string payload, bool filterInfoLines)
    {
        var request = new LyricsRequest("神圣之门", "麻枝准", string.Empty, 240d, null, filterInfoLines);
        var legacy = new LyricResult
        {
            Code = 200,
            Nolyric = false,
            Lrc = new Lyrics { Lyric = payload }
        };
        return NetEaseLyricMerge.Resolve("Netease", legacy, null, request);
    }
}
