// 仅追踪日志已报告的媒体状态，不参与快照仲裁或播放进度计算。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

internal sealed class MediaSnapshotLogState
{
    private MediaSnapshot? _previous;
    private double _duration;

    internal string? DescribeChanges(MediaSnapshot current)
    {
        var previous = _previous;
        if (previous is null)
        {
            _previous = current;
            _duration = current.Duration;
            return "initial";
        }
        List<string> changes = [];
        if (previous.SourceId != current.SourceId || previous.SourceName != current.SourceName) changes.Add("source");
        var trackChanged = previous.Title != current.Title || previous.Artist != current.Artist || previous.SourceId != current.SourceId;
        if (trackChanged) changes.Add("track");
        if (previous.IsConnected != current.IsConnected) changes.Add($"connected={current.IsConnected}");
        if (previous.IsPlaying != current.IsPlaying) changes.Add($"playing={current.IsPlaying}");
        if (previous.CanPlayPause != current.CanPlayPause || previous.CanSeek != current.CanSeek ||
            previous.CanSkipPrevious != current.CanSkipPrevious || previous.CanSkipNext != current.CanSkipNext ||
            previous.CanChangeRepeat != current.CanChangeRepeat)
            changes.Add($"controls(playPause={current.CanPlayPause},seek={current.CanSeek},prev={current.CanSkipPrevious},next={current.CanSkipNext},repeat={current.CanChangeRepeat})");
        if (previous.RepeatMode != current.RepeatMode) changes.Add($"repeat={current.RepeatMode}");
        if (Math.Round(previous.PlaybackRate, 2) != Math.Round(current.PlaybackRate, 2)) changes.Add($"rate={current.PlaybackRate:0.##}");
        if (previous.Lyrics?.Source != current.Lyrics?.Source || previous.Lyrics?.Status != current.Lyrics?.Status)
            changes.Add($"lyrics={current.Lyrics?.Source ?? "none"}/{current.Lyrics?.Status.ToString() ?? "none"}");
        if ((_duration > 0) != (current.Duration > 0) || Math.Abs(_duration - current.Duration) >= 1)
        {
            changes.Add($"duration={current.Duration:0.0}");
        }
        // 其它状态行也会显示当前时长，阈值应以最后实际输出的值为准。
        if (changes.Count > 0) _duration = current.Duration;
        _previous = current;
        return changes.Count == 0 ? null : string.Join(",", changes);
    }
}
