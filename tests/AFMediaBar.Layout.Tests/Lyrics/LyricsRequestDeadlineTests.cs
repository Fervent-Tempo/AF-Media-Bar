// 验证取词截止时刻的传递：来源能知道本轮还剩多少时间，附加内容来不及就放弃而不拖慢正文；
// 截止时刻不参与曲目身份，缓存键不受影响。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 取词预算的传递：来源按剩余时间决定还要不要做附加请求，以及缓存键不因截止时刻而失效。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LyricsRequestDeadlineTests
{
    private static readonly LyricsRequest _request = new("Track", "Artist", "Album", 180, null);

    [TestMethod]
    public async Task ProviderSeesTheRemainingBudgetOfItsRound()
    {
        LyricsRequest? seen = null;
        var provider = new Provider((request, _) =>
        {
            seen = request;
            return Task.FromResult<LyricsResult?>(null);
        });

        await new LyricsService(provider).GetLyricsAsync(_request, CancellationToken.None);

        Assert.IsNotNull(seen, "来源必须收到请求。");
        Assert.IsNotNull(seen!.Deadline, "协调器必须把本轮截止时刻告诉来源。");
        var remaining = -Stopwatch.GetElapsedTime(seen.Deadline.Value);
        Assert.IsTrue(remaining > TimeSpan.Zero, "截止时刻必须还在未来。");
        Assert.IsTrue(remaining <= LyricsService.DefaultPerSourceBudget + TimeSpan.FromSeconds(1),
            $"截止时刻不得超过单来源预算太多，实际剩 {remaining} 毫秒。");
    }

    [TestMethod]
    public void ExhaustedBudgetLeavesNoDeadline()
    {
        // 预算耗尽时不填截止时刻，来源按"没有时间了"处理。
        var exhausted = LyricsService.WithDeadline(_request, TimeSpan.Zero);
        Assert.IsNull(exhausted.Deadline, "预算耗尽时不填截止时刻，来源按没有时间处理。");

        // 有预算时必须填上，否则来源无从判断还能不能做附加请求。
        var generous = LyricsService.WithDeadline(_request, TimeSpan.FromSeconds(5));
        Assert.IsNotNull(generous.Deadline, "有预算时必须填上截止时刻。");
        Assert.AreNotEqual(_request, generous, "填了截止时刻的请求与原请求不同。");
        Assert.AreEqual(_request.Title, generous.Title, "填截止时刻不得改动其它字段。");
        Assert.AreEqual(_request.Artist, generous.Artist, "填截止时刻不得改动其它字段。");
    }

    [TestMethod]
    public void DeadlineDoesNotChangeTheCacheKey()
    {
        // 缓存键由曲目身份构成：截止时刻每轮都变，带上会让缓存永远不命中。
        var first = _request with { Deadline = Stopwatch.GetTimestamp() + (long)TimeSpan.FromSeconds(5).TotalSeconds * Stopwatch.Frequency };
        var second = _request with { Deadline = Stopwatch.GetTimestamp() + (long)TimeSpan.FromSeconds(9).TotalSeconds * Stopwatch.Frequency };
        Assert.AreNotEqual(first, second, "两轮的截止时刻不同。");

        var keyA = MediaSnapshotBuilder.KeyRequest(first);
        var keyB = MediaSnapshotBuilder.KeyRequest(second);
        Assert.AreEqual(keyA, keyB, "截止时刻不得参与缓存键，否则歌词永远不会被缓存。");
        Assert.IsNull(keyA.Deadline, "缓存键里的请求不带截止时刻。");
    }

    [TestMethod]
    public void RemainingWaitShrinksWithTheDeadline()
    {
        var generous = QQMusicRomanizationProvider.RemainingWait(_request with { Deadline = Stopwatch.GetTimestamp() + (long)TimeSpan.FromSeconds(60).TotalSeconds * Stopwatch.Frequency });
        var tight = QQMusicRomanizationProvider.RemainingWait(_request with { Deadline = Stopwatch.GetTimestamp() + (long)TimeSpan.FromSeconds(1).TotalSeconds * Stopwatch.Frequency });
        var none = QQMusicRomanizationProvider.RemainingWait(_request);

        Assert.IsTrue(generous > tight, "剩余时间越少，等待上限越短。");
        Assert.IsTrue(tight > TimeSpan.Zero, "剩余时间按毫秒计算。");
        Assert.AreEqual(TimeSpan.FromSeconds(6) - QQMusicRomanizationProvider.ReturnReserve, none,
            "没有截止时刻时用固定上限，并扣掉解析与返回的余量。");
    }

    [TestMethod]
    public void RomanizationIsSkippedWhenTheRoundHasNoTimeLeft()
    {
        // 截止时刻已过：剩余时间为零，补取必须直接放弃而不是发起注定超时的请求。
        var expired = QQMusicRomanizationProvider.RemainingWait(_request with { Deadline = Stopwatch.GetTimestamp() - (long)TimeSpan.FromSeconds(1).TotalSeconds * Stopwatch.Frequency });
        Assert.AreEqual(TimeSpan.Zero, expired, "已过期的截止时刻不给补取留任何时间。");
        Assert.IsTrue(expired < QQMusicRomanizationProvider.MinimumUsefulWait, "剩余时间不足时不再发起补取。");
    }

    [TestMethod]
    public async Task StalledRomanizationDoesNotDiscardLyricsOrTranslation()
    {
        var stalled = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string main = "[ti:SongName]\n[5000,1000]Hello there(5000,1000)\n[7000,1000]See you again(7000,1000)";
        const string translation = "[ti:SongName]\n[00:05.00]你好\n[00:07.00]再见";
        var provider = new Provider((request, token) => QQMusicLyricsProvider.CompleteLyricsAsync(main, translation, 100, request, _ =>
            {
                started.TrySetResult();
                return stalled.Task;
            }, token));
        var previous = SettingsManager.Current;
        SettingsManager.Current = new AppSettings
        {
            LyricsSource = new LyricsSourceSettings([LyricsSourceCatalog.QQMusic]) { Rule = LyricsSourceRule.Manual }
        };
        var service = new LyricsService(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), provider);
        try
        {
            var result = await service.GetLyricsAsync(_request, CancellationToken.None);
            Assert.IsNotNull(result, "协调器必须收到解析结果。");
            Assert.IsTrue(started.Task.IsCompletedSuccessfully, "测试必须进入补取。");
            var line = result.Document.Lines.First();
            Assert.AreEqual("Hello there", line.Text);
            Assert.AreEqual("你好", line.Translation);
            Assert.IsNull(line.Romanization);
        }
        finally
        {
            stalled.TrySetResult(null);
            SettingsManager.Current = previous;
        }
    }

    private sealed class Provider(Func<LyricsRequest, CancellationToken, Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => LyricsSourceCatalog.QQMusic;

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken) =>
            retrieve(request, cancellationToken);
    }
}
