// Apple Music 译名还原：候选校验与原名选择、两轮取舍、解析器缓存/冷却/限流/取消、取词协调与设置兼容。
// 曲库请求由伪造的 HttpMessageHandler 应答，不访问网络；修改全局设置的用例在清理时恢复。
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止译名还原错配曲目、产生额外联网，或拖住、覆盖首轮已可用的结果。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AppleMusicOriginalNamesTests
{
    private const string AppleMusic = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";
    private static readonly LyricsRequest _smtc = new("Matasaburo", "Yorushika", "Magic Lantern", 200, null, PlaybackSourceId: AppleMusic);

    private AppSettings _previous = null!;
    private AppSettings? _previousDefaults;

    [TestInitialize]
    public void Initialize()
    {
        _previous = SettingsManager.Current;
        _previousDefaults = SettingsManager.UserDefaults;
        SettingsManager.SetUserDefaults(null);
        SettingsManager.Current = new AppSettings();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.Current = _previous;
        SettingsManager.SetUserDefaults(_previousDefaults);
    }

    [TestMethod]
    public void CandidateWithAnotherArtistIsRejectedEvenWhenItsDurationIsCloser()
    {
        var wrong = Track("JP", 1, "又三郎", "別のバンド", 200);
        var right = Track("JP", 2, "又三郎", "ヨルシカ", 201);
        var lookups = Lookups(Track("US", 1, "Matasaburo", "Another Band", 200), Track("US", 2, "Matasaburo", "Yorushika", 201));

        var selected = AppleMusicCatalogPolicy.SelectCandidate(_smtc, [wrong, right], lookups);

        Assert.AreEqual(2, selected?.TrackId);
    }

    [TestMethod]
    public void EnglishTrackProducesNoOriginal()
    {
        var request = _smtc with { Title = "Shape of You", Artist = "Ed Sheeran" };
        var anchor = Track("JP", 1, "Shape of You", "Ed Sheeran");
        var names = Names(anchor, Track("US", 1, "Shape of You", "Ed Sheeran"), Track("CN", 1, "Shape of You", "Ed Sheeran"));

        Assert.IsNull(AppleMusicCatalogPolicy.SelectOriginal(request, anchor, names));
    }

    [TestMethod]
    public void KanjiOnlyJapaneseTitleFallsBackToTheJapaneseName()
    {
        // 曲名与歌手都没有假名；CN、TW、HK 只给出英文名时，回退到含汉字且与译名不同的 JP 名称。
        var request = _smtc with { Title = "Zankyosanka", Artist = "Aimer" };
        var anchor = Track("JP", 1, "残響散歌", "Aimer");
        var names = Names(anchor, Track("US", 1, "Zankyosanka", "Aimer"), Track("CN", 1, "Zankyosanka", "Aimer"),
            Track("TW", 1, "Zankyosanka", "Aimer"), Track("HK", 1, "Zankyosanka", "Aimer"));

        var original = AppleMusicCatalogPolicy.SelectOriginal(request, anchor, names);

        Assert.AreEqual("残響散歌", original?.Title);
        Assert.AreEqual("JP", original?.Region);
    }

    [TestMethod]
    public void FirstRoundWinsWhenNeitherRoundIsReliable()
    {
        var first = Online("First", 70);
        var second = Online("Second", 75);

        Assert.AreSame(first, AppleMusicCatalogPolicy.Pick(new(first, null, second, null)));
    }

    [TestMethod]
    public void CorroboratedSecondRoundLocalHitBeatsAReliableFirstRound()
    {
        var first = Online("First", 95);
        var local = Local(LyricsLowConfidenceReason.None, 90);

        Assert.AreSame(local, AppleMusicCatalogPolicy.Pick(new(first, null, null, local)));
    }

    [DataTestMethod]
    [DataRow(LyricsLowConfidenceReason.TitleOnly, "Local")]
    [DataRow(LyricsLowConfidenceReason.Contradicted, "Second")]
    public void SecondRoundKeepsTheLocalMarginRule(LyricsLowConfidenceReason reason, string expected)
    {
        var second = Online("Second", 60);
        var local = Local(reason, 100);

        Assert.AreEqual(expected, AppleMusicCatalogPolicy.Pick(new(null, null, second, local))?.Source);
    }

    [TestMethod]
    public async Task ResolverRestoresTheOriginalOnceAndThenUsesTheCache()
    {
        var catalog = Matasaburo();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var first = await resolver.ResolveAsync(_smtc, CancellationToken.None);
        var second = await resolver.ResolveAsync(_smtc, CancellationToken.None);

        Assert.AreEqual("又三郎", first?.Title);
        Assert.AreEqual("ヨルシカ", first?.Artist);
        Assert.AreEqual(first, second);
        Assert.AreEqual(3, catalog.Calls, "JP 区搜索加 US/CN 区 lookup，第二次走缓存。");
    }

    [TestMethod]
    public async Task ResolverSplitsTheAlbumOutOfTheAppleMusicArtistField()
    {
        // 实机：Apple Music 的 SMTC 歌手字段是「歌手 — 专辑」。拆开后才能通过校验，专辑再从同名候选里选中这张 EP。
        var request = new LyricsRequest("Her Blue Sky", "爱缪 — Her Blue Sky - EP", string.Empty, 304, null, PlaybackSourceId: AppleMusic);
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("空の青さを知る人よ", "あいみょん", 1, "おいしいパスタがあると聞いて", 305),
                Song("空の青さを知る人よ", "あいみょん", 2, "空の青さを知る人よ - EP", 305)],
            ("lookup", "cn") => [Song("Her Blue Sky", "爱缪", 1, "Oishii Pasta ga Aru to Kiite", 305),
                Song("Her Blue Sky", "爱缪", 2, "Her Blue Sky - EP", 305)],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("空の青さを知る人よ", original?.Title);
        Assert.AreEqual("あいみょん", original?.Artist);
        Assert.AreEqual("空の青さを知る人よ - EP", original?.Album);
    }

    [TestMethod]
    public async Task StaleDurationFallsBackToAStrictTitleAndArtistMatch()
    {
        // 实机：切歌瞬间 SMTC 还报上一首的 250 秒。时长都对不上时按无时长处理，曲名与歌手一致即可还原原名歌手。
        var request = new LyricsRequest("Clockwise", "Sayaka Matsuda — MementoMori Lament Collection Vol.1 (3)", string.Empty, 250, null,
            PlaybackSourceId: AppleMusic);
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("Clockwise", "松田彩花", 1, "メメントモリ Lament Collection Vol.1 (3)", 230)],
            _ => [Song("Clockwise", "Sayaka Matsuda", 1, "MementoMori Lament Collection Vol.1 (3)", 230)]
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("Clockwise", original?.Title);
        Assert.AreEqual("松田彩花", original?.Artist);
    }

    [TestMethod]
    public async Task StaleDurationStillConsidersTaiwanCandidates()
    {
        // JP 区只搜到无关曲目、华语原曲只在 TW 区，且时长是上一首的：两区结果合并后按严格规则校验。
        var request = new LyricsRequest("Tie Hua Fei", "Mili & Monster Siren Records — Tie Hua Fei - Single", string.Empty, 250, null,
            PlaybackSourceId: AppleMusic);
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("Another Song", "Another Artist", 9, "", 180)],
            ("search", "tw") => [Song("鐵花飛", "Mili & 塞壬唱片-MSR", 1, "", 200)],
            ("lookup", "us") => [Song("Tie Hua Fei", "Mili & Monster Siren Records", 1, "", 200)],
            ("lookup", "cn") => [Song("铁花飞", "Mili & 塞壬唱片-MSR", 1, "", 200)],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("铁花飞", original?.Title);
        Assert.AreEqual("CN", original?.Region);
    }

    [TestMethod]
    public async Task DurationMatchingJpDecoyDoesNotHideTheTaiwanOriginal()
    {
        // JP 区的时长相合候选可能只是无关曲目；它校验失败时仍要补搜 TW，不能被“JP 已有候选”挡住。
        var request = new LyricsRequest("Tie Hua Fei", "Mili & Monster Siren Records — Tie Hua Fei - Single", string.Empty, 200,
            null, PlaybackSourceId: AppleMusic);
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("Tie Hua Fei", "Wrong Artist", 9, "Tie Hua Fei - Single", 200)],
            ("search", "tw") => [Song("鐵花飛", "Mili & 塞壬唱片-MSR", 1, "Tie Hua Fei - Single", 200)],
            ("lookup", "us") => [Song("Tie Hua Fei", "Mili & Monster Siren Records", 1, "Tie Hua Fei - Single", 200)],
            ("lookup", "cn") => [Song("铁花飞", "Mili & 塞壬唱片-MSR", 1, "Tie Hua Fei - Single", 200)],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("铁花飞", original?.Title);
        Assert.AreEqual("CN", original?.Region);
    }

    [TestMethod]
    public async Task UnrelatedTrackMatchingTheStaleDurationDoesNotHideTheRealOne()
    {
        // 实机：切歌时还是上一首的 242 秒，JP 区搜索里恰好有一首 240 秒的无关曲目；它校验不通过后仍要找到 142 秒的《但》。
        var request = new LyricsRequest("Damn", "No Party for Cao Dong — The Clod", string.Empty, 242, null, PlaybackSourceId: AppleMusic);
        var unrelated = Song("Ain't Nobody Knows", "Hedgehogs", 9, "", 240);
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [unrelated, Song("但", "No Party For Cao Dong", 1, "瓦合", 142)],
            ("lookup", "us") => [unrelated, Song("Damn", "No Party for Cao Dong", 1, "The Clod", 142)],
            ("lookup", "cn") => [unrelated, Song("但", "草东没有派对", 1, "瓦合", 142)],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("但", original?.Title);
        Assert.AreEqual("草东没有派对", original?.Artist);
    }

    [TestMethod]
    public async Task ArtistTermWithNoResultsFallsBackToTheTitleOnlySearch()
    {
        // 实机：歌手是一长串角色名和声优，「曲名 歌手」在 JP、TW 区都零结果；只用曲名能搜到《よいまちカンターレ》。
        const string Artist = "コーロまちカド(シャミ子・桃・リリス・ミカン)/CV:小原好美・鬼頭明里・高橋未奈美・高柳知葉";
        var request = new LyricsRequest("Yoimachi Cantare", Artist + " — Machikado Tangent / Yoimachi Cantare - EP", string.Empty, 220, null,
            PlaybackSourceId: AppleMusic);
        var catalog = RespondByTerm((path, country, term) => (path, country, term) switch
        {
            ("search", "jp", "Yoimachi Cantare") => [Song("よいまちカンターレ", Artist, 1, "", 220)],
            ("lookup", _, _) => [Song("Yoimachi Cantare", Artist, 1, "", 220)],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var original = await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.AreEqual("よいまちカンターレ", original?.Title);
        Assert.AreEqual(6, catalog.Calls, "组合词 JP、TW 各一次零结果，曲名在 JP、TW 并发各一次，再 US/CN 各一次 lookup。");
    }

    [TestMethod]
    public async Task UnmatchedResultWithAStaleDurationIsNotCached()
    {
        // 时长对不上、严格校验也没通过：可能只是时长滞后，下一次带正确时长的请求必须重新查询。
        var request = _smtc with { DurationSeconds = 300 };
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("又三郎", "ヨルシカ")],
            ("lookup", _) => [Song("Matasaburo", "Yorushika Tribute")],
            _ => []
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        Assert.IsNull(await resolver.ResolveAsync(request, CancellationToken.None));
        Assert.IsNull(await resolver.ResolveAsync(request, CancellationToken.None));

        Assert.AreEqual(8, catalog.Calls, "每次都是 JP、TW 搜索加 US/CN lookup，未命中没有写入缓存。");
    }

    [TestMethod]
    public async Task ResolverSendsNothingDuringTheCooldownAfterTooManyRequests()
    {
        var catalog = new Catalog((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var clock = new TestClock();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, clock);

        Assert.IsNull(await resolver.ResolveAsync(_smtc, CancellationToken.None));
        Assert.IsNull(await resolver.ResolveAsync(_smtc, CancellationToken.None));
        Assert.AreEqual(1, catalog.Calls);

        clock.Advance(TimeSpan.FromMinutes(10));
        await resolver.ResolveAsync(_smtc, CancellationToken.None);
        Assert.AreEqual(2, catalog.Calls, "冷却结束后重新请求，429 没有写入缓存。");
    }

    [TestMethod]
    public async Task ResolverSkipsRequestsBeyondTheRateLimit()
    {
        // 没有歌手时只有一个搜索词，JP、TW 区都搜不到时每首 2 次请求：9 首用满 60 秒内的 18 次。
        var catalog = new Catalog((_, _) => Task.FromResult(Json("""{"resultCount":0,"results":[]}""")));
        var clock = new TestClock();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, clock);
        for (var index = 0; index < 9; index++)
            await resolver.ResolveAsync(_smtc with { Title = $"Song {index}", Artist = string.Empty }, CancellationToken.None);
        Assert.AreEqual(18, catalog.Calls);

        Assert.IsNull(await resolver.ResolveAsync(_smtc with { Title = "Song 9", Artist = string.Empty }, CancellationToken.None));
        Assert.AreEqual(18, catalog.Calls);

        clock.Advance(TimeSpan.FromSeconds(60));
        await resolver.ResolveAsync(_smtc with { Title = "Song 9", Artist = string.Empty }, CancellationToken.None);
        Assert.AreEqual(20, catalog.Calls, "超出限流时不写缓存，窗口过后照常请求。");
    }

    [TestMethod]
    public async Task ResolverCancelsTheRequestItselfWhenTheBudgetExpires()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = Hanging(cancelled);
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var thrown = false;
        try { await resolver.ResolveAsync(_smtc, budget.Token); }
        catch (OperationCanceledException) { thrown = true; }

        Assert.IsTrue(thrown, "调用方的取消向上抛出。");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [DataTestMethod]
    [DataRow("OtherPlayer")]
    [DataRow("ReliableFirstRound")]
    [DataRow("SwitchOff")]
    [DataRow("AllSourcesOff")]
    [DataRow("EmptyArtist")]
    [DataRow("ManualRule")]
    public async Task NoCatalogRequestOutsideTheTrigger(string scenario)
    {
        // EmptyArtist：Apple Music 加载下一首时 SMTC 先报「Connecting…」且歌手为空。
        var request = scenario switch
        {
            "OtherPlayer" => _smtc with { PlaybackSourceId = "cloudmusic.exe" },
            "EmptyArtist" => _smtc with { Title = "Connecting…", Artist = string.Empty },
            _ => _smtc
        };
        if (scenario == "SwitchOff")
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default with { AppleMusicOriginalNamesEnabled = false };
        if (scenario == "AllSourcesOff")
            SettingsManager.Current.LyricsSource = new LyricsSourceSettings([]);
        if (scenario == "ManualRule")
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default with { Rule = LyricsSourceRule.Manual };
        var preferred = new Source(LyricsSourceCatalog.QQMusic,
            (_, _) => Task.FromResult<LyricsResult?>(scenario == "ReliableFirstRound" ? Online("QQ", 90) : null));
        var catalog = Matasaburo();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        await Service(resolver, null, preferred).GetLyricsAsync(request, CancellationToken.None);

        Assert.AreEqual(0, catalog.Calls);
    }

    [TestMethod]
    public async Task PreferredNoLyricsAboveTheThresholdStillTriggersTheSecondRound()
    {
        var preferred = new Source(LyricsSourceCatalog.QQMusic, (request, _) => Task.FromResult<LyricsResult?>(
            request.Title == "又三郎" ? Online("Original", 95) : LyricsResult.NoLyrics("QQ", 90)));
        var catalog = Matasaburo();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var result = await Service(resolver, null, preferred).GetLyricsAsync(_smtc, CancellationToken.None);

        Assert.AreEqual("Original", result?.Source);
        CollectionAssert.AreEqual(new[] { "Matasaburo", "又三郎" }, preferred.Titles.ToArray());
    }

    [TestMethod]
    public async Task SecondRoundUsesTheCatalogDurationWhenThePlayerDurationIsStale()
    {
        // 实机：切歌瞬间 SMTC 还报上一首的 91 秒，曲库为 156 秒；第二轮按曲库时长评分，首轮仍用播放器原值。
        var catalog = Respond((path, country) => (path, country) switch
        {
            ("search", "jp") => [Song("又三郎", "ヨルシカ", seconds: 156)],
            ("lookup", _) => [Song("Matasaburo", "Yorushika", seconds: 156)],
            _ => []
        });
        var durations = new ConcurrentQueue<(string Title, double? Seconds)>();
        var preferred = new Source(LyricsSourceCatalog.QQMusic, (request, _) =>
        {
            durations.Enqueue((request.Title, request.DurationSeconds));
            return Task.FromResult<LyricsResult?>(null);
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        await Service(resolver, null, preferred).GetLyricsAsync(_smtc with { DurationSeconds = 91 }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Matasaburo", (double?)91), ("又三郎", (double?)156) }, durations.ToArray());
    }

    [TestMethod]
    public async Task StuckResolverReturnsTheFirstRoundWithinTheDeadline()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = Hanging(cancelled);
        var fallback = new Source(LyricsSourceCatalog.Lrclib, (_, _) => Task.FromResult<LyricsResult?>(Online("First", 60)));
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());
        var started = DateTime.UtcNow;

        var result = await Service(resolver, null, Empty(LyricsSourceCatalog.QQMusic), fallback).GetLyricsAsync(_smtc, CancellationToken.None);

        Assert.AreEqual("First", result?.Source);
        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(3), "解析预算 300 ms，不应等到单源预算。");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ReliableFirstRoundCancelsTheSecondRound()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Catalog(async (_, token) =>
        {
            requested.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return Json("{}");
        });
        var preferred = new Source(LyricsSourceCatalog.QQMusic, (_, _) => Task.FromResult<LyricsResult?>(Online("QQ", 50)));
        // 等第二轮的请求发出后首轮才可靠，确保验证的是“取消进行中的第二轮”。
        var fallback = new Source(LyricsSourceCatalog.Lrclib, async (_, _) =>
        {
            await requested.Task;
            return Online("First", 95);
        });
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());
        var service = new LyricsService(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(10), null, resolver,
            preferred, fallback);

        var result = await service.GetLyricsAsync(_smtc, CancellationToken.None);

        Assert.AreEqual("First", result?.Source);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task FallbackOnlyLocalRunsAfterBothRoundsAndMatchesBothNames()
    {
        SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default with { LocalEnabled = true, LocalFallbackOnly = true };
        var events = new ConcurrentQueue<string>();
        var preferred = new Source(LyricsSourceCatalog.QQMusic, (request, _) =>
        {
            events.Enqueue("online:" + request.Title);
            return Task.FromResult<LyricsResult?>(null);
        });
        var local = new Source(LyricsSourceCatalog.LocalFile, (request, _) =>
        {
            events.Enqueue("local:" + request.Title);
            return Task.FromResult<LyricsResult?>(request.Title == "又三郎" ? Local(LyricsLowConfidenceReason.None, 95) : null);
        });
        var catalog = Matasaburo();
        using var client = new HttpClient(catalog);
        using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

        var result = await Service(resolver, local, preferred).GetLyricsAsync(_smtc, CancellationToken.None);

        Assert.AreEqual("Local", result?.Source);
        CollectionAssert.AreEqual(new[] { "online:Matasaburo", "online:又三郎", "local:Matasaburo", "local:又三郎" }, events.ToArray());
    }

    [TestMethod]
    public async Task DefaultLocalModeMatchesTheOriginalNameOnTheFirstScan()
    {
        // 本地文件按原名命名：首轮用译名扫不到，第二轮在同一份目录清单上用原名命中。
        var folder = Path.Combine(Path.GetTempPath(), "afmb-apple-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "ヨルシカ - 又三郎.lrc"), "[ar:ヨルシカ]\n[ti:又三郎]\n[00:01.00]Lyric");
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default with { LocalEnabled = true, LocalFolders = [folder] };
            var catalog = Matasaburo();
            using var client = new HttpClient(catalog);
            using var resolver = new AppleMusicOriginalMetadataResolver(null, client, new TestClock());

            var result = await Service(resolver, new LocalFileLyricsProvider(), Empty(LyricsSourceCatalog.QQMusic))
                .GetLyricsAsync(_smtc, CancellationToken.None);

            Assert.AreEqual(LyricsSourceCatalog.LocalFile, result?.Source);
            Assert.IsFalse(result!.LowConfidence);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public void MissingFieldReadsAsEnabledAndExplicitOffSurvivesARoundTrip()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var legacy = JsonSerializer.Deserialize<AppSettings>("""{"lyricsSource":{"enabledSourceIds":null,"localEnabled":true}}""", options)!;
        var disabled = new AppSettings { LyricsSource = LyricsSourceSettings.Default with { AppleMusicOriginalNamesEnabled = false } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(disabled, options), options)!;

        Assert.IsTrue(legacy.LyricsSource.AppleMusicOriginalNamesEnabled);
        Assert.IsFalse(restored.LyricsSource.AppleMusicOriginalNamesEnabled);
    }

    private static LyricsService Service(AppleMusicOriginalMetadataResolver resolver, ILyricsProvider? local, params ILyricsProvider[] online) =>
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(300), local, resolver, online);

    private static AppleMusicTrack Track(string region, long id, string name, string artist, double seconds = 200) =>
        new(region, id, name, artist, string.Empty, seconds, "J-Pop");

    private static Dictionary<string, AppleMusicTrack> Names(params AppleMusicTrack[] tracks) =>
        tracks.ToDictionary(track => track.Region, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlyList<AppleMusicTrack>> Lookups(params AppleMusicTrack[] tracks) =>
        tracks.GroupBy(track => track.Region).ToDictionary(group => group.Key, group => (IReadOnlyList<AppleMusicTrack>)group.ToArray());

    private static LyricsResult Online(string source, int score) =>
        new(source, new LyricDocument([new LyricLine(0, 1, "Lyric")], LyricsSyncType.LineSynced, "LRC")) { MatchScore = score };

    private static LyricsResult Local(LyricsLowConfidenceReason reason, int score) =>
        Online("Local", score) with { LowConfidence = reason != LyricsLowConfidenceReason.None, LowConfidenceReason = reason };

    private static Source Empty(string id) => new(id, (_, _) => Task.FromResult<LyricsResult?>(null));

    /// <summary>按 issue 实测的「又三郎」应答：JP 区搜索给出原名，US/CN 区 lookup 给出与 SMTC 一致的译名。</summary>
    private static Catalog Matasaburo() => Respond((path, country) => (path, country) switch
    {
        ("search", "jp") => [Song("又三郎", "ヨルシカ")],
        ("lookup", "us" or "cn") => [Song("Matasaburo", "Yorushika")],
        _ => []
    });

    /// <summary>按接口路径与 country 参数应答固定的歌曲列表。</summary>
    private static Catalog Respond(Func<string, string?, string[]> songs) =>
        RespondByTerm((path, country, _) => songs(path, country));

    /// <summary>按接口路径、country 与搜索词应答固定的歌曲列表。</summary>
    private static Catalog RespondByTerm(Func<string, string?, string?, string[]> songs) => new((request, _) =>
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        var found = songs(path, query["country"], query["term"]);
        return Task.FromResult(Json($$"""{"resultCount":{{found.Length}},"results":[{{string.Join(",", found)}}]}"""));
    });

    private static string Song(string name, string artist, long id = 1, string album = "", int seconds = 200) =>
        $$"""{"wrapperType":"track","kind":"song","trackId":{{id}},"trackName":"{{name}}","artistName":"{{artist}}","collectionName":"{{album}}","trackTimeMillis":{{seconds * 1000}},"primaryGenreName":"J-Pop"}""";

    /// <summary>请求一直挂起，直到令牌取消；取消时记录到 <paramref name="cancelled"/>。</summary>
    private static Catalog Hanging(TaskCompletionSource cancelled) => new(async (_, token) =>
    {
        try { await Task.Delay(Timeout.Infinite, token); }
        catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
        return Json("{}");
    });

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Catalog(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return respond(request, cancellationToken);
        }
    }

    private sealed class Source(string id, Func<LyricsRequest, CancellationToken, Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => id;
        public ConcurrentQueue<string> Titles { get; } = new();

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Titles.Enqueue(request.Title);
            return retrieve(request, cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
