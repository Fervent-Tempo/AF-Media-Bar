using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Helpers.Optimization;
using Lyricify.Lyrics.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>解析结果、"是否由纯音乐标记确认无歌词"以及是否含正文；只供取词侧区分状态。</summary>
/// <remarks>
/// A parse result plus whether an instrumental marker confirmed no lyrics and whether any catalogued line is body;
/// only the retrieval side uses these flags.
/// </remarks>
internal readonly record struct LyricsParseResult(LyricDocument Document, bool NoLyricsConfirmed, bool HasBody);

/// <summary>
/// 把任意来源的原始歌词文本解析成统一的歌词文档。
/// Parses raw lyric text from any source into the unified lyric document.
///
/// 解析由 Lyricify 承担：先用 <see cref="LyricsFormatDetector.Detect"/> 按内容识别真实格式（LRC、YRC、QRC、KRC、Lyricify Syllable、TTML 等），
/// 再用 <see cref="ParseHelper"/> 得到行与音节模型，最后在这里映射成项目的不可变模型，并在映射过程中完成四件事：
/// 1. 把双语歌词里同时间戳的成组行拆成主歌词、译文、音译三份文本，交给既有的按时间戳贴合流程（见 <see cref="SplitBilingualLines"/>）；
/// 2. 丢弃作者/作曲/制作等信息行（静置层只有两行位置，见 <see cref="LyricInfoLinePolicy"/>）；
/// 3. 把独立传入的译文与音译文本按时间戳贴到对应行（容差 500 毫秒，对不上就不沿用上一句）；
/// 4. 补齐行与音节的结束时间（<see cref="LyricTimingPolicy"/>）。
/// The heavy lifting belongs to Lyricify: <see cref="LyricsFormatDetector.Detect"/> identifies the real format from the content and
/// <see cref="ParseHelper"/>
/// yields the line and syllable model, and this type maps that into the project's immutable model while doing four things:
/// merging the same-timestamp groups bilingual lyrics use for original, translation, and romanization into three texts that
/// feed the existing timestamp-matching pass (before parsing, see
/// <see cref="SplitBilingualLines"/>), dropping credit lines (the rest layer only has two rows, see <see cref="LyricInfoLinePolicy"/>),
/// attaching separately supplied translation and romanization text by timestamp with a 500 ms tolerance (never carrying a
/// previous line forward), and completing line and syllable end times (<see cref="LyricTimingPolicy"/>).
/// </summary>
public static partial class LyricsTextParser
{
    /// <summary>独立译文文本与主行匹配时允许的最大时间差（秒）。
    /// Maximum time difference in seconds allowed when matching separately supplied translation text to a main line.</summary>
    public const double TranslationMatchToleranceSeconds = 0.5;

    /// <summary>带 LRC 时间戳的行：一个或多个时间戳标签加正文。元数据标签（[ar:…]）不匹配。
    /// A line carrying LRC timestamp tags followed by its text; metadata tags such as [ar:…] do not match.</summary>
    [GeneratedRegex(@"^(?<tags>(?:\[\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?\])+)(?<content>.*)$")]
    private static partial Regex TimedLine();

    /// <summary>
    /// 解析歌词文本。
    /// Parses lyric text.
    /// </summary>
    /// <param name="rawText">主歌词文本，格式由内容识别 / Main lyric text; the format is detected from the content.</param>
    /// <param name="translationText">可选的独立译文文本 / Optional separately supplied translation text.</param>
    /// <param name="romanizationText">可选的独立音译文本 / Optional separately supplied romanization text.</param>
    /// <param name="request">曲目元数据，用于信息行判定 / Track metadata, used for info-line classification.</param>
    /// <param name="durationSeconds">曲目时长（秒），用于补齐末行结束时间 / Track duration in seconds, used to complete the last line's end.</param>
    /// <param name="filterInfoLines">是否丢弃作者、作曲、制作等信息行 / Whether credit lines such as writer, composer, and producer are dropped.</param>
    /// <returns>歌词文档；无法识别、解析失败、没有时间轴或只剩占位文本时返回空行文档，界面因此回落到标题与歌手。
    /// 纯音乐载荷也返回空文档，但其确认状态经 <see cref="ParseCore"/> 交给取词侧，以便来源报告"确认无歌词"。
    /// The lyric document; an unrecognized, unparsable, untimed, or placeholder-only input yields a document without lines, so
    /// the UI falls back to title and artist. An instrumental-only payload also yields an empty document, but its confirmation
    /// reaches the retrieval provider through <see cref="ParseCore"/> so the source can report a confirmed no-lyrics result.</returns>
    public static LyricDocument Parse(
        string? rawText,
        string? translationText = null,
        string? romanizationText = null,
        LyricsRequest? request = null,
        double? durationSeconds = null,
        bool filterInfoLines = true) =>
        ParseCore(rawText, translationText, romanizationText, request, durationSeconds, filterInfoLines).Document;

    /// <summary>
    /// 解析歌词文本，并额外报告确认无歌词与是否含正文。
    /// Parses lyric text and additionally reports a no-lyrics confirmation and whether the document contains body lines.
    /// </summary>
    internal static LyricsParseResult ParseCore(
        string? rawText,
        string? translationText,
        string? romanizationText,
        LyricsRequest? request,
        double? durationSeconds,
        bool filterInfoLines)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new LyricsParseResult(LyricDocument.Empty, false, false);
        }

        // 只用枚举名做格式标识：库的显示名辅助在不同版本里位置不同，诊断不依赖它。
        // The enum name alone identifies the format: the library's display-name helper moved between versions, and diagnostics
        // must not depend on it.
        var (mainText, embeddedTranslation, embeddedRomanization, pairs, triplets) = SplitBilingualLines(rawText);
        var (data, rawType) = ParsePayload(mainText);
        if (rawType == LyricsRawTypes.Unknown)
            return new LyricsParseResult(LyricDocument.Empty, false, false);
        var formatName = rawType.ToString();
        if (pairs + triplets > 0)
        {
            // 双语歌词的拆分结果要能在日志里看到：哪份文件、二合一还是三合一、各多少组。
            AppLogService.Current?.Info("Lyrics",
                $"双语歌词: title=\"{request?.Title}\" format={formatName} pairs={pairs} triplets={triplets}");
        }
        if (data?.Lines is not { Count: > 0 })
        {
            return new LyricsParseResult(new LyricDocument([], LyricsSyncType.Unsynced, formatName), false, false);
        }

        var track = CreateTrackMetadata(request, durationSeconds);
        var syncType = ResolveSyncType(data);
        var kinds = ClassifyLines(data.Lines);
        // 逐行信息行判定同时服务"确认无歌词"和"是否含正文"；位置推断会把正文首尾误标为信息行，这里不使用。
        // The direct per-line info decision serves both confirmation and body detection; positional inference would mislabel
        // body lines at the edges and is deliberately not used here.
        var directInfoLines = ResolveDirectInfoLines(data.Lines, track);
        var hasBody = false;
        // 文件内嵌的译文与音译和同目录的附件走同一条按时间戳贴合的路；外部附件写了就以外部为准。
        var translations = ParseTimestampedText(translationText ?? embeddedTranslation);
        var romanizations = ParseTimestampedText(romanizationText ?? embeddedRomanization);
        var resolvedDuration = durationSeconds ?? request?.DurationSeconds ?? 0d;

        // 网易云纯音乐载荷是"署名行 + 纯音乐提示"：署名比例过高时信息行过滤会整体放弃，占位提示再被丢弃，
        // 剩余署名就会被当成歌词。只有明确纯音乐标记且其余可呈现行全是署名时才走纯音乐分支；正文永远优先。
        // A NetEase instrumental payload is credit lines plus an instrumental marker: when credits exceed the anti-deletion
        // share the filter is abandoned and the placeholder is dropped, leaving credits as lyrics. Only an explicit marker
        // with no body enters the instrumental branch; a real lyric always wins.
        if (kinds.Contains(LyricPlaceholderKind.Instrumental) &&
            LyricNoLyricsPolicy.ConfirmsNoLyrics(kinds, directInfoLines))
        {
            if (request?.HideInstrumentalLyrics == true)
            {
                // 用户选择纯音乐只显示标题：不显示提示行，按确认无歌词缓存。
                // The user asked for title-only instrumental tracks: hide the notice and cache a confirmed no-lyrics result.
                return new LyricsParseResult(
                    new LyricDocument([], syncType == LyricsSyncType.Unknown ? LyricsSyncType.Unsynced : syncType, formatName),
                    true,
                    false);
            }

            // 默认只呈现"纯音乐，请欣赏"这一行；署名不进入文档，也不受信息行防误删阈值影响。
            // By default only the "纯音乐，请欣赏" line is presented; credits never enter the document, regardless of the
            // anti-deletion threshold that otherwise keeps a few flagged lines.
            var markerDrafts = CreateInstrumentalDrafts(data.Lines, kinds, translations, romanizations);
            if (markerDrafts.Count > 0)
            {
                var markerLines = LyricTimingPolicy.Resolve(markerDrafts, resolvedDuration);
                return new LyricsParseResult(
                    new LyricDocument(markerLines,
                        syncType == LyricsSyncType.Unknown ? InferSyncType(markerLines) : syncType, formatName),
                    false,
                    false);
            }
        }

        var dropMask = filterInfoLines ? ResolveDropMask(data.Lines, track) : new bool[data.Lines.Count];
        var drafts = new List<LyricLineDraft>(data.Lines.Count);

        for (var i = 0; i < data.Lines.Count; i++)
        {
            var line = data.Lines[i];
            if (line is SyllableLineInfo { Syllables: not { Count: > 0 } })
                continue;
            if (line.StartTime is not { } startMilliseconds)
            {
                // 没有时间戳的行无法参与按位置选行；整首都没有时间戳时下面会按"无时间轴"处理。
                // A line without a timestamp cannot take part in position-based selection; a document that has none at all
                // is reported as unsynced below.
                continue;
            }

            if (i < dropMask.Length && dropMask[i])
            {
                continue;
            }

            var start = startMilliseconds / 1000d;
            var text = ResolveLineText(line);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // 空行、暂无需歌词提示和纯音乐标记都不是歌词正文：它们带着时间戳通过解析后也不能进入文档。
            // Blank lines, transient placeholders, generic no-lyrics placeholders, and instrumental markers are not lyric
            // body: even when they pass parsing with a timestamp, they must not enter the document.
            if (kinds[i] != LyricPlaceholderKind.None)
            {
                continue;
            }

            var translation = ResolveInlineTranslation(line);
            var romanization = ResolveInlineRomanization(line);
            if (directInfoLines is null || !directInfoLines[i])
            {
                // 分类不可用时按正文处理，宁可显示也不误清空。
                // When classification is unavailable, treat the line as body so the lyric is kept rather than cleared.
                hasBody = true;
            }

            drafts.Add(new LyricLineDraft(
                start,
                line.EndTime is { } endMilliseconds ? endMilliseconds / 1000d : null,
                text,
                ResolveWords(line),
                translation ?? FindNearbyText(translations, start),
                romanization ?? FindNearbyText(romanizations, start),
                line.SubLine is not null));
        }

        if (drafts.Count == 0)
        {
            return new LyricsParseResult(
                new LyricDocument([], syncType == LyricsSyncType.Unknown ? LyricsSyncType.Unsynced : syncType, formatName),
                false,
                false);
        }

        var lines = LyricTimingPolicy.Resolve(drafts, resolvedDuration);
        return new LyricsParseResult(
            new LyricDocument(lines, syncType == LyricsSyncType.Unknown ? InferSyncType(lines) : syncType, formatName),
            false,
            hasBody);
    }

    /// <summary>
    /// 只把纯音乐提示行转成歌词行；署名与普通占位不进入结果。
    /// Converts only the instrumental notice lines into lyric lines; credits and other placeholders never enter the result.
    /// </summary>
    private static List<LyricLineDraft> CreateInstrumentalDrafts(
        List<ILineInfo> lines,
        LyricPlaceholderKind[] kinds,
        IReadOnlyList<(double Start, string Text)> translations,
        IReadOnlyList<(double Start, string Text)> romanizations)
    {
        var drafts = new List<LyricLineDraft>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (kinds[index] != LyricPlaceholderKind.Instrumental || lines[index].StartTime is not { } startMilliseconds)
            {
                continue;
            }

            var line = lines[index];
            var start = startMilliseconds / 1000d;
            var text = ResolveLineText(line);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            drafts.Add(new LyricLineDraft(
                start,
                line.EndTime is { } endMilliseconds ? endMilliseconds / 1000d : null,
                text,
                ResolveWords(line),
                ResolveInlineTranslation(line) ?? FindNearbyText(translations, start),
                ResolveInlineRomanization(line) ?? FindNearbyText(romanizations, start),
                line.SubLine is not null));
        }

        return drafts;
    }

    /// <summary>逐行分类占位文本；无法参与呈现的行按空行处理，不参与正文或标记统计。</summary>
    private static LyricPlaceholderKind[] ClassifyLines(List<ILineInfo> lines)
    {
        var kinds = new LyricPlaceholderKind[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var text = ResolveLineText(lines[index]);
            kinds[index] = IsPresentableLine(lines[index], text)
                ? LyricPlaceholderPolicy.Classify(text)
                : LyricPlaceholderKind.Blank;
        }

        return kinds;
    }

    private static string? ResolveLineText(ILineInfo line) => line.SubLine is not null ? line.FullText : line.Text;

    private static bool IsPresentableLine(ILineInfo line, string? text) =>
        line is not SyllableLineInfo { Syllables: not { Count: > 0 } } &&
        line.StartTime is not null &&
        !string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// 逐行调用信息行判定，不做标题/结尾的位置推断；分类不可用或行数不一致时返回 null。
    /// Classifies each line directly without the heading/ending positional inference; returns null when classification
    /// is unavailable or the result length does not match.
    /// </summary>
    private static bool[]? ResolveDirectInfoLines(List<ILineInfo> lines, ITrackMetadata track)
    {
        try
        {
            var flags = new bool[lines.Count];
            for (var index = 0; index < lines.Count; index++)
            {
                // 位置推断会把正文前后的行强行标成信息行，确认无歌词时必须使用逐行判定。
                // Positional inference would force lines before or after the body to look like credits; confirmation must
                // use the direct per-line decision.
                flags[index] = InfoLines.IsInfoLine(ResolveLineText(lines[index]) ?? string.Empty, track);
            }

            return flags;
        }
        catch
        {
            // 信息行判定失败只是显示优化，不能据此清空歌词。
            // Info-line classification is only a presentation nicety; its failure must never clear the lyrics.
            return null;
        }
    }

    /// <summary>
    /// 解析只用于匹配的带时间戳文本（译文、音译），返回按时间升序的片段。
    /// Parses timestamped text used only for matching (translation, romanization) into fragments ordered by time.
    /// </summary>
    internal static LyricDocument AttachRomanization(LyricDocument document, string text)
    {
        var fragments = ParseTimestampedText(text);
        return fragments.Count == 0 ? document : document with
        {
            Lines = document.Lines.Select(line =>
            {
                if (line.Romanization is not null || FindNearbyText(fragments, line.Start) is not { } romanization)
                    return line;
                return new LyricLine(line.Start, line.End, line.Text)
                {
                    Words = line.Words,
                    Translation = line.Translation,
                    Romanization = romanization,
                    IsBackground = line.IsBackground
                };
            }).ToArray()
        };
    }

    private static IReadOnlyList<(double Start, string Text)> ParseTimestampedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var (data, _) = ParsePayload(text);
        if (data?.Lines is not { Count: > 0 })
        {
            return [];
        }

        var fragments = new List<(double Start, string Text)>(data.Lines.Count);
        foreach (var line in data.Lines)
        {
            if (line is SyllableLineInfo { Syllables: not { Count: > 0 } })
                continue;
            if (line.StartTime is { } startMilliseconds && !string.IsNullOrWhiteSpace(line.Text))
            {
                fragments.Add((startMilliseconds / 1000d, line.Text.Trim()));
            }
        }

        fragments.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return fragments;
    }

    /// <summary>
    /// 把双语歌词拆成主歌词、译文、音译三份文本，各自沿用原来的时间戳，交给既有的「主歌词 + 附件」链路。
    ///
    /// 这类文件把原文、译文、音译写成同时间戳的两三行，第一行是原文。解析器会按时间戳重排，同时间戳的先后顺序
    /// 不保证保留——实测同一份文件里有的组顺序对、有的组对调——所以只能按行序在解析前拆开，不能等解析后再认。
    ///
    /// 只处理带 LRC 时间戳的行；逐字格式的时间戳不是这个写法，天然跳过。成组行不到一半时按个别重复处理——
    /// 长句拆行、对唱重叠都会出现同时间戳，那不是译文。
    /// </summary>
    private static (string Main, string? Translation, string? Romanization, int Pairs, int Triplets) SplitBilingualLines(string text)
    {
        var lines = text.Split('\n');
        var tags = new string?[lines.Length];
        var bodies = new string?[lines.Length];
        var timedLines = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var match = TimedLine().Match(lines[index].TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            tags[index] = match.Groups["tags"].Value;
            bodies[index] = match.Groups["content"].Value;
            timedLines++;
        }

        var main = new List<string>(lines.Length);
        var translation = new List<string>();
        var romanization = new List<string>();
        var pairs = 0;
        var triplets = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var groupEnd = index;
            while (tags[index] is not null && groupEnd + 1 < lines.Length && tags[groupEnd + 1] == tags[index])
            {
                groupEnd++;
            }

            // 两行是原文加译文，三行是原文、译文、音译；再多就分不清各自归属，保持原样不猜。组内有空行也不拆。
            var groupSize = groupEnd - index + 1;
            var texts = groupSize is 2 or 3 ? GroupTexts(bodies, index, groupEnd) : null;
            if (texts is not null && tags[index] is { } tag)
            {
                if (groupSize == 2) pairs++;
                else triplets++;
                main.Add(tag + texts[0]);
                translation.Add(tag + texts[1]);
                if (texts.Length == 3)
                {
                    romanization.Add(tag + texts[2]);
                }
            }
            else
            {
                // 统一去掉行尾的 \r：重建出来的文本整体按 \n 连接，不该混着两种行尾。
                for (var line = index; line <= groupEnd; line++)
                {
                    main.Add(lines[line].TrimEnd('\r'));
                }
            }

            index = groupEnd;
        }

        // 成组行不到一半时按个别重复处理：长句拆行、对唱重叠都会出现同时间戳，那不是译文。
        var groupedLines = pairs * 2 + triplets * 3;
        if (groupedLines == 0 || groupedLines * 2 < timedLines)
        {
            return (text, null, null, 0, 0);
        }

        return (string.Join('\n', main),
            translation.Count > 0 ? string.Join('\n', translation) : null,
            romanization.Count > 0 ? string.Join('\n', romanization) : null,
            pairs,
            triplets);
    }

    /// <summary>
    /// 组内各行的文本，顺序是原文、译文、音译；组内有空行时返回 null。
    /// 三行时按写法校正后两行：罗马音一定是拉丁转写，若第二行是拉丁而第三行不是，说明文件把音译放在了译文前面。
    /// </summary>
    private static string[]? GroupTexts(string?[] bodies, int start, int end)
    {
        var texts = new string[end - start + 1];
        for (var line = start; line <= end; line++)
        {
            if (string.IsNullOrWhiteSpace(bodies[line])) return null;
            texts[line - start] = bodies[line]!;
        }

        if (texts.Length == 3 && LooksLikeLatinScript(texts[1]) && !LooksLikeLatinScript(texts[2]))
        {
            (texts[1], texts[2]) = (texts[2], texts[1]);
        }

        return texts;
    }

    /// <summary>拉丁转写判断：不含汉字、假名与谚文。英文译文与罗马音都是拉丁，此时无法区分，保持文件顺序。
    /// Latin-script test: no Han, kana, or Hangul. An English translation looks the same as a romanization, so the file order wins.</summary>
    private static bool LooksLikeLatinScript(string text) =>
        !text.Any(static c => c is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF');

    private static (LyricsData? Data, LyricsRawTypes Type) ParsePayload(string text)
    {
        var type = LyricsFormatDetector.Detect(text);
        if (type == LyricsRawTypes.Unknown)
            return (null, type);

        var body = LyricsFormatDetector.TryUnwrap(text, type);
        if (type is not (LyricsRawTypes.QrcFull or LyricsRawTypes.YrcFull or LyricsRawTypes.Ttml) && text.TrimStart().StartsWith('<'))
            body = LyricsFormatDetector.ExtractLines(text, type);
        var data = body is null ? null : ParseHelper.ParseLyrics(body, NormalizeRawType(type));
        if (type is not (LyricsRawTypes.QrcFull or LyricsRawTypes.YrcFull) || data?.Lines?.Any(line =>
                line is not SyllableLineInfo { Syllables: not { Count: > 0 } } && line.StartTime.HasValue) == true)
            return (data, type);

        var lineType = LyricsFormatDetector.DetectLines(text);
        return lineType == LyricsRawTypes.Unknown
            ? (null, type)
            : (ParseHelper.ParseLyrics(LyricsFormatDetector.ExtractLines(text, lineType), lineType), lineType);
    }

    private static string? FindNearbyText(IReadOnlyList<(double Start, string Text)> fragments, double start)
    {
        if (fragments.Count == 0)
        {
            return null;
        }

        var bestDifference = TranslationMatchToleranceSeconds;
        string? bestText = null;
        foreach (var fragment in fragments)
        {
            var difference = Math.Abs(fragment.Start - start);
            if (difference <= bestDifference)
            {
                bestDifference = difference;
                bestText = fragment.Text;
            }
        }

        return bestText;
    }

    private static IReadOnlyList<LyricWord> ResolveWords(ILineInfo line)
    {
        if (line is not SyllableLineInfo syllableLine || syllableLine.Syllables is not { Count: > 0 } syllables)
        {
            return [];
        }

        var words = new List<LyricWord>(syllables.Count);
        foreach (var syllable in syllables)
        {
            if (string.IsNullOrEmpty(syllable.Text))
            {
                continue;
            }

            words.Add(new LyricWord(syllable.StartTime / 1000d, syllable.EndTime / 1000d, syllable.Text));
        }

        return words;
    }

    private static string? ResolveInlineTranslation(ILineInfo line)
    {
        if (line is not IFullLineInfo full || full.Translations is not { Count: > 0 } translations)
        {
            return null;
        }

        if (translations.TryGetValue("zh", out var chinese) && !string.IsNullOrWhiteSpace(chinese))
        {
            return chinese.Trim();
        }

        foreach (var pair in translations)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value.Trim();
            }
        }

        return null;
    }

    private static string? ResolveInlineRomanization(ILineInfo line) =>
        line is IFullLineInfo { Pronunciation: { } pronunciation } && !string.IsNullOrWhiteSpace(pronunciation)
            ? pronunciation.Trim()
            : null;

    private static bool[] ResolveDropMask(List<ILineInfo> lines, ITrackMetadata track)
    {
        try
        {
            var flagged = InfoLines.CheckInfoLines(lines, track);
            return flagged.Count == lines.Count ? LyricInfoLinePolicy.ResolveDropMask(flagged) : new bool[lines.Count];
        }
        catch
        {
            // 信息行判定只是显示优化，它自身的异常不得影响歌词本身。 / Info-line classification is a display nicety; its failure must not affect the lyrics.
            return new bool[lines.Count];
        }
    }

    private static TrackMetadata CreateTrackMetadata(LyricsRequest? request, double? durationSeconds) => new()
    {
        Title = request?.Title,
        Artist = request?.Artist,
        Album = request?.Album,
        DurationMs = ToDurationMilliseconds(durationSeconds ?? request?.DurationSeconds)
    };

    private static int? ToDurationMilliseconds(double? durationSeconds) =>
        durationSeconds is { } seconds && double.IsFinite(seconds) && seconds > 0
            ? (int)Math.Round(seconds * 1000)
            : null;

    /// <summary>
    /// 把包装格式退回库能直接解析的文本形式。
    /// Falls back from wrapper formats to a textual form the library parses directly.
    ///
    /// "Full" 变体是同一份歌词的 XML/JSON 包装，库的 <c>ParseHelper</c> 不为它们分发解析器：正文由
    /// <see cref="LyricsFormatDetector.TryUnwrap"/> 取出后按文本形式解析；正文不可用时由 ParsePayload 对原载荷扫描一次。
    /// The "Full" variants wrap the same lyrics in XML or JSON and the library's <c>ParseHelper</c> dispatches no parser for
    /// them: the body comes out of <see cref="LyricsFormatDetector.TryUnwrap"/> and is parsed as text. ParsePayload scans
    /// the original payload once when that body is unusable.
    /// </summary>
    private static LyricsRawTypes NormalizeRawType(LyricsRawTypes rawType) => rawType switch
    {
        LyricsRawTypes.QrcFull => LyricsRawTypes.Qrc,
        LyricsRawTypes.YrcFull => LyricsRawTypes.Yrc,
        _ => rawType
    };

    private static LyricsSyncType ResolveSyncType(LyricsData data) => data.File?.SyncTypes switch
    {
        SyncTypes.SyllableSynced => LyricsSyncType.SyllableSynced,
        SyncTypes.LineSynced => LyricsSyncType.LineSynced,
        SyncTypes.MixedSynced => LyricsSyncType.MixedSynced,
        SyncTypes.Unsynced => LyricsSyncType.Unsynced,
        _ => LyricsSyncType.Unknown
    };

    private static LyricsSyncType InferSyncType(IReadOnlyList<LyricLine> lines)
    {
        var withWords = 0;
        var withoutWords = 0;
        foreach (var line in lines)
        {
            if (line.Words.Count >= 2)
            {
                withWords++;
            }
            else
            {
                withoutWords++;
            }
        }

        if (withWords == 0)
        {
            return LyricsSyncType.LineSynced;
        }

        return withoutWords == 0 ? LyricsSyncType.SyllableSynced : LyricsSyncType.MixedSynced;
    }
}
