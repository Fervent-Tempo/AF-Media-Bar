// 清理整行翻译的格式噪声：行首斜杠标记与整行成对的【】/〖〗符号；只影响显示，不修改歌词文档。
using System.Runtime.CompilerServices;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 去掉行首斜杠标记和整行成对的【】/〖〗，保留正文；支持叠加标记，斜杠后必须有空白。
/// 显示副本的音节文本同步清理并按原行弱引用缓存，不修改来源文档或时间戳。
/// </summary>
public static class LyricsTranslationFormatFilter
{
    private static readonly ConditionalWeakTable<LyricLine, LyricLine> _lines = new();

    /// <summary>显式空行；Web 层保留该空格，与缺失文本区分。</summary>
    public const string HiddenText = " ";

    public static LyricLine Apply(LyricLine line) => _lines.GetValue(line, FilterLine);

    /// <summary>返回清理后的显示文本，没有标记时保留原实例。</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var (start, length) = FindVisibleRange(text);
        if (start == 0 && length == text.Length)
            return text;
        return length == 0 ? HiddenText : text.Substring(start, length);
    }

    private static LyricLine FilterLine(LyricLine line)
    {
        var text = Clean(line.Text);
        if (text == line.Text) return line;
        return new LyricLine(line.Start, line.End, text)
        {
            Words = FilterWords(line.Words),
            Translation = line.Translation,
            Romanization = line.Romanization,
            IsBackground = line.IsBackground
        };
    }

    private static IReadOnlyList<LyricWord> FilterWords(IReadOnlyList<LyricWord> words)
    {
        if (words.Count == 0) return words;
        // 标记可能跨音节，先在完整文本上定位，再按原时间片保留可见部分。
        var text = string.Concat(words.Select(word => word.Text));
        var (start, length) = FindVisibleRange(text);
        if (start == 0 && length == text.Length) return words;
        var result = new LyricWord[words.Count];
        var offset = 0;
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var visibleStart = Math.Clamp(start - offset, 0, word.Text.Length);
            var visibleEnd = Math.Clamp(start + length - offset, 0, word.Text.Length);
            result[index] = word with { Text = word.Text[visibleStart..visibleEnd] };
            offset += word.Text.Length;
        }
        return result;
    }

    private static (int Start, int Length) FindVisibleRange(string text)
    {
        var start = 0;
        var end = text.Length;
        var changed = false;
        while (true)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            // 在去掉尾部空白前判定，保证只有斜杠和空白的行也能被清理。
            var leadingSlash = end - start > 1 && text[start] == '/' && char.IsWhiteSpace(text[start + 1]);
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (leadingSlash)
            {
                start++;
                changed = true;
                continue;
            }

            var hasBrackets = end - start >= 2 &&
                              ((text[start] == '【' && text[end - 1] == '】') ||
                               (text[start] == '〖' && text[end - 1] == '〗'));
            if (!hasBrackets) break;

            start++;
            end--;
            changed = true;
        }
        return changed ? (start, end - start) : (0, text.Length);
    }
}
