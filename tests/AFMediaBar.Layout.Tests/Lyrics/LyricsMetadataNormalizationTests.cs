// 验证在线取词请求侧元数据归一化：半角中点/全半角/空白差异不影响搜索与评分。全部内存数据，不访问网络。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LyricsMetadataNormalizationTests
{
    private const string HalfWidthTitle = "ふ･れ･ん･ど･し･た･い"; // ･ = U+FF65
    private const string FullWidthTitle = "ふ・れ・ん・ど・し・た・い"; // ・ = U+30FB
    private const string Artist = "学園生活部";

    private static LyricsRequest Request(string title) =>
        new(title, Artist, FullWidthTitle, 218.7, null, PlaybackSourceId: "QQMusic.exe");

    [TestMethod]
    public void HalfWidthMiddleDotMatchesFullWidthInTitleScore()
    {
        // 回归：SMTC 给半角中点、在线曲库用全角中点时，标题相似度原先只有约 0.69，
        // 叠加歌手/专辑后仍可能跌破 60 分门槛被过滤成 FailedOrUnmatched。
        var request = Request(HalfWidthTitle);
        var breakdown = LyricsMetadataScore.Explain(request,
            [FullWidthTitle], [Artist], [FullWidthTitle], 218.7);
        Assert.AreEqual(1d, breakdown.Title, "NFKC 后两个中点写法应完全一致");
        Assert.AreEqual(1d, breakdown.Artist);
        Assert.AreEqual(1d, breakdown.Album);
        Assert.AreEqual(100, breakdown.TotalScore);
    }

    [TestMethod]
    public void NormalizationDoesNotEraseMiddleDot()
    {
        // 只统一中点写法、不删除：删除会把「ふ・れ…」误当成无中点的「ふれんどしたい」。
        var request = Request(HalfWidthTitle);
        var withoutDot = LyricsMetadataScore.Explain(request,
            ["ふれんどしたい"], [Artist], [FullWidthTitle], 218.7);
        Assert.IsTrue(withoutDot.Title < 1d, "归一后仍应保留中点，不与无中点写法完全等价");
    }

    [TestMethod]
    public void SearchKeepsOriginalAndAddsNormalizedQueries()
    {
        var queries = LyricsSearchQueryPolicy.Build(Request(HalfWidthTitle)).ToArray();
        CollectionAssert.Contains(queries, $"{HalfWidthTitle} {Artist}", "原写法必须保留作兜底");
        CollectionAssert.Contains(queries, $"{FullWidthTitle} {Artist}", "应补上归一化后的写法");
        CollectionAssert.Contains(queries, HalfWidthTitle);
        CollectionAssert.Contains(queries, FullWidthTitle);
    }

    [TestMethod]
    public void FullWidthAsciiAndExtraWhitespaceAreFolded()
    {
        var request = new LyricsRequest("ＭｙＧＯ！！", "Ａｉｃｈｉ", "", 200, null, PlaybackSourceId: "QQMusic.exe");
        var breakdown = LyricsMetadataScore.Explain(request,
            ["MyGO!!  "], ["Aichi"], [""], 200);
        Assert.AreEqual(1d, breakdown.Title, "全角字母与多余空白应折叠");
        Assert.AreEqual(1d, breakdown.Artist);
    }
}
