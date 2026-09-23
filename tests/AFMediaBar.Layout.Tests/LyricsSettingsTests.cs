using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词设置、来源选择、匹配严格度、占位文本与缓存失效的测试。
/// Tests for the lyric settings, source selection, match strictness, placeholder text, and cache invalidation.
/// </summary>
[TestClass]
public sealed class LyricsSettingsTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "AFMediaBarTests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void TearDown()
    {
        SettingsManager.ResetAll();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    // ---- 缺字段的默认值 / missing-field defaults ----

    [TestMethod]
    public void MissingLyricFieldsUseTheDocumentedDefaults()
    {
        // 文件里没有五项取词与擦亮设置：每一项都必须取文档化的默认值，尤其是来源列表——null 表示"全部来源"，
        // 绝不能读成"一首歌都取不到歌词"的空列表。
        // The file carries none of the five lyric and highlight settings, so each one must take its documented default — especially the
        // source list, where null means "all sources" and must never read as the empty list that fetches no lyrics at all.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"lyricsEnabled\":false,\"twoLineLyricsEnabled\":true}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.LyricsSyllableHighlightEnabled);
        Assert.AreEqual(LyricsUnsungOpacity.DefaultPercent, SettingsManager.Current.LyricsUnsungOpacityPercent);
        Assert.IsTrue(SettingsManager.Current.LyricsInfoLineFilterEnabled);
        Assert.AreEqual(LyricsMatchStrictness.Balanced, SettingsManager.Current.LyricsMatchStrictness);
        Assert.IsNull(SettingsManager.Current.LyricsSource.EnabledSourceIds);
        // 间距字段同样取默认 0：升级后的外观与升级前完全一致。
        // The spacing fields take their zero defaults as well: the look after upgrading matches the look before exactly.
        Assert.AreEqual(LyricsLineGap.DefaultDip, SettingsManager.Current.LyricsLineGapDip);
        Assert.AreEqual(LyricsCharacterSpacing.DefaultPercent, SettingsManager.Current.LyricsCharacterSpacingPercent);

        // 文件里已有的歌词字段照旧保留。
        // Lyric fields the file does carry survive untouched.
        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
        Assert.IsTrue(SettingsManager.Current.TwoLineLyricsEnabled);
    }

    [TestMethod]
    public void UnsungOpacityIsClampedAndSnappedOnLoad()
    {
        SettingsManager.Current.LyricsUnsungOpacityPercent = 999;
        Assert.AreEqual(LyricsUnsungOpacity.MaximumPercent, SettingsManager.Current.Normalize().LyricsUnsungOpacityPercent);

        SettingsManager.Current.LyricsUnsungOpacityPercent = 3;
        Assert.AreEqual(LyricsUnsungOpacity.MinimumPercent, SettingsManager.Current.Normalize().LyricsUnsungOpacityPercent);

        // 吸附到 5 的网格：43% 落到 45%，不是原样保留。
        // Snapped onto the step grid: 43% lands on 45% instead of staying as it is.
        SettingsManager.Current.LyricsUnsungOpacityPercent = 43;
        Assert.AreEqual(45, SettingsManager.Current.Normalize().LyricsUnsungOpacityPercent);
    }

    [TestMethod]
    public void SpacingSettingsAreClampedAndSnappedOnLoad()
    {
        SettingsManager.Current.LyricsLineGapDip = 999;
        SettingsManager.Current.LyricsCharacterSpacingPercent = 999;
        var normalized = SettingsManager.Current.Normalize();
        Assert.AreEqual(LyricsLineGap.MaximumDip, normalized.LyricsLineGapDip);
        Assert.AreEqual(LyricsCharacterSpacing.MaximumPercent, normalized.LyricsCharacterSpacingPercent);

        SettingsManager.Current.LyricsLineGapDip = -3;
        SettingsManager.Current.LyricsCharacterSpacingPercent = -3;
        normalized = SettingsManager.Current.Normalize();
        Assert.AreEqual(LyricsLineGap.MinimumDip, normalized.LyricsLineGapDip);
        Assert.AreEqual(LyricsCharacterSpacing.MinimumPercent, normalized.LyricsCharacterSpacingPercent);

        // 两个字段的步长都是 1，因此区间内的值原样保留（字距在连续渲染下每 1% 都有实际变化）。
        // Both fields step by one, so in-range values stay untouched (with continuous rendering every one-percent step is effective).
        SettingsManager.Current.LyricsLineGapDip = 5;
        SettingsManager.Current.LyricsCharacterSpacingPercent = 9;
        normalized = SettingsManager.Current.Normalize();
        Assert.AreEqual(5, normalized.LyricsLineGapDip);
        Assert.AreEqual(9, normalized.LyricsCharacterSpacingPercent);

        // 克隆必须带上这两个字段，否则设置保存会把它们丢掉。
        // Cloning has to carry both fields, or saving the settings would drop them.
        var clone = SettingsManager.Current.Clone();
        Assert.AreEqual(5, clone.LyricsLineGapDip);
        Assert.AreEqual(9, clone.LyricsCharacterSpacingPercent);
    }

    [TestMethod]
    public void ResetLyricsClearsBothSpacingFields()
    {
        SettingsManager.SetLyricsLineGapDip(8);
        SettingsManager.SetLyricsCharacterSpacingPercent(20);

        SettingsManager.ResetLyrics();

        Assert.AreEqual(LyricsLineGap.DefaultDip, SettingsManager.Current.LyricsLineGapDip);
        Assert.AreEqual(LyricsCharacterSpacing.DefaultPercent, SettingsManager.Current.LyricsCharacterSpacingPercent);
    }

    // ---- 来源设置与选择 ----

    [TestMethod]
    public void SourceSettingsKeepTheThreeDistinctMeanings()
    {
        Assert.IsNull(new LyricsSourceSettings(null).Normalize().EnabledSourceIds);
        Assert.AreEqual(0, new LyricsSourceSettings([]).Normalize().EnabledSourceIds!.Count);

        var cleaned = new LyricsSourceSettings(["  QQMusic  ", "QQMusic", " ", "Kugou"]).Normalize();
        CollectionAssert.AreEqual(new[] { "QQMusic", "Kugou" }, cleaned.EnabledSourceIds!.ToArray());
    }

    [TestMethod]
    public void NeverConfiguredUsesEverySourceInTheDefaultOrder()
    {
        var active = LyricsSourcePolicy.ResolveActive(Providers(), LyricsSourceSettings.Default);

        CollectionAssert.AreEqual(LyricsSourceCatalog.DefaultOrder.ToArray(), active.Select(p => p.SourceName).ToArray());
    }

    [TestMethod]
    public void ExplicitOrderIsHonouredAndUnknownIdsAreIgnored()
    {
        var active = LyricsSourcePolicy.ResolveActive(
            Providers(),
            new LyricsSourceSettings([LyricsSourceCatalog.Kugou, "RemovedSource", LyricsSourceCatalog.NetEase]));

        CollectionAssert.AreEqual(
            new[] { LyricsSourceCatalog.Kugou, LyricsSourceCatalog.NetEase },
            active.Select(p => p.SourceName).ToArray());
    }

    [TestMethod]
    public void DuplicateIdsDoNotDuplicateProviders()
    {
        var active = LyricsSourcePolicy.ResolveActive(
            Providers(),
            new LyricsSourceSettings([LyricsSourceCatalog.Lrclib, LyricsSourceCatalog.Lrclib]));

        Assert.AreEqual(1, active.Count);
        Assert.AreEqual(LyricsSourceCatalog.Lrclib, active[0].SourceName);
    }

    [TestMethod]
    public void ExplicitlyDisablingEverySourceReturnsNothing()
    {
        var active = LyricsSourcePolicy.ResolveActive(Providers(), new LyricsSourceSettings([]));

        Assert.AreEqual(0, active.Count);
    }

    [TestMethod]
    public async Task DisabledSourcesAreNotCalledAtAll()
    {
        var asked = new List<string>();
        var stubA = new StubProvider(LyricsSourceCatalog.NetEase, _ =>
        {
            asked.Add(LyricsSourceCatalog.NetEase);
            return Task.FromResult<LyricsResult?>(null);
        });
        var stubB = new StubProvider(LyricsSourceCatalog.QQMusic, _ =>
        {
            asked.Add(LyricsSourceCatalog.QQMusic);
            return Task.FromResult<LyricsResult?>(new LyricsResult(LyricsSourceCatalog.QQMusic, LyricDocument.Empty));
        });
        var service = new LyricsService(
            LyricsService.DefaultPerSourceBudget,
            LyricsService.DefaultTotalBudget,
            stubA,
            stubB);
        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([LyricsSourceCatalog.QQMusic]));

        var result = await service.GetLyricsAsync(
            new LyricsRequest("Song", "Artist", "Album", 200, NetEaseSongId: null),
            CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { LyricsSourceCatalog.QQMusic }, asked);
    }

    [TestMethod]
    public async Task DisabledEverywhereMeansNoRequestIsMade()
    {
        var asked = new List<string>();
        var service = new LyricsService(
            LyricsService.DefaultPerSourceBudget,
            LyricsService.DefaultTotalBudget,
            new StubProvider(LyricsSourceCatalog.NetEase, _ =>
            {
                asked.Add(LyricsSourceCatalog.NetEase);
                return Task.FromResult<LyricsResult?>(null);
            }));
        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([]));

        var result = await service.GetLyricsAsync(
            new LyricsRequest("Song", "Artist", "Album", 200, NetEaseSongId: null),
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(0, asked.Count);
    }

    // ---- 匹配严格度 ----

    [TestMethod]
    public void MatchStrictnessMapsOntoTheLibraryLevels()
    {
        Assert.AreEqual(
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.High,
            LyricsMatchPolicy.ToMinimumMatch(LyricsMatchStrictness.Balanced));
        Assert.AreEqual(
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.VeryHigh,
            LyricsMatchPolicy.ToMinimumMatch(LyricsMatchStrictness.Strict));
        Assert.AreEqual(
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.Perfect,
            LyricsMatchPolicy.ToMinimumMatch(LyricsMatchStrictness.Exact));
    }

    // ---- 占位文本 ----

    [TestMethod]
    public void PlaceholderTextIsRecognized()
    {
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("暂无歌词"));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder(" 暫無歌詞 "));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("纯音乐，请欣赏"));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("此歌曲为没有填词的纯音乐，请您欣赏"));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("Instrumental"));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("No lyrics available"));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder(null));
        Assert.IsTrue(LyricPlaceholderPolicy.IsPlaceholder("   "));
    }

    [TestMethod]
    public void RealLyricsAreNotTreatedAsPlaceholders()
    {
        Assert.IsFalse(LyricPlaceholderPolicy.IsPlaceholder("我保证你会发现我是无可替代的"));
        Assert.IsFalse(LyricPlaceholderPolicy.IsPlaceholder("I promise that you'll never find another like me"));
        // 含"歌词"二字但明显是正文的长句不能被误判。
        // A long line that merely mentions the word 歌词 must not be caught.
        Assert.IsFalse(LyricPlaceholderPolicy.IsPlaceholder("这首歌的歌词写的是一个人在深夜里的自言自语与自我和解"));
    }

    // ---- 缓存失效 ----

    [TestMethod]
    public void OnlyRetrievalSettingsClearTheLyricCache()
    {
        Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsSource), null));
        Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsMatchStrictness), null));
        Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsInfoLineFilterEnabled), null));
        Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(null, SettingsResetScope.Lyrics));
        Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(null, SettingsResetScope.All));

        // 呈现类设置不该触发重新取词，否则拖动不透明度滑杆会每一步都发一次网络请求。
        // Presentation settings must not refetch, or dragging the opacity slider would send a request per step.
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsUnsungOpacityPercent), null));
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsSyllableHighlightEnabled), null));
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsSecondaryLine), null));
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsTextAlignment), null));
        // 间距同样属于呈现：拖动行距或字距滑杆不该重新取词。
        // Spacing is presentation as well: dragging either spacing slider must not refetch lyrics.
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsLineGapDip), null));
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsCharacterSpacingPercent), null));
        Assert.IsFalse(LyricsCacheInvalidationPolicy.ShouldClearCache(null, SettingsResetScope.Appearance));
    }

    // ---- 解析与呈现 ----

    [TestMethod]
    public void PlaceholderOnlyLyricsProduceNoDisplayableLines()
    {
        var document = LyricsTextParser.Parse("[00:00.00]暂无歌词", request: Request());

        Assert.AreEqual(0, document.Lines.Count);
    }

    [TestMethod]
    public void CreditLinesSurviveWhenFilteringIsTurnedOff()
    {
        var raw = "[00:00.00]作词 : 张三\n[00:00.50]作曲 : 李四\n[00:02.00]第一句歌词\n[00:05.00]第二句歌词";

        var filtered = LyricsTextParser.Parse(raw, request: Request(), filterInfoLines: true);
        var kept = LyricsTextParser.Parse(raw, request: Request(), filterInfoLines: false);

        Assert.AreEqual(2, filtered.Lines.Count);
        Assert.AreEqual(4, kept.Lines.Count);
        Assert.AreEqual("作词 : 张三", kept.Lines[0].Text);
    }

    [TestMethod]
    public void PresenterExposesRomanizationForTheSecondLine()
    {
        var document = LyricsTextParser.Parse(
            "[00:01.00]今天我 寒夜里看雪飘过",
            romanizationText: "[00:01.00]gam tin o hon yei lei hon sv piu guo",
            request: Request());
        var presenter = new LyricLinePresenter();

        var update = presenter.Update(new LyricsResult(LyricsSourceCatalog.NetEase, document), 1.2);

        Assert.AreEqual("今天我 寒夜里看雪飘过", update.Text);
        Assert.AreEqual("gam tin o hon yei lei hon sv piu guo", update.RomanizationText);
    }

    private static LyricsRequest Request() => new("Song", "Artist", "Album", 200, NetEaseSongId: null);

    private static IReadOnlyList<ILyricsProvider> Providers() =>
    [
        new StubProvider(LyricsSourceCatalog.NetEase),
        new StubProvider(LyricsSourceCatalog.NetEaseSearch),
        new StubProvider(LyricsSourceCatalog.Lrclib),
        new StubProvider(LyricsSourceCatalog.QQMusic),
        new StubProvider(LyricsSourceCatalog.Kugou),
        new StubProvider(LyricsSourceCatalog.SodaMusic)
    ];

    private sealed class StubProvider(
        string sourceName,
        Func<CancellationToken, Task<LyricsResult?>>? handler = null) : ILyricsProvider
    {
        public string SourceName { get; } = sourceName;

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken) =>
            handler is null ? Task.FromResult<LyricsResult?>(null) : handler(cancellationToken);
    }
}
