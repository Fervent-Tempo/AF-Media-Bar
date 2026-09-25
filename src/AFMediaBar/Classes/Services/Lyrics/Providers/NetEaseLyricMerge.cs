using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 合并网易云两个取词端点的结果，产出最终的歌词文档。
/// Merges the results of NetEase's two retrieval endpoints into the final lyric document.
///
/// 两个端点的分工（实测）：
/// - 旧端点 <c>GetLyric</c>：<c>Lrc</c> 是行级 LRC 正文，<c>Tlyric</c> 是行级译文，<c>Romalrc</c> 是音译；
/// - 新端点 <c>GetLyricNew</c>：<c>Yrc</c> 是"署名 JSON 行 + YRC 逐字行"的混合文本，<c>Ytlrc</c> 是逐字译文，
///   <c>Yromalrc</c> 是逐字音译；它的 <c>Lrc</c> 字段只有署名 JSON，**不是歌词**。
/// What the two endpoints actually carry (measured):
/// - the legacy <c>GetLyric</c>: <c>Lrc</c> holds line-level LRC, <c>Tlyric</c> the line-level translation, <c>Romalrc</c> the
///   romanization;
/// - the new <c>GetLyricNew</c>: <c>Yrc</c> holds credit JSON lines followed by YRC syllable lines, <c>Ytlrc</c> the syllable
///   translation, <c>Yromalrc</c> the syllable romanization, and its <c>Lrc</c> field carries only the credit JSON rather than
///   lyrics.
///
/// 因此主文本取自新端点的逐字字段**只在它确实被识别成逐字格式时**，否则回落到旧端点的行级正文；译文与音译分别取
/// 两个端点的并集（逐字优先）。主文本候选按顺序尝试，第一个能产出非空行的胜出，全部为空时返回 null 让兜底链继续。
/// The main text therefore comes from the new endpoint's syllable field only when that field really is recognized as a syllable
/// format, and otherwise falls back to the legacy endpoint's line-level body; translation and romanization take the union of
/// both endpoints with the syllable variant first. Candidates are tried in order and the first one that yields lines wins; when
/// every candidate is empty the merge returns null so the fallback chain continues.
/// </summary>
public static class NetEaseLyricMerge
{
    /// <summary>
    /// 合并两个端点的结果。
    /// Merges the results of both endpoints.
    /// </summary>
    /// <param name="sourceName">来源标识 / Source identifier.</param>
    /// <param name="legacy">旧端点结果，缺失为 null / Legacy endpoint result, null when unavailable.</param>
    /// <param name="wordLevel">新端点结果，缺失为 null / New endpoint result, null when unavailable.</param>
    /// <param name="request">歌词请求，提供信息行判定与时长 / Lyric request supplying info-line classification and duration.</param>
    /// <returns>歌词结果；没有可用行时为 null / The lyric result, or null without usable lines.</returns>
    public static LyricsResult? Resolve(
        string sourceName,
        LyricResult? legacy,
        LyricResult? wordLevel,
        LyricsRequest request)
    {
        var translation = Normalize(wordLevel?.Ytlrc?.Lyric) ?? Normalize(legacy?.Tlyric?.Lyric);
        var romanization = Normalize(wordLevel?.Yromalrc?.Lyric) ?? Normalize(legacy?.Romalrc?.Lyric);

        foreach (var candidate in EnumerateMainTexts(legacy, wordLevel))
        {
            var document = LyricsTextParser.Parse(
                candidate,
                translation,
                romanization,
                request,
                request.DurationSeconds,
                request.FilterInfoLines);
            if (document.Lines.Count > 0)
            {
                return new LyricsResult(sourceName, document);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateMainTexts(LyricResult? legacy, LyricResult? wordLevel)
    {
        if (HasLyrics(wordLevel))
        {
            var syllableText = Normalize(wordLevel!.Yrc?.Lyric);
            if (syllableText is not null && IsSyllableFormat(syllableText))
            {
                yield return syllableText;
            }
        }

        if (HasLyrics(legacy))
        {
            var lineLevelText = Normalize(legacy!.Lrc?.Lyric);
            if (lineLevelText is not null)
            {
                yield return lineLevelText;
            }
        }

        // 新端点的行级字段通常只有署名 JSON；个别曲目上它可能带正文，因此留在最后兜底。
        // The new endpoint's line-level field usually holds only credit JSON; on some tracks it may carry a body, so it stays
        // as the last candidate.
        if (HasLyrics(wordLevel))
        {
            var wordLevelLineText = Normalize(wordLevel!.Lrc?.Lyric);
            var legacyLineText = Normalize(legacy?.Lrc?.Lyric);
            if (wordLevelLineText is not null && !string.Equals(wordLevelLineText, legacyLineText, StringComparison.Ordinal))
            {
                yield return wordLevelLineText;
            }
        }
    }

    private static bool HasLyrics(LyricResult? result) => result is not null && !result.Nolyric;

    /// <summary>
    /// 判断逐字字段是否可以当作主文本：只有被识别成逐字或标记格式的文本才算，署名 JSON 不算。
    /// Decides whether the syllable field can serve as the main text: only text recognized as a syllable or markup format
    /// counts, while credit JSON does not.
    /// </summary>
    private static bool IsSyllableFormat(string text) => LyricsFormatDetector.Detect(text) is
        LyricsRawTypes.Yrc or
        LyricsRawTypes.Krc or
        LyricsRawTypes.Qrc or
        LyricsRawTypes.LyricifySyllable or
        LyricsRawTypes.Ttml;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
