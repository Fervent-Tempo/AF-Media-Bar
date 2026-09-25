using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 按播放位置在歌词行集合中定位当前行。
/// Locates the active lyric line for a playback position inside a line collection.
///
/// 语义与历史实现一致：返回最后一个起始时间不晚于位置的行；位置早于第一行时返回 -1（由调用方回落到标题与歌手）。
/// The semantics match the previous implementation: the last line whose start is not later than the position, or -1
/// before the first line, which makes the caller fall back to title and artist.
/// </summary>
public static class LyricLineIndexPolicy
{
    /// <summary>
    /// 返回播放位置对应的行下标；位置早于第一行时返回 -1。
    /// Returns the index of the line active at the position, or -1 before the first line.
    /// </summary>
    /// <param name="lines">按起始时间升序排列的行 / Lines ordered by start time.</param>
    /// <param name="positionSeconds">播放位置（秒）/ Playback position in seconds.</param>
    public static int FindIndex(IReadOnlyList<LyricLine> lines, double positionSeconds)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Start <= positionSeconds)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }
}
