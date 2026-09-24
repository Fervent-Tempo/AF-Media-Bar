using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Helpers.Optimization;
using Lyricify.Lyrics.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 把任意来源的原始歌词文本解析成统一的歌词文档。
/// Parses raw lyric text from any source into the unified lyric document.
///
/// 解析由 Lyricify 承担：先用 <see cref="LyricsFormatDetector.Detect"/> 按内容识别真实格式（LRC、YRC、QRC、KRC、Lyricify Syllable、TTML 等），
/// 再用 <see cref="ParseHelper"/> 得到行与音节模型，最后在这里映射成项目的不可变模型，并在映射过程中完成三件事：
/// 1. 丢弃作者/作曲/制作等信息行（静置层只有两行位置，见 <see cref="LyricInfoLinePolicy"/>）；
/// 2. 把独立传入的译文与音译文本按时间戳贴到对应行（容差 500 毫秒，对不上就不沿用上一句）；
/// 3. 补齐行与音节的结束时间（<see cref="LyricTimingPolicy"/>）。
/// The heavy lifting belongs to Lyricify: <see cref="LyricsFormatDetector.Detect"/> identifies the real format from the content and
/// <see cref="ParseHelper"/>
/// yields the line and syllable model, and this type maps that into the project's immutable model while doing three things:
/// dropping credit lines (the rest layer only has two rows, see <see cref="LyricInfoLinePolicy"/>), attaching separately supplied
/// translation and romanization text by timestamp with a 500 ms tolerance (never carrying a previous line forward), and
/// completing line and syllable end times (<see cref="LyricTimingPolicy"/>).
/// </summary>
public static class LyricsTextParser
{
    /// <summary>独立译文文本与主行匹配时允许的最大时间差（秒）。
    /// Maximum time difference in seconds allowed when matching separately supplied translation text to a main line.</summary>
    public const double TranslationMatchToleranceSeconds = 0.5;

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
    /// The lyric document; an unrecognized, unparsable, untimed, or placeholder-only input yields a document without lines, so
    /// the UI falls back to title and artist.</returns>
    public static LyricDocument Parse(
        string? rawText,
        string? translationText = null,
        string? romanizationText = null,
        LyricsRequest? request = null,
        double? durationSeconds = null,
        bool filterInfoLines = true)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return LyricDocument.Empty;
        }

        var rawType = LyricsFormatDetector.Detect(rawText);
        if (rawType == LyricsRawTypes.Unknown)
        {
            return LyricDocument.Empty;
        }

        // 只用枚举名做格式标识：库的显示名辅助在不同版本里位置不同，诊断不依赖它。
        // The enum name alone identifies the format: the library's display-name helper moved between versions, and diagnostics
        // must not depend on it.
        var formatName = rawType.ToString();
        var body = LyricsFormatDetector.TryUnwrap(rawText, rawType);
        var data = body is null ? null : ParseHelper.ParseLyrics(body, NormalizeRawType(rawType));
        if (data?.Lines is not { Count: > 0 })
        {
            return new LyricDocument([], LyricsSyncType.Unsynced, formatName);
        }

        var track = CreateTrackMetadata(request, durationSeconds);
        var dropMask = filterInfoLines ? ResolveDropMask(data.Lines, track) : new bool[data.Lines.Count];
        var translations = ParseTimestampedText(translationText);
        var romanizations = ParseTimestampedText(romanizationText);
        var drafts = new List<LyricLineDraft>(data.Lines.Count);

        for (var i = 0; i < data.Lines.Count; i++)
        {
            var line = data.Lines[i];
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
            var text = line.SubLine is not null ? line.FullText : line.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // "暂无歌词"一类的占位行不是歌词：它带着时间戳通过解析后，会成为静置层唯一的一句歌词。
            // A placeholder line such as "暂无歌词" is not a lyric: it passes parsing with a timestamp and would then be the only
            // line the rest layer shows.
            if (LyricPlaceholderPolicy.IsPlaceholder(text))
            {
                continue;
            }

            var translation = ResolveInlineTranslation(line);
            var romanization = ResolveInlineRomanization(line);
            drafts.Add(new LyricLineDraft(
                start,
                line.EndTime is { } endMilliseconds ? endMilliseconds / 1000d : null,
                text,
                ResolveWords(line),
                translation ?? FindNearbyText(translations, start),
                romanization ?? FindNearbyText(romanizations, start),
                line.SubLine is not null));
        }

        var syncType = ResolveSyncType(data);
        if (drafts.Count == 0)
        {
            return new LyricDocument([], syncType == LyricsSyncType.Unknown ? LyricsSyncType.Unsynced : syncType, formatName);
        }

        var resolvedDuration = durationSeconds ?? request?.DurationSeconds ?? 0d;
        var lines = LyricTimingPolicy.Resolve(drafts, resolvedDuration);
        return new LyricDocument(lines, syncType == LyricsSyncType.Unknown ? InferSyncType(lines) : syncType, formatName);
    }

    /// <summary>
    /// 解析只用于匹配的带时间戳文本（译文、音译），返回按时间升序的片段。
    /// Parses timestamped text used only for matching (translation, romanization) into fragments ordered by time.
    /// </summary>
    private static IReadOnlyList<(double Start, string Text)> ParseTimestampedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var rawType = LyricsFormatDetector.Detect(text);
        if (rawType == LyricsRawTypes.Unknown)
        {
            return [];
        }

        var body = LyricsFormatDetector.TryUnwrap(text, rawType);
        var data = body is null ? null : ParseHelper.ParseLyrics(body, NormalizeRawType(rawType));
        if (data?.Lines is not { Count: > 0 })
        {
            return [];
        }

        var fragments = new List<(double Start, string Text)>(data.Lines.Count);
        foreach (var line in data.Lines)
        {
            if (line.StartTime is { } startMilliseconds && !string.IsNullOrWhiteSpace(line.Text))
            {
                fragments.Add((startMilliseconds / 1000d, line.Text.Trim()));
            }
        }

        fragments.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return fragments;
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
    /// <see cref="LyricsFormatDetector.TryUnwrap"/> 取出后按文本形式解析，取不出来时返回空文档并让兜底链继续走下一个来源。
    /// The "Full" variants wrap the same lyrics in XML or JSON and the library's <c>ParseHelper</c> dispatches no parser for
    /// them: the body comes out of <see cref="LyricsFormatDetector.TryUnwrap"/> and is parsed as text, and an unextractable
    /// payload yields an empty document so the fallback chain moves on to the next source.
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
