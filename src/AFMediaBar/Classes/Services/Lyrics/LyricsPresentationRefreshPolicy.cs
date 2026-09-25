namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词呈现的刷新时机：决定 50ms 帧定时器是否需要运行、以及一帧是否需要下发给 Web 视图。
/// When the lyric presentation refreshes: whether the 50 ms frame timer must run, and whether a frame must be sent to
/// the web view.
///
/// 播放器的时间轴可以停在新曲目的 0 秒不随播放推进（澜音实测如此），而歌词行号按"快照 + 当前墙钟"重算：
/// 第一句之前的投影结果是"隐藏"，旧逻辑据此停掉帧定时器，此后没有任何东西再按时间重投影，媒体栏会一直停在
/// 歌名/歌手，直到播放器碰巧发布一次新时间轴（例如暂停再播放）。因此只要"正在播放且已拿到歌词"，即使当前帧是
/// 隐藏也要让定时器继续跑，让它在墙钟走到第一句时把画面切回歌词。
/// A player's timeline can stall at zero for a newly started track and not advance with playback (measured with Ceru
/// Music). Lyric line selection is recomputed from "snapshot + current wall clock", and before the first line the
/// projection is Hidden; the old logic stopped the frame timer on that verdict, after which nothing ever re-projected
/// with time and the bar stayed on title/artist until the player happened to publish a fresh timeline (for example a
/// pause and resume). So as long as media is playing and lyrics are already loaded, the timer keeps running even while
/// the frame is Hidden, letting it flip the bar back to lyrics when the wall clock reaches the first line.
/// </summary>
public static class LyricsPresentationRefreshPolicy
{
    /// <summary>
    /// 帧定时器是否需要运行。隐藏帧只有在"正在播放且已拿到可用的歌词"时才需要继续跑：行号会随墙钟越过第一句；
    /// 歌词关闭、未连接或还没拿到歌词时隐藏是稳定状态，不必空转。
    /// Whether the frame timer must run. A hidden frame only needs it while media is playing with loaded lyrics,
    /// because line selection advances with the wall clock; with lyrics disabled, disconnected, or no lyrics yet the
    /// hidden state is stable and the timer would only spin.
    /// </summary>
    /// <param name="isLoaded">控件是否已加载 / Whether the control is loaded.</param>
    /// <param name="isAdvancePruned">空闲剪枝是否暂停推进 / Whether idle pruning suspended advancement.</param>
    /// <param name="frameVisible">当前帧是否可见 / Whether the current frame is visible.</param>
    /// <param name="mediaIsPlaying">媒体是否在播放（取自快照，不能用隐藏帧的 IsPlaying，它恒为 false） / Whether media is
    /// playing (taken from the snapshot; do not use the hidden frame's IsPlaying, which is always false).</param>
    /// <param name="lyricsEnabled">歌词功能是否开启 / Whether the lyrics feature is enabled.</param>
    /// <param name="isConnected">媒体会话是否已连接 / Whether a media session is connected.</param>
    /// <param name="loadedLineCount">已取得的歌词行数 / Number of loaded lyric lines.</param>
    /// <returns>定时器是否需要运行 / Whether the timer has to run.</returns>
    public static bool ShouldRunTimer(
        bool isLoaded,
        bool isAdvancePruned,
        bool frameVisible,
        bool mediaIsPlaying,
        bool lyricsEnabled,
        bool isConnected,
        int loadedLineCount)
    {
        if (!isLoaded || isAdvancePruned || !mediaIsPlaying)
            return false;

        return frameVisible || (lyricsEnabled && isConnected && loadedLineCount > 0);
    }

    /// <summary>
    /// 该帧是否需要下发给 Web 视图。隐藏帧在"等待第一句"期间每帧都相等，重复下发只会白跑脚本；
    /// 可见帧始终下发（卡拉OK 进度与小节扫描依赖它），页面刷新后也由第一帧可见内容带回全部状态。
    /// Whether the frame must be sent to the web view. While waiting for the first line the hidden frames are all
    /// equal and re-sending them only runs the script for nothing; a visible frame is always sent (word-scan and
    /// karaoke progress depend on it), and after a page reload the first visible frame carries the full state back.
    /// </summary>
    /// <param name="frameVisible">新帧是否可见 / Whether the new frame is visible.</param>
    /// <param name="previous">上一帧 / The previous frame.</param>
    /// <param name="next">新帧 / The new frame.</param>
    /// <returns>是否需要下发 / Whether the frame has to be sent.</returns>
    public static bool ShouldPresent(bool frameVisible, LyricsPresentationFrame previous, LyricsPresentationFrame next) =>
        frameVisible || !next.Equals(previous);
}
