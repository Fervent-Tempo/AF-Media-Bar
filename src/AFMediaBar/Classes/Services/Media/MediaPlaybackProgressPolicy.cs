// 判断“播放器没有给播放进度”这件事要不要向用户解释；纯策略，不读界面也不读设置存储。
using System.Diagnostics;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>跨帧要记住的记录。</summary>
/// <param name="ReportedTrack">报过进度的曲目；同一首里的瞬时 0 不算数。</param>
/// <param name="PlayingTrack">真正播过的曲目；暂停时靠它继续显示提示，播放器空着时它为空。</param>
/// <param name="CandidateTrack">正在观察的“没有进度”的曲目。</param>
/// <param name="CandidateSince">这段观察从什么时候开始，用 Stopwatch 时间戳。</param>
public readonly record struct PlaybackProgressMemory(
    string ReportedTrack, string PlayingTrack, string CandidateTrack, long CandidateSince)
{
    /// <summary>空记录：什么都没播过。</summary>
    public static PlaybackProgressMemory Empty => new(string.Empty, string.Empty, string.Empty, 0);
}

/// <summary>
/// 播放进度缺失时的解释策略。
/// </summary>
/// <remarks>
/// 网易云和酷狗本来就不通过 SMTC 报进度，进度只来自进程内存通道；其它播放器只有 SMTC 一条路。判断只有一句话：
/// 这首曲目播过，但一次进度都没拿到。适配与不适配只影响提示文案。
///
/// 难点在时机：换歌时歌词常常直接命中缓存，时长要等内存通道下一轮读取才回来，中间有一两百毫秒的“有歌词没进度”。
/// 所以还要求这段状态稳定持续一段时间才下结论；播放器空着时因为没播过，会被直接排除。
///
/// 隐藏与提示是两个决策，不再共用一个标记：歌词隐藏只看「当前有没有可用时间轴」，立即生效
/// （<see cref="ShouldHideLyrics"/>）；提示才要求稳定期（<see cref="ExplainsMissingProgress"/>）。
/// </remarks>
public static class MediaPlaybackProgressPolicy
{
    /// <summary>没有进度的状态要稳定这么久才下结论。</summary>
    private static readonly TimeSpan MinimumHold = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// 是否没有可用时间轴而应立即隐藏歌词：不等稳定期。
    /// 换歌后时长未到的空窗与空闲未播放，都不该按位置 0 把第一句当成正在唱的那句；
    /// 「不提供播放进度」的说明由第二行在稳定期后另行显示，见 <see cref="ExplainsMissingProgress"/>。
    /// </summary>
    /// <param name="snapshot">当前快照。</param>
    /// <param name="lyricsEnabled">用户是否启用了歌词；没启用时歌词区本来就不显示。</param>
    public static bool ShouldHideLyrics(MediaSnapshot snapshot, bool lyricsEnabled) =>
        lyricsEnabled &&
        snapshot.Duration <= 0 &&
        snapshot.Position <= 0 &&
        snapshot.Lyrics?.Document.Lines is { Count: > 0 };

    /// <summary>
    /// 是否应当告诉用户当前播放器不提供播放进度；稳定期后才有意义，之前第二行保持显示歌手。
    /// </summary>
    /// <param name="snapshot">当前快照。</param>
    /// <param name="lyricsEnabled">用户是否启用了歌词；没启用时歌词区本来就不显示，不需要解释。</param>
    public static bool ExplainsMissingProgress(MediaSnapshot snapshot, bool lyricsEnabled) =>
        lyricsEnabled &&
        snapshot.PlaybackProgressUnavailable &&
        snapshot.Lyrics?.Document.Lines is { Count: > 0 };

    /// <summary>曲目标识，用来判断两帧是不是同一首。</summary>
    public static string TrackKey(MediaSnapshot snapshot) =>
        $"{snapshot.SourceId}\u001f{snapshot.Title}\u001f{snapshot.Artist}";

    /// <summary>
    /// 判断这一帧是不是“播过但没有进度”，同时给出下一帧要记住的记录。
    /// </summary>
    /// <param name="snapshot">当前快照。</param>
    /// <param name="memory">上一帧留下的记录。</param>
    /// <param name="nowTicks">当前时刻，Stopwatch 时间戳。</param>
    public static (bool Unavailable, PlaybackProgressMemory Memory) Resolve(
        MediaSnapshot snapshot, PlaybackProgressMemory memory, long nowTicks)
    {
        if (!snapshot.IsConnected)
        {
            return (false, PlaybackProgressMemory.Empty);
        }

        var trackKey = TrackKey(snapshot);
        var playingTrack = snapshot.IsPlaying ? trackKey : memory.PlayingTrack;
        if (snapshot.Duration > 0 || snapshot.Position > 0)
        {
            return (false, new PlaybackProgressMemory(trackKey, playingTrack, string.Empty, 0));
        }

        // 播过这一首、又有歌词，才开始观察它到底有没有进度；暂停后 PlayingTrack 仍是它，观察继续。
        var observing = playingTrack == trackKey && snapshot.Lyrics?.Document.Lines is { Count: > 0 };
        if (!observing)
        {
            return (false, new PlaybackProgressMemory(memory.ReportedTrack, playingTrack, string.Empty, 0));
        }

        var since = memory.CandidateTrack == trackKey ? memory.CandidateSince : nowTicks;
        var settled = Stopwatch.GetElapsedTime(since, nowTicks) >= MinimumHold;
        var unavailable = settled && memory.ReportedTrack != trackKey;
        return (unavailable, new PlaybackProgressMemory(memory.ReportedTrack, playingTrack, trackKey, since));
    }
}
