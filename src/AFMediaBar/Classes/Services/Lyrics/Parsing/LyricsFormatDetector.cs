using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lyricify.Lyrics.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 按内容识别歌词文本的格式。
/// Detects the lyric text format from its content.
///
/// 识别必须由本项目负责：已引用的 Lyricify 0.2.0 里 <c>TypeHelper.GetLyricsTypes</c> 对任何输入都返回 Unknown，
/// 依赖它会把所有非 LRC 来源都当成"没有歌词"。规则本身与各格式的公开写法一一对应，因此它是可单测的纯策略。
/// Detection has to live here: <c>TypeHelper.GetLyricsTypes</c> in the referenced Lyricify 0.2.0 returns Unknown for every
/// input, and relying on it would treat every non-LRC source as "no lyrics". Each rule matches one documented format
/// spelling, which keeps this a unit-testable pure policy.
///
/// 判定顺序即优先级：先排除逐字格式，再落到行级格式，最后才是 LRC，避免把逐字歌词误判成 Lyricify Lines 或 LRC。
/// The order is the precedence: syllable formats are ruled out first, then line-level formats, and LRC last, which keeps
/// syllable lyrics from being mistaken for Lyricify Lines or LRC.
///
/// 整段 JSON/XML 包装（YRC Full、QRC Full、TTML）只有在整段确实是一份 JSON 或 XML 时才成立；解析失败说明它是混合载荷
/// （实测网易云新版端点的逐字字段就是"署名 JSON 行 + YRC 行"），此时继续按行扫描，而不是把整首歌判成无法识别。
/// A whole-document JSON or XML wrapper (YRC Full, QRC Full, TTML) is only recognized when the whole text really is one JSON
/// or XML value; a failed parse means the payload is mixed (the new NetEase word-level field really is "credit JSON lines plus
/// YRC lines"), and line scanning continues instead of writing the whole song off as unrecognizable.
/// </summary>
public static class LyricsFormatDetector
{
    private const string TtmlNamespace = "http://www.w3.org/ns/ttml";

    private const RegexOptions LineOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    /// <summary>一格 LRC 时间戳行：一个或多个 [分:秒.毫秒] 前缀。
    /// An LRC timestamp line: one or more [minutes:seconds.fraction] prefixes.</summary>
    private static readonly Regex LrcLine = new(
        @"^[ \t]*(?:\[\d+:\d{1,2}(?:[.:]\d{1,3})?\])+[^\r\n]*",
        LineOptions);

    /// <summary>QRC Full 的 LyricContent 属性原文：属性值不含未转义引号，因此 [^"] 足够，换行保留在捕获里。
    /// The raw LyricContent attribute of a QRC Full document: the value carries no unescaped quote, so [^"] suffices and
    /// the newlines stay inside the capture.</summary>
    private static readonly Regex QrcContentRegex = new(
        @"LyricContent\s*=\s*""(?<content>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Lyricify Lines 与逐字格式共有的行头：[开始,结束] 或 [开始,时长]。
    /// The line header shared by Lyricify Lines and the syllable formats: [start,end] or [start,duration].</summary>
    private static readonly Regex BracketedLine = new(
        @"^[ \t]*\[\d+,\d+\][^\r\n]*",
        LineOptions);

    /// <summary>QRC 行：[行起,行时长]文本(字起,字时长)。
    /// A QRC line: [lineStart,lineDuration]text(syllableStart,syllableDuration).</summary>
    private static readonly Regex QrcLine = new(
        @"^[ \t]*\[\d+,\d+\][^\r\n]*\(-?\d+,\d+\)[^\r\n]*",
        LineOptions);

    /// <summary>KRC 行：[行起,行时长]&lt;字起,字时长,保留值&gt;文本。
    /// A KRC line: [lineStart,lineDuration]&lt;syllableStart,syllableDuration,reserved&gt;text.</summary>
    private static readonly Regex KrcLine = new(
        @"^[ \t]*\[\d+,\d+\]<-?\d+,\d+,\d+>[^\r\n]*",
        LineOptions);

    /// <summary>YRC 行：[行起,行时长](字起,字时长,保留值)文本。
    /// A YRC line: [lineStart,lineDuration](syllableStart,syllableDuration,reserved)text.</summary>
    private static readonly Regex YrcLine = new(
        @"^[ \t]*\[\d+,\d+\]\(-?\d+,\d+,\d+\)[^\r\n]*",
        LineOptions);

    /// <summary>Lyricify Syllable 行：[行属性]文本(字起,字时长)。
    /// A Lyricify Syllable line: [lineProperty]text(syllableStart,syllableDuration).</summary>
    private static readonly Regex LyricifySyllableLine = new(
        @"^[ \t]*\[\d+\][^\r\n]*\(-?\d+,\d+\)[^\r\n]*",
        LineOptions);

    /// <summary>任意受支持的逐字时间片段，用于把逐字歌词与 Lyricify Lines 区分开。
    /// Any supported syllable timing span, used to tell syllable lyrics apart from Lyricify Lines.</summary>
    private static readonly Regex AnySyllableTiming = new(
        @"(?:\(-?\d+,\d+(?:,\d+)?\)|<-?\d+,\d+,\d+>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>XML 无法解析时的 QRC Full 特征：Lyric_1 节点带 LyricContent 属性。
    /// The QRC Full marker used when the XML cannot be parsed: a Lyric_1 element carrying a LyricContent attribute.</summary>
    private static readonly Regex QrcFullFallback = new(
        @"<Lyric_1\b[^>]*\bLyricContent\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// 识别歌词文本的原始格式。
    /// Detects the raw format of lyric text.
    /// </summary>
    /// <param name="text">歌词文本 / Lyric text.</param>
    /// <returns>识别出的原始格式；无法识别时返回 <see cref="LyricsRawTypes.Unknown"/>。
    /// The detected raw format, or <see cref="LyricsRawTypes.Unknown"/> when it cannot be recognized.</returns>
    public static LyricsRawTypes Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LyricsRawTypes.Unknown;
        }

        var trimmed = text.TrimStart();
        if (trimmed[0] == '{')
        {
            // 只在整段确实是一份 JSON 时才按包装处理；网易云的逐字载荷是"署名 JSON 行 + YRC 行"的混合文本，
            // 这里必须继续往下按行扫描，否则整首歌会被判成无法识别，歌词就此丢失。
            // The wrapper is only recognized when the whole text really is one JSON value: NetEase's word-level payload mixes
            // credit JSON lines with YRC lines, so detection has to keep scanning lines instead of giving up on the whole song,
            // which would lose its lyrics entirely.
            if (IsYrcFullJson(text))
            {
                return LyricsRawTypes.YrcFull;
            }
        }
        else if (trimmed[0] == '<')
        {
            var markupType = DetectMarkup(text);
            if (markupType != LyricsRawTypes.Unknown)
            {
                return markupType;
            }
        }

        if (KrcLine.IsMatch(text))
        {
            return LyricsRawTypes.Krc;
        }

        if (YrcLine.IsMatch(text))
        {
            return LyricsRawTypes.Yrc;
        }

        if (LyricifySyllableLine.IsMatch(text))
        {
            return LyricsRawTypes.LyricifySyllable;
        }

        if (QrcLine.IsMatch(text))
        {
            return LyricsRawTypes.Qrc;
        }

        if (BracketedLine.IsMatch(text) && !AnySyllableTiming.IsMatch(text))
        {
            return LyricsRawTypes.LyricifyLines;
        }

        return LrcLine.IsMatch(text) ? LyricsRawTypes.Lrc : LyricsRawTypes.Unknown;
    }

    /// <summary>
    /// 取出包装格式里的歌词正文（QRC Full 的 XML 与 YRC Full 的 JSON）。
    /// Extracts the lyric body from a wrapper format (QRC Full XML and YRC Full JSON).
    /// </summary>
    /// <param name="text">原始文本 / Raw text.</param>
    /// <param name="rawType">识别出的格式 / Detected format.</param>
    /// <returns>正文文本；无法取出时返回 null / The body text, or null when it cannot be extracted.</returns>
    public static string? TryUnwrap(string? text, LyricsRawTypes rawType)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return rawType switch
        {
            LyricsRawTypes.QrcFull => ExtractQrcContent(text),
            LyricsRawTypes.YrcFull => ExtractYrcContent(text),
            _ => text
        };
    }

    private static LyricsRawTypes DetectMarkup(string text)
    {
        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return LyricsRawTypes.Unknown;
            }

            if (root.DescendantsAndSelf().Any(element =>
                    element.Name.LocalName.Equals("Lyric_1", StringComparison.OrdinalIgnoreCase)
                    && element.Attributes().Any(attribute =>
                        attribute.Name.LocalName.Equals("LyricContent", StringComparison.OrdinalIgnoreCase))))
            {
                return LyricsRawTypes.QrcFull;
            }

            if (root.Name.LocalName.Equals("tt", StringComparison.OrdinalIgnoreCase)
                && root.Name.NamespaceName.Equals(TtmlNamespace, StringComparison.Ordinal))
            {
                return LyricsRawTypes.Ttml;
            }
        }
        catch
        {
            if (QrcFullFallback.IsMatch(text))
            {
                return LyricsRawTypes.QrcFull;
            }
        }

        return LyricsRawTypes.Unknown;
    }

    private static bool IsYrcFullJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("yrc", out var yrc)
                   && yrc.ValueKind == JsonValueKind.Object
                   && yrc.TryGetProperty("lyric", out var lyric)
                   && lyric.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractQrcContent(string text)
    {
        try
        {
            // XML 属性值规范化会把属性里的字面换行折成空格（XML 规范行为），整首歌词会黏成一行，
            // 库的 QRC 解析器随后只认出元数据头。因此这里用正则取属性原文并自行解码实体，保住换行；
            // 正则取不到（引号形态意外、转义特殊）再退回 XML 解析——宁可退化也不要因为一个来源丢掉整首歌词。
            // XML attribute-value normalization folds the literal newlines inside an attribute into spaces (spec
            // behaviour), gluing the whole lyric into one line that the QRC parser then reads as the metadata header only.
            // A regex therefore extracts the raw attribute value and decodes the entities here, keeping the newlines; the
            // XML parse remains the fallback for unexpected quote shapes.
            var match = QrcContentRegex.Match(text);
            if (match.Success)
            {
                var raw = System.Net.WebUtility.HtmlDecode(match.Groups["content"].Value);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
        }
        catch
        {
            // 退回 XML 解析。 / Falls back to the XML parse.
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var content = document.Root?
                .DescendantsAndSelf()
                .Where(element => element.Name.LocalName.Equals("Lyric_1", StringComparison.OrdinalIgnoreCase))
                .SelectMany(static element => element.Attributes())
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("LyricContent", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractYrcContent(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("yrc", out var yrc)
                && yrc.TryGetProperty("lyric", out var lyric)
                && lyric.ValueKind == JsonValueKind.String)
            {
                var content = lyric.GetString();
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
