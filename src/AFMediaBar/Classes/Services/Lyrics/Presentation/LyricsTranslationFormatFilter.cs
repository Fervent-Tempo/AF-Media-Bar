// 清理整行翻译的格式噪声：行首斜杠标记与整行成对的【】/〖〗符号；只影响显示，不修改歌词文档。
using System.Runtime.CompilerServices;
using System.Text;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 清理一行歌词里由来源附加的格式噪声：行首用 <c>/</c> 标记译文、整行被 <c>【】</c> 或 <c>〖〗</c> 包住，
/// 去掉这些标记后把剩下的文字照常显示。
/// Cleans the formatting noise a lyric source adds to a line: a leading <c>/</c> marking a translation row, or the
/// whole line being wrapped in <c>【】</c> or <c>〖〗</c>. Whatever remains between the markers is displayed as usual.
///
/// 两种标记可以叠加（<c>/ 【…】</c>），因此按"能去就去"的方式循环处理，直到没有可去的标记。被去掉的永远只有
/// 行首和行尾，中间一个字符都不动，因此逐字歌词的时间轴不需要重算；但它按可见字符分配的进度权重必须跟着改，
/// 否则擦亮会整体偏一截——所以这里连 <see cref="LyricWord"/> 一起清理，而不是只改整行文本。
/// The two kinds can stack (<c>/ 【…】</c>), so they are removed in a loop until nothing else matches. Only the head
/// and the tail are ever removed, never anything in the middle, so a syllable timeline needs no recomputation; the
/// progress weights, however, are per visible character and have to follow, or the wipe would be off by the removed
/// characters. That is why the words are cleaned too instead of only the line text.
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

    // 只缓存显示用的副本，随原行一起回收，避免每帧重复清理。
    private static readonly ConditionalWeakTable<LyricLine, LyricLine> _lines = new();

    /// <summary>返回清理后的行（文本与逐字音节一起清理），没有可去的标记时返回原行本身。</summary>
    public static LyricLine Apply(LyricLine line) => _lines.GetValue(line, FilterLine);

    /// <summary>
    /// 去掉行首斜杠标记与首尾成对的方括号符号，返回剩下的文字；没有可去的标记时返回同一个实例。
    /// Removes the leading slash marker and the enclosing bracket pair, returning what is left; returns the very same
    /// instance when there is nothing to remove.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (!TryFindRetainedRange(text, out var start, out var end))
            return text;

        return start >= end ? HiddenText : text[start..end];
    }

    private static LyricLine FilterLine(LyricLine line)
    {
        var text = Clean(line.Text);
        var words = CleanWords(line.Words);
        if (ReferenceEquals(text, line.Text) && ReferenceEquals(words, line.Words))
            return line;

        return new LyricLine(line.Start, line.End, text)
        {
            Words = words,
            Translation = line.Translation,
            Romanization = line.Romanization,
            IsBackground = line.IsBackground
        };
    }

    private static IReadOnlyList<LyricWord> CleanWords(IReadOnlyList<LyricWord> words)
    {
        if (words.Count == 0)
            return words;

        // 标记可能跨多个音节，不能逐个音节独立处理。
        var text = string.Concat(words.Select(word => word.Text));
        if (!TryFindRetainedRange(text, out var start, out var end) || start >= end)
            return words;

        var result = new LyricWord[words.Count];
        var offset = 0;
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var visible = new StringBuilder(word.Text.Length);
            for (var character = 0; character < word.Text.Length; character++)
                if (offset + character >= start && offset + character < end)
                    visible.Append(word.Text[character]);
            result[index] = word with { Text = visible.ToString() };
            offset += word.Text.Length;
        }

        return result;
    }

    /// <summary>
    /// 算出保留区间 [start, end)。只会收缩首尾：空白、行首斜杠标记、整行括号依次去掉，中间不受影响。
    /// Resolves the retained range [start, end). Only the ends shrink: whitespace, the leading slash marker, and the
    /// enclosing bracket pair are removed in turn while the middle stays untouched.
    /// </summary>
    private static bool TryFindRetainedRange(string text, out int start, out int end)
    {
        start = 0;
        end = text.Length;
        var changed = false;
        while (true)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
                changed = true;
            }

            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
                changed = true;
            }

            if (end - start >= 2 && text[start] == '/' && char.IsWhiteSpace(text[start + 1]))
            {
                start++;
                changed = true;
                continue;
            }

            if (end - start >= 2 &&
                ((text[start] == '【' && text[end - 1] == '】') || (text[start] == '〖' && text[end - 1] == '〗')))
            {
                start++;
                end--;
                changed = true;
                continue;
            }

            return changed;
        }
    }
}
