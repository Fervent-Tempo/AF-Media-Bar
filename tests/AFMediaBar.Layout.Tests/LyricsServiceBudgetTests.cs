using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词并发链的时间上限与调度测试。
/// Tests for the concurrent lyric chain's time limits and dispatching.
///
/// 已引用的歌词库内部请求不接受取消令牌，因此这里断言的是"等待上限"：超时后不再等待该来源并推进后续批次，
/// 被放弃的任务不会被观察成未处理异常。
/// Requests inside the referenced lyric library take no cancellation token, so what these tests assert is a wait limit: after
/// a timeout the source is no longer awaited and later batches advance, while the abandoned task is not left as an unobserved
/// exception.
/// </summary>
[TestClass]
public sealed class LyricsServiceBudgetTests
{
    private static readonly TimeSpan PerSource = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan Total = TimeSpan.FromMilliseconds(400);

    private static readonly LyricsRetrievalOptions Options =
        new(LyricsQueryStrategy.Concurrent, LyricsAdoptionMode.FirstArrival, LyricsConcurrencyDefaults.BatchSizeDefault, TimeSpan.Zero);

    private static LyricsRequest Request() => new("Song", "Artist", "Album", 200, NetEaseSongId: null);

    private static LyricsResult Hit(string source) => new(source, LyricDocument.Empty);

    [TestMethod]
    public async Task TheEarliestResultInTheBatchWinsEvenWhenAHigherPrioritySourceIsSlower()
    {
        // 并发语义：同一批里的来源同时被询问，先到先得——高优先级但慢的结果不会赢得比赛。
        // Concurrent semantics: sources in one batch are asked together, first arrival wins — a higher-priority but slower
        // result never wins the race.
        var asked = new List<string>();
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider("slow-first", async _ =>
            {
                asked.Add("slow-first");
                await Task.Delay(40);
                return Hit("slow-first");
            }),
            new StubProvider("fast-second", _ =>
            {
                asked.Add("fast-second");
                return Task.FromResult<LyricsResult?>(Hit("fast-second"));
            }));

        var result = await service.GetLyricsAsync(Request(), Options, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("fast-second", result!.Source);
        CollectionAssert.AreEqual(new[] { "slow-first", "fast-second" }, asked);
    }

    [TestMethod]
    public async Task HangingProviderIsAbandonedAndTheNextOneRuns()
    {
        var hang = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider("hanging", _ => hang.Task),
            new StubProvider("fallback", _ => Task.FromResult<LyricsResult?>(Hit("fallback"))));

        var result = await service.GetLyricsAsync(Request(), Options, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("fallback", result!.Source);
        hang.SetResult(null);
    }

    [TestMethod]
    public async Task EveryProviderHangingEndsWithinTheTotalBudget()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider("one", _ => new TaskCompletionSource<LyricsResult?>().Task),
            new StubProvider("two", _ => new TaskCompletionSource<LyricsResult?>().Task),
            new StubProvider("three", _ => new TaskCompletionSource<LyricsResult?>().Task));

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await service.GetLyricsAsync(Request(), Options, CancellationToken.None);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);

        Assert.IsNull(result);
        Assert.IsTrue(elapsed < Total + TimeSpan.FromMilliseconds(300), $"取词链耗时 {elapsed.TotalMilliseconds:0} 毫秒，超过整链预算。");
    }

    [TestMethod]
    public async Task FaultingProviderDoesNotBreakTheChain()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider("faulting", _ => throw new InvalidOperationException("boom")),
            new StubProvider("fallback", _ => Task.FromResult<LyricsResult?>(Hit("fallback"))));

        var result = await service.GetLyricsAsync(Request(), Options, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("fallback", result!.Source);
    }

    [TestMethod]
    public async Task CallerCancellationIsThrown()
    {
        var service = new LyricsService(
            PerSource,
            Total,
            new StubProvider("hanging", _ => new TaskCompletionSource<LyricsResult?>().Task));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => service.GetLyricsAsync(Request(), Options, cancellation.Token));
    }

    [TestMethod]
    public async Task CallerCancellationWhileProviderIsPendingIsThrown()
    {
        var hanging = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LyricsService(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            new StubProvider("hanging", _ => hanging.Task));
        using var cancellation = new CancellationTokenSource();

        var lookup = service.GetLyricsAsync(Request(), Options, cancellation.Token);
        Assert.IsFalse(lookup.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => lookup.WaitAsync(TimeSpan.FromSeconds(2)));
        hanging.SetResult(null);
    }

    [TestMethod]
    public void DefaultProviderOrderKeepsExactSourcesBeforeFuzzySearches()
    {
        var names = LyricsProviderFactory.CreateDefault().Select(provider => provider.SourceName).ToArray();

        CollectionAssert.AreEqual(
            new[] { "Netease", "NeteaseSearch", "LRCLIB", "QQMusic", "Kugou", "SodaMusic" },
            names);
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
