using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词页来源列表的视图模型测试：默认状态、逐个启停、排序与恢复默认。
/// View-model tests for the lyrics page's source list: the default state, per-source toggling, ordering, and resetting.
/// </summary>
[TestClass]
public sealed class LyricsViewModelTests
{
    [TestInitialize]
    public void SetUp() => SettingsManager.ResetAll();

    [TestCleanup]
    public void TearDown() => SettingsManager.ResetAll();

    [TestMethod]
    public void DefaultStateListsEverySourceEnabledInTheDefaultOrder()
    {
        var viewModel = CreateViewModel();

        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.ToArray(),
            viewModel.SourceEntries.Select(entry => entry.SourceId).ToArray());
        Assert.IsTrue(viewModel.SourceEntries.All(entry => entry.IsEnabled));
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void DisablingOneSourcePersistsTheRemainingOrder()
    {
        var viewModel = CreateViewModel();
        var disabled = viewModel.SourceEntries[1];

        disabled.IsEnabled = false;

        var stored = SettingsManager.Current.LyricsSource.EnabledSourceIds;
        Assert.IsNotNull(stored);
        CollectionAssert.DoesNotContain(stored!.ToArray(), disabled.SourceId);
        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.Where(id => id != disabled.SourceId).ToArray(),
            stored.ToArray());

        // 关掉的那一行仍然留在列表里，否则用户没法把它开回来。
        // The row that was turned off stays in the list, otherwise the user could never turn it back on.
        Assert.AreEqual(LyricsSourceCatalog.DefaultOrder.Count, viewModel.SourceEntries.Count);
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void DisablingEverySourceIsStoredAsAnExplicitlyEmptyList()
    {
        var viewModel = CreateViewModel();

        foreach (var entry in viewModel.SourceEntries)
        {
            entry.IsEnabled = false;
        }

        var stored = SettingsManager.Current.LyricsSource.EnabledSourceIds;
        Assert.IsNotNull(stored, "关闭全部来源必须存成空数组，而不是「未配置」的 null。");
        Assert.AreEqual(0, stored!.Count);
        Assert.IsTrue(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void MovingASourceDownPersistsTheNewPriority()
    {
        var viewModel = CreateViewModel();
        var first = viewModel.SourceEntries[0];
        var second = viewModel.SourceEntries[1];

        viewModel.MoveSourceDownCommand.Execute(first);

        Assert.AreEqual(second.SourceId, viewModel.SourceEntries[0].SourceId);
        Assert.AreEqual(first.SourceId, viewModel.SourceEntries[1].SourceId);
        CollectionAssert.AreEqual(
            new[] { second.SourceId, first.SourceId },
            SettingsManager.Current.LyricsSource.EnabledSourceIds!.Take(2).ToArray());
    }

    [TestMethod]
    public void ResetOrderRestoresEverySourceAndTheUnconfiguredState()
    {
        var viewModel = CreateViewModel();
        viewModel.SourceEntries[0].IsEnabled = false;
        viewModel.MoveSourceDownCommand.Execute(viewModel.SourceEntries[2]);

        viewModel.ResetSourceOrderCommand.Execute(null);

        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.ToArray(),
            viewModel.SourceEntries.Select(entry => entry.SourceId).ToArray());
        Assert.IsTrue(viewModel.SourceEntries.All(entry => entry.IsEnabled));
        // "全部来源按默认顺序"写回"未配置"，这样以后新增的来源会自动生效。
        // "Every source in the default order" is stored as "never configured", so a source added later takes effect on its own.
        Assert.IsNull(SettingsManager.Current.LyricsSource.EnabledSourceIds);
    }

    [TestMethod]
    public void ExternalSourceChangeRebuildsTheListWithEveryRowVisible()
    {
        var viewModel = CreateViewModel();

        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([LyricsSourceCatalog.Kugou]));

        var entries = viewModel.SourceEntries;
        Assert.AreEqual(LyricsSourceCatalog.DefaultOrder.Count, entries.Count);
        Assert.AreEqual(LyricsSourceCatalog.Kugou, entries[0].SourceId);
        Assert.IsTrue(entries[0].IsEnabled);
        Assert.IsTrue(entries.Skip(1).All(entry => !entry.IsEnabled));
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    private static LyricsViewModel CreateViewModel() => new(new LocalizationService(), new StubSessionScanner());

    [DataTestMethod]
    [DataRow(LyricsQueryStrategy.Sequential)]
    [DataRow(LyricsQueryStrategy.Concurrent)]
    public async Task QueryStrategyFlowsFromViewModelIntoProviderDispatch(LyricsQueryStrategy strategy)
    {
        var viewModel = CreateViewModel();
        viewModel.ConcurrencyBatchSize = 3;
        viewModel.QueryStrategy = strategy == LyricsQueryStrategy.Sequential
            ? LyricsQueryStrategy.Concurrent : LyricsQueryStrategy.Sequential;
        viewModel.QueryStrategy = strategy;
        Assert.AreEqual(strategy, LyricsRetrievalOptions.FromSettings().QueryStrategy);

        var releaseFirst = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new DispatchProvider(LyricsSourceCatalog.QQMusic, () => releaseFirst.Task);
        var second = new DispatchProvider(LyricsSourceCatalog.Kugou, () => Task.FromResult<LyricsResult?>(null));
        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([first.SourceName, second.SourceName]));
        var service = new LyricsService(first, second);
        var retrieval = service.GetLyricsAsync(new LyricsRequest("Song", "Artist", "", null, null), CancellationToken.None);
        try
        {
            Assert.IsTrue(first.Started);
            Assert.AreEqual(strategy == LyricsQueryStrategy.Concurrent, second.Started,
                "Only concurrent mode should dispatch the second provider before the first finishes.");
        }
        finally
        {
            releaseFirst.TrySetResult(null);
            await retrieval;
        }
        Assert.IsTrue(second.Started);
    }

    private sealed class DispatchProvider(string name, Func<Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => name;
        public bool Started { get; private set; }
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Started = true;
            return retrieve();
        }
    }

    /// <summary>空会话扫描桩：绑定列表的刷新与来源列表共用全局设置，扫描本身另有并发测试覆盖。
    /// An empty scanner stub: the binding list's refresh shares the global settings with the source list, while the scan
    /// itself is covered by the concurrency tests.</summary>
    private sealed class StubSessionScanner : IMediaSessionSourceScanner
    {
        public IReadOnlyList<MediaSessionOption> CurrentSessionOptions { get; } = [];
    }
}
