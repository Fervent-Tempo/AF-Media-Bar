// 验证来源标识到显示名的映射；不依赖 SMTC、进程或当前语言（只覆盖各语言写法相同的品牌名）。
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>来源标识的显示名与分类回归。</summary>
[TestClass]
public sealed class MediaSourceNameFormatterTests
{
    [TestMethod]
    [DataRow("top.izuna.foliamajor", "Folia")]
    [DataRow("Folia.exe", "Folia")]
    [DataRow("nons", "NONS")]
    [DataRow("Nons.exe", "NONS")]
    [DataRow("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [DataRow("vlc.exe", "VLC")]
    [DataRow("mpv", "mpv")]
    public void BrandNamesStayReadable(string sourceId, string expected)
    {
        Assert.AreEqual(expected, MediaSourceNameFormatter.GetDisplayName(sourceId, "未知来源"));
    }

    [TestMethod]
    public void PackagedFoliaKeepsItsCategory()
    {
        // 首页按这个键分组并匹配「播放器支持」里的条目；漏掉映射会掉进 other，卡片显示成 top.izuna。
        Assert.AreEqual("Folia", MediaSourceNameFormatter.GetKnownSourceKey("top.izuna.foliamajor"));
        Assert.AreEqual("Folia", MediaSourceNameFormatter.GetKnownSourceKey("Folia.exe"));
        // NonsPlayer 的 SMTC 标识就是 Nons，未登记时首页会把包名原样显示成 nons 并归到 other。
        // NonsPlayer reports Nons as its SMTC identity; without the mapping the home page shows the bare package name and files it under other.
        Assert.AreEqual("NONS", MediaSourceNameFormatter.GetKnownSourceKey("nons"));
        Assert.AreEqual("NONS", MediaSourceNameFormatter.GetKnownSourceKey("Nons.exe"));
        Assert.AreEqual("other", MediaSourceNameFormatter.GetKnownSourceKey("top.someoneelse.player"));
    }

    [TestMethod]
    public void CategoriesDriveTheMusicFirstTiering()
    {
        Assert.AreEqual(MediaSourceCategory.Music, MediaSourceNameFormatter.GetCategory("cloudmusic.exe"));
        Assert.AreEqual(MediaSourceCategory.Music, MediaSourceNameFormatter.GetCategory("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"));
        Assert.AreEqual(MediaSourceCategory.Music, MediaSourceNameFormatter.GetCategory("nons"));
        Assert.AreEqual(MediaSourceCategory.Video, MediaSourceNameFormatter.GetCategory("PotPlayerMini64.exe"));
        Assert.AreEqual(MediaSourceCategory.Video, MediaSourceNameFormatter.GetCategory("mpv"));
        Assert.AreEqual(MediaSourceCategory.Video, MediaSourceNameFormatter.GetCategory("vlc.exe"));
        Assert.AreEqual(MediaSourceCategory.Browser, MediaSourceNameFormatter.GetCategory("msedge"));
        Assert.AreEqual(MediaSourceCategory.Other, MediaSourceNameFormatter.GetCategory("some-random-app.exe"));
        Assert.AreEqual(MediaSourceCategory.Other, MediaSourceNameFormatter.GetCategory(null));
    }

    [TestMethod]
    public void UnknownSourceFallsBackToTheCallerText()
    {
        Assert.AreEqual("未知来源", MediaSourceNameFormatter.GetDisplayName(null, "未知来源"));
        Assert.AreEqual("未知来源", MediaSourceNameFormatter.GetDisplayName("   ", "未知来源"));
        Assert.AreEqual("SomePlayer", MediaSourceNameFormatter.GetDisplayName("SomePlayer", "未知来源"));
    }
}
