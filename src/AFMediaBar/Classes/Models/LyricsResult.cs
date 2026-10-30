namespace AFMediaBar.Classes.Models;

/// <summary>已完成取词的有效状态；请求或解析失败用 null 表示，不参与排序。</summary>
public enum LyricsResultStatus
{
    /// <summary>有可用歌词。</summary>
    Available,
    /// <summary>来源已成功响应并确认没有歌词。</summary>
    NoLyrics
}

/// <summary>
/// 歌词结果：一个来源命中的歌词文档及其来源标识；确认无歌词时保留分数并携带空文档。
/// Lyrics result: the lyric document one source matched, together with that source's identifier.
///
/// 解析在取词侧完成，因此这里携带的是已解析的 <see cref="LyricDocument"/>，而不是原始文本；呈现层只做按位置选行，
/// 不再在 UI 线程上解析歌词文本。
/// Parsing happens on the retrieval side, so this carries an already parsed <see cref="LyricDocument"/> instead of raw text,
/// and the presentation layer only selects the active line instead of parsing lyric text on the UI thread.
///
/// ⚠️ 注意 Note:
/// 这是一个不可变记录类型（record），每次修改都会创建新实例。
/// This is an immutable record type; modifications create new instances.
/// </summary>
/// <param name="Source">歌词来源标识（如 "Netease"、"LRCLIB"），只用于诊断 / Source identifier such as "Netease" or "LRCLIB"; diagnostics only.</param>
/// <param name="Document">该来源命中的歌词文档 / The lyric document this source matched.</param>
public sealed record LyricsResult(string Source, LyricDocument Document)
{
    /// <summary>获取侧计算的匹配分（0–100），用于备用结果排序。/ Retrieval-side metadata score for fallback ranking.</summary>
    public int MatchScore { get; init; }

    /// <summary>有效取词结果的状态；无歌词也保留曲目匹配分。</summary>
    public LyricsResultStatus Status { get; init; } = LyricsResultStatus.Available;

    /// <summary>
    /// 命中的证据只有标题、没有歌手或时长佐证：同名不同版本（翻唱、Live、同名片）无从区分。
    /// 这类结果不直接短路在线搜索，在线都没结果时仍然会被采用。
    /// The match rests on the title alone, with no artist or duration corroboration, so a cover or a same-titled
    /// track cannot be ruled out. Such a result does not short-circuit the online lookup but is still used when
    /// every online source comes back empty.
    /// </summary>
    public bool LowConfidence { get; init; }

    /// <summary>创建确认无歌词的结果；空文档沿用现有呈现与缓存接口。</summary>
    public static LyricsResult NoLyrics(string source, int score = 0) =>
        new(source, LyricDocument.Empty) { Status = LyricsResultStatus.NoLyrics, MatchScore = score };
}
