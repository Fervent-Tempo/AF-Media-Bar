using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 缓存 LRC 解析结果并按媒体位置选择当前歌词行。
/// Caches parsed LRC lines and selects the active lyric line by media position.
/// </summary>
public sealed class LyricLinePresenter
{
    private string? _lrc;
    private IReadOnlyList<LrcLine> _lines = [];
    private int _lastIndex = -2;

    /// <summary>
    /// 根据歌词文本和播放位置计算呈现状态。
    /// Computes presentation state from lyric text and playback position.
    /// </summary>
    /// <param name="lyrics">歌词结果；为空时清除状态。/ Lyric result; null clears the state.</param>
    /// <param name="position">当前播放位置（秒）。/ Current playback position in seconds.</param>
    /// <returns>当前歌词文本及是否发生变化。/ Active lyric text and whether the state changed.</returns>
    public LyricLineUpdate Update(LyricsResult? lyrics, double position)
    {
        var lrc = lyrics?.Lrc;
        if (string.IsNullOrWhiteSpace(lrc))
        {
            var changed = _lrc is not null || _lastIndex != -2;
            _lrc = null;
            _lines = [];
            _lastIndex = -2;
            return new LyricLineUpdate(string.Empty, changed);
        }

        if (!string.Equals(_lrc, lrc, StringComparison.Ordinal))
        {
            _lrc = lrc;
            _lines = LrcParser.Parse(lrc);
            _lastIndex = -2;
        }

        var index = LrcParser.FindIndex(_lines, TimeSpan.FromSeconds(position));
        if (index == _lastIndex)
            return new LyricLineUpdate(index >= 0 ? _lines[index].Text : string.Empty, false);

        _lastIndex = index;
        return new LyricLineUpdate(index >= 0 ? _lines[index].Text : string.Empty, true);
    }
}

/// <summary>歌词行呈现更新结果。/ Lyric-line presentation update.</summary>
public readonly record struct LyricLineUpdate(string Text, bool Changed);
