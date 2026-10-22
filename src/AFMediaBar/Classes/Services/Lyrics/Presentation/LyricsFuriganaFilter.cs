// 仅生成显示用的歌词副本；弱引用缓存随原行回收，避免在每个呈现帧重复过滤。
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 隐藏纯假名括号标注，保留原文档及所有时间片、译文和音译。
/// Hides pure-kana bracket annotations while preserving the source document, every time slice, and the translation
/// and romanization.
///
/// 译文行同样带括号内容时，主行的假名括号是唱出来的人声而不是标注音（如「！（キュン）」对「！（心跳~）」），
/// 此时整行保留，避免删掉人声后与译文对不上。
/// When the translation carries bracketed content of its own, the main line's kana bracket is a vocal that is
/// actually sung rather than an annotation, so the whole line is kept instead of being stripped.
/// </summary>
public static partial class LyricsFuriganaFilter
{
    // 支持平假名、片假名、半角片假名及其长音、浊音标记；空白、标点和混合文字不匹配。
    private const string Kana = "[ぁ-ゖァ-ヺｦ-ﾟーゝゞヽヾ\u3099\u309A]+";

    private static readonly ConditionalWeakTable<LyricLine, LyricLine> _lines = new();

    public static LyricLine Apply(LyricLine line) => _lines.GetValue(line, FilterLine);

    private static LyricLine FilterLine(LyricLine line)
    {
        var text = Annotation().Replace(line.Text, string.Empty);
        // 整句只有括号内容时保留，避免隐藏括号人声后留下空白帧。
        if (text == line.Text || string.IsNullOrWhiteSpace(text)) return line;
        if (TranslationCarriesBracketLyric(line)) return line;
        return new LyricLine(line.Start, line.End, text)
        {
            Words = FilterWords(line.Words),
            Translation = line.Translation,
            Romanization = line.Romanization,
            IsBackground = line.IsBackground
        };
    }

    /// <summary>
    /// 译文行是否带括号人声：括号内不是纯假名（已翻译的人声），或括号内容在原行同样出现（未翻译、原样保留的人声）。
    /// Whether the translation carries a bracketed vocal: the bracket holds non-kana text (a translated vocal), or the
    /// same bracket content also appears in the original line (a vocal left untranslated).
    /// </summary>
    private static bool TranslationCarriesBracketLyric(LyricLine line)
    {
        var translation = line.Translation;
        if (string.IsNullOrEmpty(translation)) return false;
        foreach (Match match in TranslationBracket().Matches(translation))
        {
            var inner = match.Groups["inner"].Value;
            if (inner.Length == 0) continue;
            // 纯假名的括号更像注音而非人声，除非原行也有同样内容（未翻译的人声原样保留）。
            if (!KanaOnly().IsMatch(inner)) return true;
            if (line.Text.Contains(inner, StringComparison.Ordinal)) return true;
        }

        return false;
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

    [GeneratedRegex(@"\(" + Kana + @"\)|（" + Kana + @"）")]
    private static partial Regex Annotation();

    // 译文里成对出现的括号（半角或全角），括号内至少一个字符。
    [GeneratedRegex(@"[（(](?<inner>[^（）()]+)[)）]")]
    private static partial Regex TranslationBracket();

    [GeneratedRegex(@"^" + Kana + "$")]
    private static partial Regex KanaOnly();
}
