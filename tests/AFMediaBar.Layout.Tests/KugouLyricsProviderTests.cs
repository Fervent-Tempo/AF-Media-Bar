using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 酷狗提供器的本地命中路径测试。
/// Tests of the Kugou provider's local-hit path.
/// </summary>
[TestClass]
public sealed class KugouLyricsProviderTests
{
    [TestMethod]
    public async Task ALocalHitReturnsWithoutOnlineSearch()
    {
        var provider = new KugouLyricsProvider((_, _) =>
            new LocalLyricsCacheLookup.CachedText("[1000,1000]<0,1000,0>Example"));
        var request = new LyricsRequest("Song", "Artist", string.Empty, 200, null);

        var result = await provider.GetLyricsAsync(request, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.Kugou, result.Source);
        Assert.AreEqual("Krc", result.Document.SourceFormat);
    }
}
