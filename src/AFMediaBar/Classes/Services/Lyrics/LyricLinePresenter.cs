using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 缓存 LRC 解析结果并按媒体位置选择当前歌词行。
/// Caches parsed LRC lines and selects the active lyric line by media position.
/// </summary>
public sealed class LyricLinePresenter
{
    private string? _lrc;
    private string? _translation;
    private IReadOnlyList<LrcLine> _lines = [];
    private IReadOnlyList<LrcLine> _translationLines = [];
    private int _lastIndex = -2;

    /// <summary>
    /// 根据歌词文本和播放位置计算呈现状态。
    /// Computes presentation state from lyric text and playback position.
    /// </summary>
    /// <param name="lyrics">歌词结果；为空时清除状态。/ Lyric result; null clears the state.</param>
    /// <param name="position">当前播放位置（秒）。/ Current playback position in seconds.</param>
    /// <returns>当前歌词、下一句、同句翻译及是否发生变化。/ Current lyric, next line, matching translation, and change state.</returns>
    public LyricLineUpdate Update(LyricsResult? lyrics, double position)
    {
        var lrc = lyrics?.Lrc;
        if (string.IsNullOrWhiteSpace(lrc))
        {
            var cleared = _lrc is not null || _translation is not null || _lastIndex != -2;
            _lrc = null;
            _translation = null;
            _lines = [];
            _translationLines = [];
            _lastIndex = -2;
            return new LyricLineUpdate(string.Empty, string.Empty, string.Empty, cleared);
        }

        var translation = lyrics?.Translation;
        var sourceChanged = !string.Equals(_lrc, lrc, StringComparison.Ordinal) ||
                            !string.Equals(_translation, translation, StringComparison.Ordinal);
        if (sourceChanged)
        {
            _lrc = lrc;
            _translation = translation;
            _lines = LrcParser.Parse(lrc);
            _translationLines = LrcParser.Parse(translation);
            _lastIndex = -2;
        }

        var index = LrcParser.FindIndex(_lines, TimeSpan.FromSeconds(position));
        var currentText = index >= 0 ? _lines[index].Text : string.Empty;
        var nextText = index >= 0 && index + 1 < _lines.Count ? _lines[index + 1].Text : string.Empty;
        var translationText = index >= 0 ? FindMatchingTranslation(_lines[index].Time) : string.Empty;
        var changed = sourceChanged || index != _lastIndex;

        _lastIndex = index;
        return new LyricLineUpdate(currentText, nextText, translationText, changed);
    }

    private string FindMatchingTranslation(TimeSpan sourceTime)
    {
        // 网易云翻译时间戳通常完全一致；允许很小的舍入差，但不要沿用上一句翻译。
        // NetEase timestamps usually match exactly; tolerate small rounding without carrying a prior translation forward.
        var bestDifference = TimeSpan.FromMilliseconds(500);
        var bestText = string.Empty;
        foreach (var line in _translationLines)
        {
            var difference = (line.Time - sourceTime).Duration();
            if (difference <= bestDifference)
            {
                bestDifference = difference;
                bestText = line.Text;
            }

            if (line.Time - sourceTime > bestDifference)
                break;
        }

        return bestText;
    }
}

/// <summary>歌词行呈现更新结果，包括当前句、下一句和同句翻译。/ Lyric-line presentation including current, next, and translated text.</summary>
public readonly record struct LyricLineUpdate(
    string Text,
    string NextText,
    string TranslationText,
    bool Changed);
