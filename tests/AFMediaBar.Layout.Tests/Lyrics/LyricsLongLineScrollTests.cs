// 验证长歌词封顶策略、逐字时间轴进度与擦亮开关的解耦；不连接歌词来源。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>长歌词动态滚动的宽度封顶、字号缩放与滚动/擦亮解耦回归。</summary>
[TestClass, DoNotParallelize]
public sealed class LyricsLongLineScrollTests
{
    [TestMethod]
    public void MaximumWidthScalesWithFontSize()
    {
        // 封顶按全角字个数折算：16 个全角字 ≈ 16 em，随字号等比缩放。
        Assert.AreEqual(192, LyricsLongLineScroll.ResolveMaximumDip(12), 0.000001);
        Assert.AreEqual(208, LyricsLongLineScroll.ResolveMaximumDip(13), 0.000001);
        Assert.AreEqual(320, LyricsLongLineScroll.ResolveMaximumDip(20), 0.000001);
        Assert.AreEqual(0, LyricsLongLineScroll.ResolveMaximumDip(0), 0.000001);
        Assert.AreEqual(0, LyricsLongLineScroll.ResolveMaximumDip(-5), 0.000001);
    }

    [TestMethod]
    public void FollowContentWidthCapsOnlySyllableTimedLinesWhileEnabled()
    {
        const double FontSize = 12;
        var cap = LyricsLongLineScroll.ResolveMaximumDip(FontSize);
        Assert.AreEqual(400, LyricsLongLineScroll.ResolveFollowContentWidth(400, enabled: false, hasSyllableTimeline: true, FontSize), 0.000001,
            "开关关闭时不封顶：长歌词继续撑开媒体栏，维持原有行为。");
        Assert.AreEqual(400, LyricsLongLineScroll.ResolveFollowContentWidth(400, enabled: true, hasSyllableTimeline: false, FontSize), 0.000001,
            "行级歌词没有逐字时间轴，Web 层无法滚动，因此不封顶。");
        Assert.AreEqual(100, LyricsLongLineScroll.ResolveFollowContentWidth(100, enabled: true, hasSyllableTimeline: true, FontSize), 0.000001,
            "未超过封顶时原样返回，媒体栏照旧跟随内容。");
        Assert.AreEqual(cap, LyricsLongLineScroll.ResolveFollowContentWidth(900, enabled: true, hasSyllableTimeline: true, FontSize), 0.000001,
            "超过封顶时取封顶值，超出部分交给 Web 层横向滚动。");
    }

    [TestMethod]
    public void FollowContentWidthPairCapsOnlyThePrimaryLine()
    {
        const double FontSize = 12;
        var cap = LyricsLongLineScroll.ResolveMaximumDip(FontSize);

        // 回归（用户实机发现）：译文/次行超过封顶时不得被一起压窄——它没有逐字时间轴、也不会随进度滚动，
        // 封顶只会把它截断（右侧被省略号裁掉），因此保持自然宽度，媒体栏跟随次行。
        Assert.AreEqual(500, LyricsLongLineScroll.ResolveFollowContentWidthPair(300, 500, enabled: true, hasSyllableTimeline: true, FontSize), 0.000001,
            "译文超过封顶时保持自然宽度：次行不滚动，封顶只会截断它。");
        Assert.AreEqual(cap, LyricsLongLineScroll.ResolveFollowContentWidthPair(900, 100, enabled: true, hasSyllableTimeline: true, FontSize), 0.000001,
            "次行更短时取封顶后的主行，超出部分交给 Web 层横向滚动。");
        Assert.AreEqual(480, LyricsLongLineScroll.ResolveFollowContentWidthPair(900, 480, enabled: true, hasSyllableTimeline: true, FontSize), 0.000001,
            "两行都超过封顶时取自然宽度的次行：次行不滚动，必须完整可见。");
        Assert.AreEqual(900, LyricsLongLineScroll.ResolveFollowContentWidthPair(900, 300, enabled: false, hasSyllableTimeline: true, FontSize), 0.000001,
            "开关关闭时两行都不封顶，与改动前行为一致。");
        Assert.AreEqual(500, LyricsLongLineScroll.ResolveFollowContentWidthPair(500, 300, enabled: true, hasSyllableTimeline: false, FontSize), 0.000001,
            "行级歌词没有逐字时间轴，两行都不封顶。");
    }

    [TestMethod]
    public void ProjectionKeepsScrollProgressWhenHighlightIsOff()
    {
        var line = new LyricLine(0, 5, "夢なんて叶うわけない") { Words = [new(0, 1, "夢"), new(1, 2, "なんて"), new(2, 5, "叶うわけない")] };
        var settings = new AppSettings { LyricsSyllableHighlightEnabled = false };
        var frame = LyricsPresentationProjector.Project(Snapshot([line], 1.5), settings, DateTimeOffset.UtcNow);
        Assert.IsNull(frame.WordScanProgress, "关闭擦亮后不得再发送擦亮进度。");
        Assert.IsNotNull(frame.ScrollProgress, "关闭擦亮后滚动进度必须照常发送，否则长歌词失去横向滚动。");
        // 1.5s 落在第二个词（1–2s）正中：已唱权重 = 1 + 3×0.5 = 2.5，总权重 10。
        Assert.AreEqual(0.25, frame.ScrollProgress!.Value, 0.000001);

        settings.LyricsSyllableHighlightEnabled = true;
        var highlighted = LyricsPresentationProjector.Project(Snapshot([line], 1.5), settings, DateTimeOffset.UtcNow);
        Assert.AreEqual(frame.ScrollProgress, highlighted.ScrollProgress, "开关两侧的滚动轨迹必须一致。");
        Assert.AreEqual(highlighted.ScrollProgress, highlighted.WordScanProgress, "开启擦亮时滚动与擦亮共用同一轨迹。");
    }

    [TestMethod]
    public void ProjectionHasNoScrollProgressForLineLevelLyrics()
    {
        var line = new LyricLine(0, 5, "行级歌词没有逐字时间轴");
        var settings = new AppSettings { LyricsSyllableHighlightEnabled = false };
        var frame = LyricsPresentationProjector.Project(Snapshot([line], 2.5), settings, DateTimeOffset.UtcNow);
        Assert.IsNull(frame.WordScanProgress);
        Assert.IsNull(frame.ScrollProgress, "行级歌词不得产生滚动进度：没有逐字时间轴就无法按演唱进度滚动。");
    }

    [TestMethod]
    public void SettingDefaultsToOnAndSurvivesCloning()
    {
        Assert.IsTrue(new AppSettings().LyricsLongLineScrollEnabled, "长歌词动态滚动默认开启。");
        var settings = new AppSettings { LyricsLongLineScrollEnabled = false };
        Assert.IsFalse(settings.Clone().LyricsLongLineScrollEnabled);
        settings.LyricsLongLineScrollEnabled = true;
        Assert.IsTrue(settings.Clone().LyricsLongLineScrollEnabled);
    }

    private static MediaSnapshot Snapshot(IReadOnlyList<LyricLine> lines, double position) => MediaSnapshot.Disconnected with
    {
        IsConnected = true,
        Lyrics = new LyricsResult("test", new LyricDocument(lines, LyricsSyncType.MixedSynced, "test")),
        Position = position,
        Duration = 30
    };
}
