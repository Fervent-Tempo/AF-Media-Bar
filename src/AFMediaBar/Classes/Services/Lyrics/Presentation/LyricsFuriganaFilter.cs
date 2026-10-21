// 仅生成显示用的歌词副本；弱引用缓存随原行回收，避免在每个呈现帧重复过滤。
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>隐藏纯假名括号标注，保留原文档及所有时间片、译文和音译。</summary>
public static partial class LyricsFuriganaFilter
{
    private static readonly ConditionalWeakTable<LyricLine, LyricLine> _lines = new();

    public static LyricLine Apply(LyricLine line) => _lines.GetValue(line, FilterLine);

    private static LyricLine FilterLine(LyricLine line)
    {
        var text = Annotation().Replace(line.Text, string.Empty);
        // 整句只有括号内容时保留，避免隐藏括号人声后留下空白帧。
        if (text == line.Text || string.IsNullOrWhiteSpace(text)) return line;
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
        // 括号与读音可能跨多个音节，不能逐个音节独立匹配。
        var text = string.Concat(words.Select(word => word.Text));
        var matches = Annotation().Matches(text);
        if (matches.Count == 0) return words;
        var removed = new bool[text.Length];
        foreach (Match match in matches) removed.AsSpan(match.Index, match.Length).Fill(true);
        var result = new LyricWord[words.Count];
        var offset = 0;
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var visible = new StringBuilder(word.Text.Length);
            for (var character = 0; character < word.Text.Length; character++)
                if (!removed[offset + character]) visible.Append(word.Text[character]);
            result[index] = word with { Text = visible.ToString() };
            offset += word.Text.Length;
        }
        return result;
    }

    // 支持平假名、片假名、半角片假名及其长音、浊音标记；空白、标点和混合文字不匹配。
    [GeneratedRegex(@"\([ぁ-ゖァ-ヺｦ-ﾟーゝゞヽヾ\u3099\u309A]+\)|（[ぁ-ゖァ-ヺｦ-ﾟーゝゞヽヾ\u3099\u309A]+）")]
    private static partial Regex Annotation();
}
