// 验证转换阻塞不会阻塞呈现，重复帧不会重复转换，积压时保留最新请求。
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsChineseConversionCacheTests
{
    [TestMethod]
    public async Task BlockedConversionReturnsOriginalAndReusesCompletedResult()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var cache = new LyricsChineseConversionCache((text, mode) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            return "這裡";
        });
        try
        {
            Assert.AreEqual("这里", cache.GetOrRequest("这里", LyricsChineseConversionMode.SimplifiedToTraditional));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            var frame = Task.Run(() => cache.GetOrRequest("这里", LyricsChineseConversionMode.SimplifiedToTraditional));
            Assert.AreEqual("这里", await frame.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            release.Set();
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.AreEqual("這裡", cache.GetOrRequest("这里", LyricsChineseConversionMode.SimplifiedToTraditional));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task QueueDropsOlderRequestsAndKeepsDirectionSeparate()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var processed = new List<string>();
        var cache = new LyricsChineseConversionCache((text, mode) =>
        {
            if (text == "active")
            {
                entered.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            }
            processed.Add(text);
            return $"{text}:{mode}";
        }, capacity: 2, pendingCapacity: 2);
        try
        {
            _ = cache.GetOrRequest("active", LyricsChineseConversionMode.SimplifiedToTraditional);
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            _ = cache.GetOrRequest("obsolete", LyricsChineseConversionMode.SimplifiedToTraditional);
            _ = cache.GetOrRequest("latest", LyricsChineseConversionMode.SimplifiedToTraditional);
            _ = cache.GetOrRequest("latest", LyricsChineseConversionMode.TraditionalToSimplified);
        }
        finally
        {
            release.Set();
            await cache.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        CollectionAssert.AreEqual(new[] { "active", "latest", "latest" }, processed);
        Assert.AreEqual("latest:SimplifiedToTraditional", cache.GetOrRequest("latest", LyricsChineseConversionMode.SimplifiedToTraditional));
        Assert.AreEqual("latest:TraditionalToSimplified", cache.GetOrRequest("latest", LyricsChineseConversionMode.TraditionalToSimplified));
    }
}
