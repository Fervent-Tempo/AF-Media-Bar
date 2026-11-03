// 验证“播放器不提供播放进度”的解释条件；纯策略，不依赖界面、SMTC 或设置存储。
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
    public void ExplainsOnlyWhenLyricsExistAndProgressIsEntirelyMissing()
    {
        Assert.IsTrue(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(), lyricsEnabled: true),
            "歌词在手、位置与时长都是 0 时必须给出解释。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(duration: 261.3), lyricsEnabled: true),
            "有时长说明播放器报了时间轴。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(position: 12), lyricsEnabled: true),
            "位置在推进时不需要解释。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(withLyrics: false), lyricsEnabled: true),
            "没有歌词时空白是正常结果，不能提示进度问题。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(), lyricsEnabled: false),
            "关掉歌词后这行本来就显示歌手，不该被提示占用。");
        Assert.IsTrue(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(playing: false), lyricsEnabled: true),
            "暂停不改变这个状态，提示要留在原地，不能一按暂停就闪回歌手。");
        Assert.IsFalse(MediaPlaybackProgressPolicy.ExplainsMissingProgress(Snapshot(connected: false), lyricsEnabled: true),
            "没有媒体会话时不提示。");
    }

    [TestMethod]
    public void LyricsStayHiddenWhenThePlayerReportsNoProgress()
    {
        var settings = new AppSettings { LyricsEnabled = true };
        var frozen = LyricsPresentationProjector.Project(Snapshot(), settings, DateTimeOffset.UtcNow);
        Assert.IsFalse(frozen.IsVisible, "没有进度时不能把第一句当成正在唱的那句，否则整首歌都停在这一句。");

        var normal = LyricsPresentationProjector.Project(Snapshot(duration: 261.3), settings, DateTimeOffset.UtcNow);
        Assert.IsTrue(normal.IsVisible, "有时长时第一句照常显示。");
    }

    private static MediaSnapshot Snapshot(
        bool connected = true,
        bool playing = true,
        double duration = 0,
        double position = 0,
        bool withLyrics = true,
        double firstLineStart = 0) =>
        new(connected, playing, true, true, true, "Ring My Bell", "blue drops", "cloudmusic", "网易云音乐",
            null,
            withLyrics
                ? new LyricsResult("QQMusic", new LyricDocument(
                    [new LyricLine(firstLineStart, firstLineStart + 3, "第一句")], LyricsSyncType.LineSynced, "lrc"))
                : null,
            position, duration, false, false, MediaRepeatMode.Unavailable, 1, DateTimeOffset.MinValue);
}
