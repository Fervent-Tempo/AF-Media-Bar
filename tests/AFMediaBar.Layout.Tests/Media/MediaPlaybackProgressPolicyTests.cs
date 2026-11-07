// 验证“播放器不提供播放进度”的判断；纯策略，不依赖界面、SMTC 或设置存储。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>没有播放进度时的提示条件。</summary>
[TestClass]
public sealed class MediaPlaybackProgressPolicyTests
{
    [TestMethod]
    public void RegisteredSmtcEnhancerCountsAsAdaptedWithoutIndependentCapability()
    {
        using var provider = new EnhancerProbe();
        var registry = new MediaSourceRegistry([provider]);
        Assert.IsTrue(registry.IsAdapted("enhancer.exe"), "普通 SMTC 增强器同样属于已适配来源。");
        Assert.IsFalse(registry.IsAdapted("other"));
    }

    [TestMethod]
    public void ExplainsOnlyTracksThatPlayedWithoutEverReportingProgress()
    {
        var start = Stopwatch.GetTimestamp();
        var later = start + Stopwatch.Frequency;

        var idle = MediaPlaybackProgressPolicy.Resolve(Snapshot(playing: false), PlaybackProgressMemory.Empty, start);
        Assert.IsFalse(idle.Unavailable, "播放器刚打开、什么都没播时内存通道也读不到，不能提示。");

        var firstFrame = MediaPlaybackProgressPolicy.Resolve(Snapshot(), PlaybackProgressMemory.Empty, start);
        Assert.IsFalse(firstFrame.Unavailable, "刚开始播或刚换歌的那一帧不下结论，时长可能下一轮才回来。");

        var playing = MediaPlaybackProgressPolicy.Resolve(Snapshot(), firstFrame.Memory, later);
        Assert.IsTrue(playing.Unavailable, "播过、有歌词、稳定一段时间仍没有进度，才提示。");
        Assert.AreEqual(MediaPlaybackProgressPolicy.TrackKey(Snapshot()), playing.Memory.PlayingTrack);

        Assert.IsTrue(MediaPlaybackProgressPolicy.Resolve(Snapshot(playing: false), playing.Memory, later).Unavailable,
            "同一首暂停后提示要留在原地，不能闪回歌手。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.Resolve(Snapshot(playing: false) with { Title = "别的歌" }, playing.Memory, later).Unavailable,
            "暂停状态下换歌要退出提示。");

        var withProgress = MediaPlaybackProgressPolicy.Resolve(Snapshot(duration: 261.3), playing.Memory, later);
        Assert.IsFalse(withProgress.Unavailable, "拿到时长说明这条路是通的。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.Resolve(Snapshot(), withProgress.Memory, later).Unavailable,
            "同一首的进度短暂变 0，不能当成播放器不提供进度。");

        var kugou = Snapshot() with { SourceId = "kugou", SourceName = "酷狗音乐" };
        var kugouFirst = MediaPlaybackProgressPolicy.Resolve(kugou, PlaybackProgressMemory.Empty, start);
        Assert.IsFalse(kugouFirst.Unavailable, "酷狗和网易云走同一套判断，第一帧同样不下结论。");
        Assert.IsTrue(MediaPlaybackProgressPolicy.Resolve(kugou, kugouFirst.Memory, later).Unavailable,
            "酷狗的内存通道读不到进度时同样提示。");

        Assert.IsFalse(MediaPlaybackProgressPolicy.Resolve(Snapshot(connected: false), playing.Memory, later).Unavailable,
            "没有媒体会话时不提示。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.Resolve(Snapshot(withLyrics: false), playing.Memory, later).Unavailable,
            "没有歌词时空白是正常结果，不能提示进度问题。");
    }

    [TestMethod]
    public void ExplainsOnlyWhenTheTrackIsMarkedAndLyricsAreEnabled()
    {
        Assert.IsTrue(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(unavailable: true), lyricsEnabled: true),
            "标记成立且歌词开着时才解释。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(), lyricsEnabled: true),
            "没有标记时不能提示，否则播放器空着也会弹说明。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(unavailable: true), lyricsEnabled: false),
            "关掉歌词后这行本来就显示歌手，不该被提示占用。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(unavailable: true, withLyrics: false), lyricsEnabled: true),
            "没有歌词时不解释。");
    }

    [TestMethod]
    public void LyricsStayHiddenWhenThePlayerReportsNoProgress()
    {
        var settings = new AppSettings { LyricsEnabled = true };
        var frozen = LyricsPresentationProjector.Project(Snapshot(unavailable: true), settings, DateTimeOffset.UtcNow);
        Assert.IsFalse(frozen.IsVisible, "没有进度时不能把第一句当成正在唱的那句，否则整首歌都停在这一句。");

        var normal = LyricsPresentationProjector.Project(Snapshot(duration: 261.3), settings, DateTimeOffset.UtcNow);
        Assert.IsTrue(normal.IsVisible, "有时长时第一句照常显示。");
    }

    private sealed class EnhancerProbe : IMediaSourceProvider
    {
        public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged { add { } remove { } }
        public bool CanHandle(string sourceId) => sourceId == "enhancer.exe";
        public void UpdateSessionSnapshot(MediaSnapshot snapshot) { }
        public void Start() { }
        public void Dispose() { }
    }

    private static MediaSnapshot Snapshot(
        bool connected = true,
        bool playing = true,
        double duration = 0,
        double position = 0,
        bool withLyrics = true,
        bool unavailable = false,
        double firstLineStart = 0) =>
        new(connected, playing, true, true, true, "Ring My Bell", "blue drops", "cloudmusic", "网易云音乐",
            null,
            withLyrics
                ? new LyricsResult("QQMusic", new LyricDocument(
                    [new LyricLine(firstLineStart, firstLineStart + 3, "第一句")], LyricsSyncType.LineSynced, "lrc"))
                : null,
            position, duration, false, false, MediaRepeatMode.Unavailable, 1, DateTimeOffset.MinValue)
        {
            PlaybackProgressUnavailable = unavailable
        };
}
