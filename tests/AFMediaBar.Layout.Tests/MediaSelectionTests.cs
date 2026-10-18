// 使用可控候选和时间验证真实选择状态机；不启动 SMTC、播放器或平台探测。
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>播放活动、会话新增、手动覆盖和浏览器恢复的组合回归。</summary>
[TestClass]
public sealed class MediaSelectionTests
{
    [TestMethod]
    public void AutomaticFollowsNewPlaybackWithoutBeingStolenByPausedSessionsOrRefreshes() => Run(f =>
    {
        var video = Source("video", true, "chrome");
        var music = Source("music");
        Assert.AreEqual("video", f.Resolve(video)?.Key);
        Assert.AreEqual("video", f.Resolve(video, music)?.Key);
        music = music with { IsPlaying = true };
        Assert.AreEqual("music", f.Resolve(video, music)?.Key);
        Assert.AreEqual("music", f.Resolve(music, video)?.Key, "枚举顺序和重复播放通知不能重新排序。");
        video = video with { IsPlaying = false };
        Assert.AreEqual("music", f.Resolve(video, music)?.Key);
        video = video with { IsPlaying = true };
        Assert.AreEqual("video", f.Resolve(video, music)?.Key);
        Assert.AreEqual("video", f.Resolve(video with { IsPlaying = false }, music with { IsPlaying = false })?.Key);
    });

    [TestMethod]
    public void LatestSessionAcceptsPausedSourcesAndIgnoresPlaybackChanges() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.LatestSession);
        var first = Source("first", true);
        var later = Source("later");
        Assert.AreEqual("first", f.Resolve(first)?.Key);
        Assert.AreEqual("later", f.Resolve(first, later)?.Key);
        Assert.AreEqual("later", f.Resolve(first with { IsPlaying = false }, later)?.Key);
        Assert.AreEqual("later", f.Resolve(first, later)?.Key, "旧会话恢复播放不改变新增顺序。");
        var unavailable = Source("pending", false, recovering: true);
        Assert.AreEqual("later", f.Resolve(first, later, unavailable)?.Key);
        Assert.AreEqual("pending", f.Resolve(first, later, unavailable with { IsRecovering = false })?.Key);
    });

    [TestMethod]
    public void PreferredApplicationIncludesPausedSessionsAndFallsBackToAutomatic() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.PreferredApplication, "player");
        var video = Source("video", true);
        var preferred = Source("preferred", false, "player");
        var other = Source("other", true);
        Assert.AreEqual("video", f.Resolve(video)?.Key);
        Assert.AreEqual("preferred", f.Resolve(video, preferred)?.Key);
        Assert.AreEqual("preferred", f.Resolve(video, preferred, Source("second", true, "player"))?.Key,
            "同一优先应用的多个会话保留当前选择。");
        Assert.AreEqual("video", f.Resolve(video)?.Key);
        Assert.AreEqual("other", f.Resolve(video, other)?.Key);
        Assert.AreEqual("preferred", f.Resolve(video, other, preferred)?.Key);
    });

    [TestMethod]
    public void ManualChoiceSurvivesRefreshButLaterPlaybackCanTakeOver() => Run(f =>
    {
        var first = Source("first", true);
        var second = Source("second", true);
        f.Resolve(first);
        f.Resolve(first, second);
        Assert.IsTrue(f.Selection.Select("first", [first, second]));
        Assert.AreEqual("first", f.Resolve(second, first)?.Key);
        Assert.IsTrue(f.Selection.IsManualSelection);
        second = second with { IsPlaying = false };
        f.Resolve(first, second);
        Assert.AreEqual("second", f.Resolve(first, second with { IsPlaying = true })?.Key);
        Assert.IsFalse(f.Selection.IsManualSelection);
    });

    [TestMethod]
    public void LatestManualChoiceOnlyYieldsToNewSessions() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.LatestSession);
        var first = Source("first");
        var second = Source("second");
        f.Resolve(first);
        f.Resolve(first, second);
        f.Selection.Select("first", [first, second]);
        Assert.AreEqual("first", f.Resolve(first, second with { IsPlaying = true })?.Key);
        Assert.AreEqual("third", f.Resolve(first, second, Source("third"))?.Key);
    });

    [TestMethod]
    public void BrowserRestorationKeepsItsOrderAndDoesNotTakeBackTheMusic() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.LatestSession);
        var browser = Source("tab", true, "chrome");
        var music = Source("music", true);
        f.Resolve(browser);
        Assert.AreEqual("music", f.Resolve(browser, music)?.Key);
        f.Resolve(music);
        f.Now = f.Now.AddSeconds(1);
        var restored = browser with { Key = "restored", SessionKey = "restored" };
        Assert.AreEqual("music", f.Resolve(restored, music)?.Key);
        Assert.AreEqual("new-tab", f.Resolve(restored, music, Source("new-tab", false, "chrome"))?.Key,
            "新的独立标签页仍然是新增会话。");
    });

    [TestMethod]
    public void AmbiguousBrowserTabsAreNotMergedByApplicationIdentity() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.LatestSession);
        var music = Source("music");
        f.Resolve(Source("tab-a", true, "chrome"), Source("tab-b", true, "chrome"));
        f.Resolve(music);
        Assert.AreEqual("new-a", f.Resolve(music, Source("new-a", false, "chrome"), Source("new-b", false, "chrome"))?.Key);
    });

    [TestMethod]
    public void SelectedBrowserHasBoundedGraceAndRestoresManualChoice() => Run(f =>
    {
        var browser = Source("tab", true, "chrome");
        var other = Source("other");
        f.Resolve(browser, other);
        f.Selection.Select("tab", [browser, other]);
        Assert.IsNull(f.Resolve(other));
        Assert.IsTrue(f.Selection.IsMissingSessionGraceActive);
        f.Now = f.Now.AddSeconds(2);
        Assert.AreEqual("restored", f.Resolve(browser with { Key = "restored", SessionKey = "restored" }, other)?.Key);
        Assert.IsTrue(f.Selection.IsManualSelection);
        Assert.IsNull(f.Resolve(other));
        f.Now = f.Now.AddSeconds(3);
        Assert.AreEqual("other", f.Resolve(other)?.Key);
        Assert.IsFalse(f.Selection.IsMissingSessionGraceActive);
    });

    [TestMethod]
    public void ExpiredBrowserEvenWithTheSameKeyCountsAsNew() => Run(f =>
    {
        f.Settings = new(MediaSelectionMode.LatestSession);
        var browser = Source("tab", true, "chrome");
        var music = Source("music", true);
        f.Resolve(browser);
        f.Resolve(browser, music);
        f.Resolve(music);
        f.Now = f.Now.AddSeconds(4);
        Assert.AreEqual("tab", f.Resolve(browser, music)?.Key);
    });

    [TestMethod]
    public void RecoveringSourceDoesNotCreateAFakePlaybackRestart() => Run(f =>
    {
        var first = Source("first", true);
        var second = Source("second", true);
        f.Resolve(first);
        f.Resolve(first, second);
        Assert.AreEqual("second", f.Resolve(first, second with { IsPlaying = false, IsRecovering = true })?.Key);
        Assert.AreEqual("second", f.Resolve(first, second)?.Key);
        f.Resolve(first with { IsPlaying = false }, second);
        Assert.AreEqual("first", f.Resolve(first, second)?.Key);
    });

    [TestMethod]
    public void ManuallySelectedRecoveringSourceKeepsItsChoiceWhenItBecomesReadable() => Run(f =>
    {
        var playing = Source("playing", true);
        var pending = Source("pending", false, recovering: true);
        f.Resolve(playing);
        f.Resolve(playing, pending);
        f.Selection.Select("pending", [playing, pending]);
        Assert.AreEqual("pending", f.Resolve(playing, pending)?.Key);
        Assert.AreEqual("pending", f.Resolve(playing, pending with { IsRecovering = false })?.Key,
            "已手动选中的会话读到数据不属于另一个新来源。");
        Assert.IsTrue(f.Selection.IsManualSelection);
    });

    [TestMethod]
    public void ChangingPolicyReusesArrivalHistoryAndClearsManualOverride() => Run(f =>
    {
        var first = Source("first", true);
        var second = Source("second");
        f.Resolve(first);
        f.Resolve(first, second);
        f.Selection.Select("first", [first, second]);
        f.Settings = new(MediaSelectionMode.LatestSession);
        Assert.AreEqual("second", f.Resolve(first, second)?.Key);
        f.Settings = new(MediaSelectionMode.PreferredApplication, "first");
        Assert.AreEqual("first", f.Resolve(first, second)?.Key);
        f.Settings = MediaSelectionSettings.Default;
        Assert.AreEqual("first", f.Resolve(first, second)?.Key);
    });

    [TestMethod]
    public void InitialTiesUsePlayingThenSystemFocusAndStableKeys() => Run(f =>
    {
        f.Focus = "paused";
        Assert.AreEqual("a", f.Resolve(Source("z", true), Source("paused"), Source("a", true))?.Key);
        f.Selection.ClearSelection();
        f.Focus = "z";
        Assert.AreEqual("z", f.Resolve(Source("a", true), Source("z", true), Source("paused"))?.Key);
        Assert.AreEqual("z", f.Resolve(Source("a", true), Source("z", true))?.Key, "并列优先保留当前选择。");
    });

    private static MediaSourceCandidate Source(string key, bool playing = false, string? app = null, bool recovering = false) =>
        new(key, app ?? key, playing, key, recovering);

    private static void Run(Action<Fixture> assertions) => StaTest.Run(dispatcher =>
    {
        using var fixture = new Fixture(dispatcher);
        assertions(fixture);
        return Task.CompletedTask;
    });

    private sealed class Fixture : IDisposable
    {
        public DateTime Now { get; set; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        public string? Focus { get; set; }
        public MediaSelectionSettings Settings { get; set; } = MediaSelectionSettings.Default;
        public MediaSessionSelectionService Selection { get; }

        public Fixture(Dispatcher dispatcher) => Selection = new(() => Focus, dispatcher, () => Now);
        public MediaSourceCandidate? Resolve(params MediaSourceCandidate[] sessions) => Selection.Resolve(sessions, Settings);
        public void Dispose() => Selection.Dispose();
    }
}
