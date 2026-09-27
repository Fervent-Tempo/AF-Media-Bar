namespace AFMediaBar.Classes.Services;

/// <summary>
/// 播放时间的短文本：完整层的位置/时长与悬停层拖动气泡共用同一格式。
/// Short playback-time text: the full panel's position/duration and the hover layer's drag bubble share one format.
/// </summary>
public static class PlaybackTimeText
{
    /// <summary>
    /// 把秒数格式化为 m:ss（达到或超过 1 小时为 h:mm:ss）；负值、NaN 与无穷都按 0 处理。
    /// Formats seconds as m:ss (h:mm:ss at one hour or more); negative, NaN, and infinite values count as zero.
    /// </summary>
    /// <param name="seconds">秒数 / Seconds.</param>
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
