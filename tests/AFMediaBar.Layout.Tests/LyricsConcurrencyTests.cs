using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 并发取词的映射与采纳测试：AppID → 默认来源、批次计划，以及三种采纳模式的状态迁移。
/// Tests of concurrent retrieval's mapping and adoption: the AppID-to-default-source mapping, the batching plan, and the
/// state transitions of the three adoption modes.
///
/// 所有服务调用都走显式 <see cref="LyricsRetrievalOptions"/> 重载，避免依赖全局设置；
/// 需要来源开关的用例自行设置并在结尾还原。
/// Every service call goes through the explicit LyricsRetrievalOptions overload so global settings are not relied upon;
/// cases that need the source toggles set them and restore them at the end.
/// </summary>
[TestClass]
public sealed class LyricsConcurrencyTests
{
    private static readonly TimeSpan PerSource = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Total = TimeSpan.FromSeconds(3);

    private static LyricsRequest Request(
        string? sourceAppId = null,
        string? netEaseSongId = null) => new("Song", "Artist", "Album", 200, netEaseSongId, sourceAppId);

    private static LyricsResult Hit(string source) => new(source, LyricDocument.Empty);

    private static LyricsRetrievalOptions Options(
        LyricsAdoptionMode mode,
        int batchSize = LyricsConcurrencyDefaults.BatchSizeDefault,
        int deadlineMilliseconds = LyricsConcurrencyDefaults.AdoptionDeadlineMillisecondsDefault) =>
        new(mode, batchSize, TimeSpan.FromMilliseconds(deadlineMilliseconds));

    // ---- AppID → 默认来源映射 ----

    [TestMethod]
    public void KnownAppIdsMapOntoTheirSearchFallbackSource()
    {
        Assert.AreEqual(LyricsSourceCatalog.NetEaseSearch, LyricsConcurrencyPolicy.MapDefaultSource("cloudmusic.exe", null));
        Assert.AreEqual(LyricsSourceCatalog.NetEaseSearch, LyricsConcurrencyPolicy.MapDefaultSource("Netease.Cloudmusic_wxyz", null));
        Assert.AreEqual(LyricsSourceCatalog.QQMusic, LyricsConcurrencyPolicy.MapDefaultSource("qqmusic.exe", null));
        Assert.AreEqual(LyricsSourceCatalog.QQMusic, LyricsConcurrencyPolicy.MapDefaultSource("Tencent.QQMusicDA_something!App", null));
        Assert.AreEqual(LyricsSourceCatalog.Kugou, LyricsConcurrencyPolicy.MapDefaultSource("KuGou", null));
        Assert.AreEqual(LyricsSourceCatalog.SodaMusic, LyricsConcurrencyPolicy.MapDefaultSource("汽水音乐", null));
    }

    [TestMethod]
    public void UnknownOrMissingAppIdHasNoDefaultSource()
    {
        Assert.IsNull(LyricsConcurrencyPolicy.MapDefaultSource("Spotify.exe", null));
        Assert.IsNull(LyricsConcurrencyPolicy.MapDefaultSource(null, null));
        Assert.IsNull(LyricsConcurrencyPolicy.MapDefaultSource("", null));
    }

    [TestMethod]
    public void ANetEaseSongIdOverridesTheAppIdMapping()
    {
        Assert.AreEqual(
            LyricsSourceCatalog.NetEase,
            LyricsConcurrencyPolicy.MapDefaultSource("cloudmusic.exe", "123456"));
    }

    // ---- 批次计划 ----

    [TestMethod]
    public void BatchesFollowTheUserOrderAndExcludeTheDefaultSource()
    {
        var providers = new[]
        {
            new StubProvider("a", _ => Task.FromResult<LyricsResult?>(null)),
            new StubProvider("b", _ => Task.FromResult<LyricsResult?>(null)),
            new StubProvider("c", _ => Task.FromResult<LyricsResult?>(null)),
            new StubProvider("d", _ => Task.FromResult<LyricsResult?>(null)),
            new StubProvider("e", _ => Task.FromResult<LyricsResult?>(null))
        };
        var batches = LyricsConcurrencyPolicy.PlanBatches(providers, providers[2], 2);

        Assert.AreEqual(2, batches.Count);
        CollectionAssert.AreEqual(new[] { "a", "b" }, batches[0].Select(provider => provider.SourceName).ToArray());
        CollectionAssert.AreEqual(new[] { "d", "e" }, batches[1].Select(provider => provider.SourceName).ToArray());
    }

    [TestMethod]
    public void ANonPositiveBatchSizeIsClampedToTheMinimum()
    {
        var providers = new[]
        {
            new StubProvider("a", _ => Task.FromResult<LyricsResult?>(null)),
            new StubProvider("b", _ => Task.FromResult<LyricsResult?>(null))
        };
        var batches = LyricsConcurrencyPolicy.PlanBatches(providers, null, 0);

        // 批次 1：每批一个来源。/ A batch size of one: one source per batch.
        Assert.AreEqual(2, batches.Count);
        CollectionAssert.AreEqual(new[] { "a" }, batches[0].Select(provider => provider.SourceName).ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, batches[1].Select(provider => provider.SourceName).ToArray());
    }

    // ---- 采纳模式：先到先得 ----

    [TestMethod]
    public async Task FirstArrivalStopsWaitingForTheDefaultInterfaceToo()
    {
        var hang = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic))),
            new StubProvider(LyricsSourceCatalog.NetEase, _ => hang.Task));

        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"), // NetEase 是默认接口，但先到先得不等它
            Options(LyricsAdoptionMode.FirstArrival),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.QQMusic, result!.Source);
        hang.SetResult(null);
    }

    // ---- 采纳模式：偏心默认 ----

    [TestMethod]
    public async Task TheDefaultInterfaceHitIsAdoptedWhenItCompletesSynchronously()
    {
        // 本地缓存一类的默认接口全程无 await，任务在构造时就已完成：这个结果不能被扇出循环丢掉。
        // A local-cache-style default interface never awaits, so its task completes at construction: the dispatch loop
        // must not drop that result.
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.NetEase))),
            new StubProvider(LyricsSourceCatalog.Lrclib, async _ =>
            {
                await Task.Delay(200);
                return Hit(LyricsSourceCatalog.Lrclib);
            }));

        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"),
            Options(LyricsAdoptionMode.PreferDefaultSource),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.NetEase, result!.Source);
    }

    [TestMethod]
    public async Task TheDefaultInterfaceReplacesAnEarlierCandidate()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.NetEase, async _ => // 默认接口（慢）
            {
                await Task.Delay(80);
                return Hit(LyricsSourceCatalog.NetEase);
            }),
            new StubProvider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic))));

        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"),
            Options(LyricsAdoptionMode.PreferDefaultSource),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.NetEase, result!.Source);
    }

    [TestMethod]
    public async Task TheCandidateWinsWhenTheDefaultInterfaceMisses()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(null)), // 默认接口未命中
            new StubProvider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic))));

        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"),
            Options(LyricsAdoptionMode.PreferDefaultSource),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.QQMusic, result!.Source);
    }

    // ---- 采纳模式：偏心默认 + 限时 ----

    [TestMethod]
    public async Task TheDeadlineHandsTheResultToTheCandidateWhenTheDefaultInterfaceHangs()
    {
        var hang = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.NetEase, _ => hang.Task),
            new StubProvider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic))));

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"),
            Options(LyricsAdoptionMode.PreferDefaultSourceWithDeadline, deadlineMilliseconds: 100),
            CancellationToken.None);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.QQMusic, result!.Source);
        Assert.IsTrue(
            elapsed < TimeSpan.FromSeconds(1),
            $"候补在倒计时后就应该转正，实际耗时 {elapsed.TotalMilliseconds:0} 毫秒。");
        hang.SetResult(null);
    }

    [TestMethod]
    public async Task TheDefaultInterfaceStillWinsInsideTheDeadline()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider(LyricsSourceCatalog.NetEase, async _ =>
            {
                await Task.Delay(60);
                return Hit(LyricsSourceCatalog.NetEase);
            }),
            new StubProvider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic))));

        var result = await service.GetLyricsAsync(
            Request(netEaseSongId: "123"),
            Options(LyricsAdoptionMode.PreferDefaultSourceWithDeadline, deadlineMilliseconds: 300),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.NetEase, result!.Source);
    }

    // ---- 批次推进与默认接口的交互 ----

    [TestMethod]
    public async Task BatchesStopAdvancingOnceACandidateExists()
    {
        var asked = new List<string>();
        var miss = new StubProvider("a", _ =>
        {
            asked.Add("a");
            return Task.FromResult<LyricsResult?>(null);
        });
        var hit = new StubProvider("b", _ =>
        {
            asked.Add("b");
            return Task.FromResult<LyricsResult?>(Hit("b"));
        });
        var neverAsked = new StubProvider("c", _ =>
        {
            asked.Add("c");
            return Task.FromResult<LyricsResult?>(Hit("c"));
        });
        var service = new LyricsService(PerSource, Total, miss, hit, neverAsked);

        var result = await service.GetLyricsAsync(
            Request(),
            Options(LyricsAdoptionMode.PreferDefaultSource, batchSize: 2),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("b", result!.Source);
        CollectionAssert.AreEqual(new[] { "a", "b" }, asked);
    }

    [TestMethod]
    public async Task TheDefaultInterfaceIsDispatchedEvenWhenItsSourceIsDisabled()
    {
        var previous = SettingsManager.Current.LyricsSource;
        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([LyricsSourceCatalog.QQMusic]));
        try
        {
            var asked = new List<string>();
            var service = new LyricsService(
                PerSource,
                Total,
                new StubProvider(LyricsSourceCatalog.NetEaseSearch, _ =>
                {
                    asked.Add(LyricsSourceCatalog.NetEaseSearch);
                    return Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.NetEaseSearch));
                }),
                new StubProvider(LyricsSourceCatalog.QQMusic, _ =>
                {
                    asked.Add(LyricsSourceCatalog.QQMusic);
                    return Task.FromResult<LyricsResult?>(Hit(LyricsSourceCatalog.QQMusic));
                }));

            var result = await service.GetLyricsAsync(
                Request(sourceAppId: "cloudmusic.exe"),
                Options(LyricsAdoptionMode.PreferDefaultSource),
                CancellationToken.None);

            // 用户只开了 QQMusic（优先级），但网易云播放器的默认接口（NeteaseSearch）照发并且赢了。
            // 默认接口同步命中时直接返回，优先级来源根本不必被询问。
            // The user enabled QQMusic only (priority), yet the NetEase player's default interface (NeteaseSearch) is
            // still dispatched and wins; a synchronously-hitting default short-circuits before the priority list is asked.
            Assert.IsNotNull(result);
            Assert.AreEqual(LyricsSourceCatalog.NetEaseSearch, result!.Source);
            CollectionAssert.AreEqual(new[] { LyricsSourceCatalog.NetEaseSearch }, asked);
        }
        finally
        {
            SettingsManager.SetLyricsSourceSettings(previous);
        }
    }

    [TestMethod]
    public async Task TheDefaultInterfaceIsNotAskedTwiceWhenItAlsoSitsInThePriorityList()
    {
        var asked = new List<string>();
        var stub = new StubProvider(LyricsSourceCatalog.NetEaseSearch, _ =>
        {
            asked.Add(LyricsSourceCatalog.NetEaseSearch);
            return Task.FromResult<LyricsResult?>(null);
        });
        var filler = new StubProvider(LyricsSourceCatalog.Lrclib, _ =>
        {
            asked.Add(LyricsSourceCatalog.Lrclib);
            return Task.FromResult<LyricsResult?>(null);
        });
        var service = new LyricsService(PerSource, Total, stub, filler);

        var result = await service.GetLyricsAsync(
            Request(sourceAppId: "cloudmusic.exe"),
            Options(LyricsAdoptionMode.PreferDefaultSource, batchSize: 3),
            CancellationToken.None);

        Assert.IsNull(result);
        // NeteaseSearch 既是默认接口又在优先级列表里：只允许一次请求。
        // NeteaseSearch is both the default interface and in the priority list: exactly one request is allowed.
        Assert.AreEqual(1, asked.Count(name => name == LyricsSourceCatalog.NetEaseSearch));
        CollectionAssert.AreEquivalent(
            new[] { LyricsSourceCatalog.NetEaseSearch, LyricsSourceCatalog.Lrclib },
            asked);
    }

    private sealed class StubProvider(
        string sourceName,
        Func<CancellationToken, Task<LyricsResult?>> handler) : ILyricsProvider
    {
        public string SourceName { get; } = sourceName;

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken) =>
            handler(cancellationToken);
    }
}
