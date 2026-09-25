using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// QQ 音乐缓存读取的线程边界测试。
/// Tests of the QQ Music cache-read threading boundary.
/// </summary>
[TestClass]
public sealed class QQMusicLyricsProviderTests
{
    [TestMethod]
    public async Task ABlockingCacheReadDoesNotBlockTheCaller()
    {
        using var release = new ManualResetEventSlim();
        var provider = new QQMusicLyricsProvider((_, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(2));
            return new LocalLyricsCacheLookup.CachedText("[00:01.00]Example");
        });
        var request = new LyricsRequest("Song", "Artist", "Album", 200, null);
        var unblock = Task.Run(async () =>
        {
            await Task.Delay(500);
            release.Set();
        });

        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var lookup = provider.GetLyricsAsync(request, CancellationToken.None);
            var callDuration = Stopwatch.GetElapsedTime(startedAt);

            Assert.IsTrue(callDuration < TimeSpan.FromMilliseconds(250),
                $"调用方被缓存读取同步阻塞了 {callDuration.TotalMilliseconds:0} 毫秒。");
            var result = await lookup.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotNull(result);
            Assert.AreEqual(LyricsSourceCatalog.QQMusic, result.Source);
        }
        finally
        {
            release.Set();
            await unblock;
        }
    }
}
