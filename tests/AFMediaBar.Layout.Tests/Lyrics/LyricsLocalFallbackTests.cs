// 验证本地来源相对在线来源的先后顺序，含「只在在线未命中时使用」的保守开关；不访问网络，结束时恢复全局设置。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>本地默认先于在线；保守模式下只有在线上全都空手而归时才读本地。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LyricsLocalFallbackTests
{
    private static readonly LyricsRequest Request = new("Track", "Artist", "Album", 180, null);

    private AppSettings _previous = null!;
    private AppSettings? _previousDefaults;

    [TestInitialize]
    public void Initialize()
    {
        _previous = SettingsManager.Current;
        _previousDefaults = SettingsManager.UserDefaults;
        SettingsManager.SetUserDefaults(null);
        SettingsManager.Current = new AppSettings { LyricsSource = LyricsSourceSettings.Default };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.Current = _previous;
        SettingsManager.SetUserDefaults(_previousDefaults);
    }

    [TestMethod]
    public async Task LocalRunsBeforeEveryOnlineSourceByDefault()
    {
        var local = Fixed(LyricsSourceCatalog.LocalFile, "Local");
        var online = Fixed(LyricsSourceCatalog.Lrclib, "Online");
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.AreEqual("Local", result?.Source);
        Assert.AreEqual(0, online.Calls, "本地命中时不应再联网。");
    }

    [TestMethod]
    public async Task FallbackOnlyKeepsTheOnlineResultAndNeverTouchesLocalFiles()
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { LocalFallbackOnly = true };
        var local = Fixed(LyricsSourceCatalog.LocalFile, "Local");
        var online = Fixed(LyricsSourceCatalog.Lrclib, "Online");
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.AreEqual("Online", result?.Source);
        Assert.AreEqual(0, local.Calls, "在线已给出结果时不能再用同名本地文件替换。");
    }

    [TestMethod]
    public async Task FallbackOnlyUsesLocalWhenEveryOnlineSourceComesBackEmpty()
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { LocalFallbackOnly = true };
        var local = Fixed(LyricsSourceCatalog.LocalFile, "Local");
        var online = Empty(LyricsSourceCatalog.Lrclib);
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.AreEqual("Local", result?.Source);
        Assert.AreEqual(1, online.Calls);
        Assert.AreEqual(1, local.Calls);
    }

    [TestMethod]
    public async Task FallbackOnlyStillReturnsNothingWhenNeitherOfThemFindsLyrics()
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with { LocalFallbackOnly = true };
        var local = Empty(LyricsSourceCatalog.LocalFile);
        var online = Empty(LyricsSourceCatalog.Lrclib);
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.IsNull(result);
        Assert.AreEqual(1, local.Calls, "在线全空之后才轮到本地，且只跑一次。");
    }

    [TestMethod]
    public async Task TitleOnlyLocalHitLetsOnlineLookupRunFirst()
    {
        // 只靠标题命中的本地文件（同名翻唱、同名片）不能短路在线搜索。
        var local = Fixed(LyricsSourceCatalog.LocalFile, "Local", lowConfidence: true);
        var online = Fixed(LyricsSourceCatalog.Lrclib, "Online");
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.AreEqual("Online", result?.Source);
        Assert.AreEqual(1, online.Calls);
    }

    [TestMethod]
    public async Task LowConfidenceLocalHitIsStillUsedWhenOnlineFindsNothing()
    {
        var local = Fixed(LyricsSourceCatalog.LocalFile, "Local", lowConfidence: true);
        var online = Empty(LyricsSourceCatalog.Lrclib);
        var result = await Create(local, online).GetLyricsAsync(Request, CancellationToken.None);
        Assert.AreEqual("Local", result?.Source);
        Assert.AreEqual(1, local.Calls);
    }

    private static LyricsService Create(Provider local, params Provider[] online) =>
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), local, online);

    private static Provider Fixed(string id, string source, bool lowConfidence = false) =>
        new(id, _ => Task.FromResult<LyricsResult?>(Available(source, lowConfidence)));

    private static Provider Empty(string id) => new(id, _ => Task.FromResult<LyricsResult?>(null));

    private static LyricsResult Available(string source, bool lowConfidence = false) =>
        new(source, new LyricDocument([new LyricLine(0, 1, "Lyric")], LyricsSyncType.LineSynced, "LRC"))
        {
            MatchScore = 100,
            LowConfidence = lowConfidence
        };

    private sealed class Provider(string id, Func<CancellationToken, Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => id;
        public int Calls { get; private set; }

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return retrieve(cancellationToken);
        }
    }
}
