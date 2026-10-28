using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止相似进程名和共享显示名把音量与频谱指向其他应用。</summary>
[TestClass]
public sealed class MediaSourceProcessResolverTests
{
    [TestMethod]
    public void ExplicitIdentityWinsRegardlessOfSessionOrderAndLifetime()
    {
        var resolver = new MediaSourceProcessResolver();
        (string, string)[] sessions = [("QQ", "QQ音乐"), ("QQMusic", "QQ音乐")];
        foreach (var order in new[] { sessions, sessions.Reverse().ToArray() })
        {
            CollectionAssert.AreEquivalent(new[] { "QQMusic" },
                resolver.ResolveMatches("QQMusic.exe", "QQ音乐", order).ToArray());
            Assert.AreEqual(0, resolver.ResolveMatches("QQMusic.exe", "QQ音乐", [("QQ", "QQ音乐")]).Count);
            CollectionAssert.AreEquivalent(new[] { "QQMusic" },
                resolver.ResolveMatches("QQMusic.exe", "QQ音乐", order).ToArray());
        }
        CollectionAssert.AreEquivalent(new[] { "QQ" },
            resolver.ResolveMatches(@"C:\Apps\QQ.exe", "QQMusic", sessions).ToArray());
    }

    [TestMethod]
    public void UnknownSourceRequiresUniqueFullNameAndRechecksEveryScan()
    {
        var resolver = new MediaSourceProcessResolver();
        Assert.AreEqual(0, resolver.ResolveMatches("unknown!app", "Player",
            [("PlayerHelper", "PlayerHelper"), ("OtherPlayer", "OtherPlayer")]).Count);
        Assert.AreEqual(0, resolver.ResolveMatches("unknown!app", "播放器",
            [("first", "播放器"), ("second", "播放器")]).Count);
        CollectionAssert.AreEquivalent(new[] { "second" }, resolver.ResolveMatches("unknown!app", "播放器",
            [("second", "播放器")]).ToArray());
        CollectionAssert.AreEquivalent(new[] { "first" }, resolver.ResolveMatches("unknown!app", "播放器",
            [("first", "播放器")]).ToArray());
    }

    [TestMethod]
    [DataRow("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [DataRow("Chrome", "chrome")]
    [DataRow("MicrosoftEdge", "msedge")]
    [DataRow("kugou", "KuGouMusic")]
    [DataRow("Microsoft.ZuneMusic!App", "Music.UI")]
    public void RegisteredAliasesRemainAvailable(string sourceId, string processName)
    {
        var resolver = new MediaSourceProcessResolver();
        CollectionAssert.AreEquivalent(new[] { processName }, resolver.ResolveMatches(sourceId, "播放器",
            [(processName, "播放器"), ("unrelated", "播放器")]).ToArray());
    }
}
