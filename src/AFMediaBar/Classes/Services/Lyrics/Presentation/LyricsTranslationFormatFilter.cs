// 清理整行翻译的格式噪声：行首斜杠标记与整行成对的【】/〖〗符号；只影响显示，不修改歌词文档。
namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 清理一行歌词里由来源附加的格式噪声：行首用 <c>/</c> 标记译文、整行被 <c>【】</c> 或 <c>〖〗</c> 包住，
/// 去掉这些标记后把剩下的文字照常显示。
/// Cleans the formatting noise a lyric source adds to a line: a leading <c>/</c> marking a translation row, or the
/// whole line being wrapped in <c>【】</c> or <c>〖〗</c>. Whatever remains between the markers is displayed as usual.
///
/// 两种标记可以叠加（<c>/ 【…】</c>），因此按"能去就去"的方式循环处理，直到没有可去的标记。
/// The two kinds can stack (<c>/ 【…】</c>), so they are removed in a loop until nothing else matches.
///
/// 行首斜杠只在后面紧跟空白时才去掉：这样 <c>/ 曾经沉睡着</c> 这种译文标记会被清理，而 <c>/cause I…</c>
/// 这种以斜杠开头的正文不受影响。
/// A leading slash is removed only when whitespace follows it, so a translation marker such as
/// <c>/ 曾经沉睡着</c> is cleaned while text that genuinely starts with a slash, such as <c>/cause I…</c>, is left alone.
///
/// 清理后什么都不剩时返回一个空格：Web 呈现层把空串当成"还没取到歌词"，会显示搜索中的兜底文案。
/// When nothing is left this returns a single space: the web presentation layer treats an empty string as "lyrics
/// not loaded yet" and shows its searching placeholder.
/// </summary>
public static class LyricsTranslationFormatFilter
{
    /// <summary>清理后什么都不剩时的显示文本：单个空格。/ The display text when nothing is left: one space.</summary>
    public const string HiddenText = " ";

    /// <summary>
    /// 去掉行首斜杠标记与首尾成对的方括号符号，返回剩下的文字；没有可去的标记时返回同一个实例。
    /// Removes the leading slash marker and the enclosing bracket pair, returning what is left; returns the very same
    /// instance when there is nothing to remove.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var trimmed = text.Trim();
        var changed = false;
        while (true)
        {
            if (trimmed.Length > 1 && trimmed[0] == '/' && char.IsWhiteSpace(trimmed[1]))
            {
                trimmed = trimmed[1..].Trim();
                changed = true;
                continue;
            }

            var inner = InnerText(trimmed);
            if (inner is null)
                break;

            trimmed = inner.Trim();
            changed = true;
        }

        if (!changed)
            return text;

        return trimmed.Length == 0 ? HiddenText : trimmed;
    }

    private static string? InnerText(string text)
    {
        if (text.Length < 2)
            return null;
        if (text[0] == '【' && text[^1] == '】')
            return text[1..^1];
        if (text[0] == '〖' && text[^1] == '〗')
            return text[1..^1];
        return null;
    }
}
