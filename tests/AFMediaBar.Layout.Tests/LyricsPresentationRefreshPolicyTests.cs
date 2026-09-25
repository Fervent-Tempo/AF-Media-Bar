using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词呈现刷新时机的测试：隐藏帧在"等待第一句"期间必须让 50ms 定时器继续跑，重复的隐藏帧不必下发。
/// Tests for the lyric presentation refresh timing: a hidden frame must keep the 50 ms timer running while waiting for
/// the first line, and repeated hidden frames do not have to be sent.
/// </summary>
[TestClass]
public sealed class LyricsPresentationRefreshPolicyTests
{
    private static LyricsPresentationFrame Visible(int index = 0, double progress = 0.5) => new(
        true,
        "TRACK",
        index,
        "当前句",
        "下一句",
        string.Empty,
        string.Empty,
        false,
        progress,
        null,
        true);

    [TestMethod]
    public void TimerKeepsRunningWhileAPlayingTrackWaitsForItsFirstLine()
    {
        // 播放器时间轴停在新曲目 0 秒时必须继续按墙钟重投影，否则媒体栏永远停在歌名/歌手。
        // With the player's timeline stalled at zero for a new track the projection must keep being recomputed from the
        // wall clock, otherwise the bar would stay on title/artist forever.
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldRunTimer(
            isLoaded: true,
            isAdvancePruned: false,
            frameVisible: false,
            mediaIsPlaying: true,
            lyricsEnabled: true,
            isConnected: true,
            loadedLineCount: 38));
    }

    [TestMethod]
    public void TimerStaysStoppedWhenAHiddenFrameCannotBecomeVisibleOnItsOwn()
    {
        // 没歌词、歌词关闭、未连接或暂停时，隐藏是稳定状态：空转没有意义。
        // With no lyrics, lyrics disabled, disconnected, or paused the hidden state is stable and spinning would be pointless.
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, false, true, true, true, 0));
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, false, true, false, true, 38));
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, false, true, true, false, 38));
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, false, false, true, true, 38));
    }

    [TestMethod]
    public void TimerRespectsTheHostGates()
    {
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(false, false, true, true, true, true, 38));
        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, true, true, true, true, true, 38));
    }

    [TestMethod]
    public void VisibleFramesAlwaysRunTheTimer()
    {
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, true, true, true, true, 38));
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldRunTimer(true, false, true, true, false, false, 0));
    }

    [TestMethod]
    public void HiddenFramesAreSentOnlyWhenTheyChange()
    {
        var hidden = LyricsPresentationFrame.Hidden;

        Assert.IsFalse(LyricsPresentationRefreshPolicy.ShouldPresent(false, hidden, hidden));
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldPresent(false, Visible(), hidden));
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldPresent(false, hidden, Visible()));
    }

    [TestMethod]
    public void VisibleFramesAreAlwaysSent()
    {
        var frame = Visible();

        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldPresent(true, frame, frame));
        Assert.IsTrue(LyricsPresentationRefreshPolicy.ShouldPresent(true, Visible(0, 0.1), Visible(0, 0.9)));
    }
}
