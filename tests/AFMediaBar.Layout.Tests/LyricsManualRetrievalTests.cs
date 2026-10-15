// 使用可控提供器验证手动取词调度、取消与设置兼容；不访问网络，结束时恢复全局设置。
using System.Text.Json;
using System.Reflection;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止来源优先级被默认规则覆盖，或慢源、旧请求阻塞取词。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LyricsManualRetrievalTests
{
    private AppSettings _previous = null!;
    private AppSettings? _previousDefaults;
    private static readonly LyricsRequest _request = new("Track", "Artist", "Album", 180, null);

    [TestInitialize]
    public void Initialize()
    {
        _previous = SettingsManager.Current;
        _previousDefaults = SettingsManager.UserDefaults;
        SettingsManager.SetUserDefaults(null);
        SettingsManager.Current = new AppSettings { LyricsSource = LyricsSourceSettings.Default with { Rule = LyricsSourceRule.Manual } };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.Current = _previous;
        SettingsManager.SetUserDefaults(_previousDefaults);
    }

    [DataTestMethod]
    [DataRow(LyricsSourceResultMode.Ordered)]
    [DataRow(LyricsSourceResultMode.FirstAvailable)]
    public async Task SequentialUsesManualOrderAndAcceptsLowScores(LyricsSourceResultMode mode)
    {
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Available("QQ", 100)));
        var lrclib = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(Available("LRCLIB", 1)));
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with
        {
            ResultMode = mode,
            PrioritySourceIds = [lrclib.SourceName, qq.SourceName]
        };
        var result = await new LyricsService(qq, lrclib).GetLyricsAsync(_request with { PlaybackSourceId = "qqmusic" }, CancellationToken.None);
        Assert.AreEqual("LRCLIB", result?.Source);
        Assert.AreEqual(0, qq.Calls, "手动优先级不得被QQ阶段、匹配分或当前播放器覆盖。");
    }

    [DataTestMethod]
    [DataRow(LyricsSourceResultMode.Ordered)]
    [DataRow(LyricsSourceResultMode.FirstAvailable)]
    public async Task ParallelStartsTogetherButSelectsByTheChosenResultRule(LyricsSourceResultMode mode)
    {
        var gate = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var earlier = new Provider(LyricsSourceCatalog.NetEase, _ => gate.Task);
        var later = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(Available("Later")));
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with
        {
            AttemptMode = LyricsSourceAttemptMode.Parallel,
            ResultMode = mode
        };
        try
        {
            var retrieval = new LyricsService(earlier, later).GetLyricsAsync(_request, CancellationToken.None);
            await later.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (mode == LyricsSourceResultMode.Ordered)
            {
                Assert.IsFalse(retrieval.IsCompleted, "后序先返回不能越过仍在等待的前序。");
                gate.SetResult(Available("Earlier"));
                Assert.AreEqual("Earlier", (await retrieval.WaitAsync(TimeSpan.FromSeconds(3)))?.Source);
            }
            else
            {
                Assert.AreEqual("Later", (await retrieval.WaitAsync(TimeSpan.FromSeconds(3)))?.Source);
                Assert.IsTrue(earlier.Token.IsCancellationRequested, "先到先得命中后取消其余来源。");
            }
        }
        finally { gate.TrySetResult(Available("Late result")); }
    }

    [DataTestMethod]
    [DataRow(LyricsSourceAttemptMode.Sequential, LyricsSourceResultMode.Ordered)]
    [DataRow(LyricsSourceAttemptMode.Sequential, LyricsSourceResultMode.FirstAvailable)]
    [DataRow(LyricsSourceAttemptMode.Parallel, LyricsSourceResultMode.Ordered)]
    [DataRow(LyricsSourceAttemptMode.Parallel, LyricsSourceResultMode.FirstAvailable)]
    public async Task NoLyricsAndFailuresFallThroughToValidLyrics(LyricsSourceAttemptMode attempt, LyricsSourceResultMode selection)
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { AttemptMode = attempt, ResultMode = selection };
        var noLyrics = new Provider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(LyricsResult.NoLyrics("No lyrics")));
        var failed = new Provider(LyricsSourceCatalog.QQMusic, _ => Task.FromException<LyricsResult?>(new IOException("Failure")));
        var empty = new Provider(LyricsSourceCatalog.Kugou, _ => Task.FromResult<LyricsResult?>(new("Empty", LyricDocument.Empty)));
        var valid = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(Available("Valid")));
        var result = await new LyricsService(noLyrics, failed, empty, valid).GetLyricsAsync(_request, CancellationToken.None);
        Assert.AreEqual("Valid", result?.Source);
    }

    [DataTestMethod]
    [DataRow(LyricsSourceAttemptMode.Sequential)]
    [DataRow(LyricsSourceAttemptMode.Parallel)]
    public async Task TimeoutAdvancesAndCallerCancellationStopsTheChain(LyricsSourceAttemptMode attempt)
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { AttemptMode = attempt };
        var abandoned = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new Provider(LyricsSourceCatalog.NetEase, _ => abandoned.Task);
        var fallback = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(Available("Fallback")));
        try
        {
            var service = new LyricsService(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(40), slow, fallback);
            Assert.AreEqual("Fallback", (await service.GetLyricsAsync(_request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)))?.Source);
            Assert.IsTrue(slow.Token.IsCancellationRequested);
        }
        finally { abandoned.TrySetException(new IOException("Late provider failure")); }

        using var cancellation = new CancellationTokenSource();
        var pending = new Provider(LyricsSourceCatalog.NetEase, async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; });
        var lookup = new LyricsService(pending).GetLyricsAsync(_request, cancellation.Token);
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        try { await lookup.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Fail("调用方取消后不得返回未命中。"); }
        catch (OperationCanceledException) { }
        Assert.IsTrue(pending.Token.IsCancellationRequested);
    }

    [TestMethod]
    public async Task EmptySelectionMakesNoRequestsAndConfirmedAbsenceDiffersFromFailure()
    {
        var absent = new Provider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(LyricsResult.NoLyrics("Confirmed")));
        var failed = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(null));
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { EnabledSourceIds = [] };
        var service = new LyricsService(absent, failed);
        Assert.IsNull(await service.GetLyricsAsync(_request, CancellationToken.None));
        Assert.AreEqual(0, absent.Calls + failed.Calls);
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { EnabledSourceIds = null };
        Assert.AreEqual(LyricsResultStatus.NoLyrics, (await service.GetLyricsAsync(_request, CancellationToken.None))?.Status);
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { EnabledSourceIds = [failed.SourceName] };
        Assert.IsNull(await service.GetLyricsAsync(_request, CancellationToken.None));
    }

    [TestMethod]
    public async Task DefaultRulesIgnoreRememberedManualOptions()
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with
        {
            Rule = LyricsSourceRule.Default,
            AttemptMode = LyricsSourceAttemptMode.Parallel,
            ResultMode = LyricsSourceResultMode.FirstAvailable,
            PrioritySourceIds = [LyricsSourceCatalog.Lrclib, LyricsSourceCatalog.QQMusic]
        };
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Available("Default QQ", 90)));
        var other = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(Available("Other", 100)));
        Assert.AreEqual("Default QQ", (await new LyricsService(other, qq).GetLyricsAsync(_request, CancellationToken.None))?.Source);
        Assert.AreEqual(0, other.Calls);
    }

    [TestMethod]
    public void OldSettingsKeepSourceTriStateAndNewOptionsRoundTripThroughProductionStorage()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var json in new[] { "{}", "{\"lyricsSource\":{}}", "{\"lyricsSource\":{\"enabledSourceIds\":null}}" })
        {
            var old = JsonSerializer.Deserialize<AppSettings>(json, options)!.Normalize().LyricsSource;
            Assert.AreEqual(LyricsSourceRule.Default, old.Rule);
            Assert.IsNull(old.EnabledSourceIds);
        }
        Assert.AreEqual(0, JsonSerializer.Deserialize<AppSettings>("{\"lyricsSource\":{\"enabledSourceIds\":[]}}", options)!.Normalize().LyricsSource.EnabledSourceIds!.Count);
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-manual-lyrics-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var storage = new SettingsPersistenceService(directory))
            {
                storage.Initialize();
                SettingsManager.Current.LyricsSource = new([LyricsSourceCatalog.Lrclib, "Future"])
                {
                    Rule = LyricsSourceRule.Manual,
                    AttemptMode = LyricsSourceAttemptMode.Parallel,
                    ResultMode = LyricsSourceResultMode.FirstAvailable,
                    PrioritySourceIds = [" Future ", LyricsSourceCatalog.Lrclib, LyricsSourceCatalog.Lrclib]
                };
                storage.Flush();
            }
            using var loaded = new SettingsPersistenceService(directory);
            loaded.Initialize();
            var source = SettingsManager.Current.LyricsSource;
            Assert.AreEqual(LyricsSourceRule.Manual, source.Rule);
            Assert.AreEqual(LyricsSourceAttemptMode.Parallel, source.AttemptMode);
            Assert.AreEqual(LyricsSourceResultMode.FirstAvailable, source.ResultMode);
            CollectionAssert.AreEqual(new[] { "Future", LyricsSourceCatalog.Lrclib }, source.PrioritySourceIds!.ToArray());
            CollectionAssert.AreEqual(new[] { LyricsSourceCatalog.Lrclib, "Future" }, source.EnabledSourceIds!.ToArray());
            SettingsManager.SetUserDefaults(new AppSettings { LyricsSource = source });
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default;
            SettingsManager.ResetLyrics();
            var reset = SettingsManager.Current.LyricsSource;
            Assert.AreEqual(source.Rule, reset.Rule);
            Assert.AreEqual(source.AttemptMode, reset.AttemptMode);
            Assert.AreEqual(source.ResultMode, reset.ResultMode);
            CollectionAssert.AreEqual(source.PrioritySourceIds.ToArray(), reset.PrioritySourceIds!.ToArray());
            CollectionAssert.AreEqual(source.EnabledSourceIds.ToArray(), reset.EnabledSourceIds!.ToArray());
            Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsSource), null));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ManualOrderIsIndependentOfTheLegacyEnabledListAndEditorPreservesDisabledPositions()
    {
        var first = new Provider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(null));
        var second = new Provider(LyricsSourceCatalog.Lrclib, _ => Task.FromResult<LyricsResult?>(null));
        var selected = LyricsSourcePolicy.ResolveActive([first, second], new([second.SourceName, first.SourceName]) { Rule = LyricsSourceRule.Manual });
        Assert.AreSame(first, selected[0], "旧启用集合的顺序不能冒充手动优先级。");
        StaTest.Run(_ =>
        {
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default;
            using var context = new SettingsPageContext();
            context.Initialize(SettingsContext.Initial);
            using var localization = new LocalizationService();
            using var editor = new LyricsViewModel(localization, new LegacySettingsConfiguration(context));
            editor.SourceRule = LyricsSourceRule.Manual;
            var item = editor.SourceEntries[2];
            editor.MoveSourceUpCommand.Execute(item);
            Assert.IsNull(SettingsManager.Current.LyricsSource.EnabledSourceIds, "全启用时排序不能把启用集合改成显式列表。");
            Assert.AreEqual(item.SourceId, SettingsManager.Current.LyricsSource.PrioritySourceIds![1]);
            item.IsEnabled = false;
            editor.SourceRule = LyricsSourceRule.Default;
            editor.SourceRule = LyricsSourceRule.Manual;
            Assert.AreEqual(item.SourceId, editor.SourceEntries[1].SourceId);
            Assert.IsFalse(editor.SourceEntries[1].IsEnabled);
            editor.SourceAttemptMode = LyricsSourceAttemptMode.Parallel;
            editor.SourceResultMode = LyricsSourceResultMode.FirstAvailable;
            SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { EnabledSourceIds = ["Future"] };
            editor.EnableAllSourcesCommand.Execute(null);
            Assert.IsTrue(SettingsManager.Current.LyricsSource.EnabledSourceIds!.Contains("Future"));
            editor.ResetSourceOrderCommand.Execute(null);
            CollectionAssert.AreEqual(LyricsSourceCatalog.DefaultOrder.ToArray(), editor.SourceEntries.Select(entry => entry.SourceId).ToArray());
            Assert.AreEqual(LyricsSourceAttemptMode.Parallel, editor.SourceAttemptMode);
            Assert.AreEqual(LyricsSourceResultMode.FirstAvailable, editor.SourceResultMode);
            context.Deactivate();
            editor.SourceAttemptMode = LyricsSourceAttemptMode.Sequential;
            Assert.AreEqual(LyricsSourceAttemptMode.Parallel, SettingsManager.Current.LyricsSource.AttemptMode);
            return Task.CompletedTask;
        });
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SettingsAndTrackChangesCancelOldRequestsWithoutBlockingTheirReplacements(bool useNetEase)
    {
        StaTest.Run(async dispatcher =>
        {
            var first = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var source = new Provider(LyricsSourceCatalog.QQMusic, _ => ++calls switch
            {
                1 => first.Task,
                2 => second.Task,
                _ => Task.FromResult<LyricsResult?>(Available("Latest"))
            });
            using var builder = new MediaSnapshotBuilder(new LyricsService(source));
            using var netEase = useNetEase ? new NetEaseMediaProvider(new LyricsService(source), dispatcher,
                () => throw new InvalidOperationException("测试不能读取播放器进程。")) : null;
            MediaSnapshot? latest = null;
            if (netEase is not null) netEase.SnapshotChanged += (_, snapshot) => latest = snapshot;
            LyricsResult? Fetch(string title)
            {
                if (netEase is null)
                    return builder.GetLyrics("session", "Player", _request with { Title = title });
                var info = new PlayerInfo
                {
                    Identity = title,
                    Title = title,
                    Artists = "Artist",
                    Album = "Album",
                    Cover = "",
                    Url = "",
                    Duration = 180,
                    Schedule = 0,
                    Pause = false
                };
                typeof(NetEaseMediaProvider).GetMethod("PublishPlayerInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(netEase, [info, DateTimeOffset.UtcNow, CancellationToken.None]);
                return latest?.Lyrics;
            }
            try
            {
                Fetch("First");
                var firstToken = source.Token;
                SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { AttemptMode = LyricsSourceAttemptMode.Parallel };
                Assert.IsTrue(firstToken.IsCancellationRequested);
                Fetch("First");
                Assert.AreEqual(2, calls, "旧在途标记不能阻止同曲按新规则获取。");
                var secondToken = source.Token;
                first.SetResult(Available("Old rule"));
                await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Fetch("Second");
                Assert.IsTrue(secondToken.IsCancellationRequested, "切歌应结束上一曲的手动查询链。");
                Assert.AreEqual(3, calls);
                second.SetResult(Available("Old track"));
                await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.AreEqual("Latest", Fetch("Second")?.Source);
                Assert.AreEqual(3, calls, "迟到清理不能移除替换请求或导致重复取词。");
            }
            finally
            {
                first.TrySetResult(null);
                second.TrySetResult(null);
            }
        });
    }

    private static LyricsResult Available(string source, int score = 10) =>
        new(source, new LyricDocument([new LyricLine(0, 1, "Lyric")], LyricsSyncType.LineSynced, "LRC")) { MatchScore = score };

    private sealed class Provider(string id, Func<CancellationToken, Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => id;
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            Started.TrySetResult();
            return retrieve(cancellationToken);
        }
    }
}
