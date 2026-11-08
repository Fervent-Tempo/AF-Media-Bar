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
        Assert.AreEqual("other", MediaSourceNameFormatter.GetKnownSourceKey("top.someoneelse.player"));
    }

    [TestMethod]
    public void UnknownSourceFallsBackToTheCallerText()
    {
        Assert.AreEqual("未知来源", MediaSourceNameFormatter.GetDisplayName(null, "未知来源"));
        Assert.AreEqual("未知来源", MediaSourceNameFormatter.GetDisplayName("   ", "未知来源"));
        Assert.AreEqual("SomePlayer", MediaSourceNameFormatter.GetDisplayName("SomePlayer", "未知来源"));
    }
}
