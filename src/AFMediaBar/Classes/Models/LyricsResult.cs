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
/// 低置信命中的成因：仲裁阶段只允许「无佐证」这一类被分差挽留，「有反证」必须继续让位给在线。
/// Why a hit is low confidence. Only "no corroboration" may be rescued by a score margin during arbitration;
/// a "contradicted" hit keeps yielding to the online lookup.
/// </summary>
public enum LyricsLowConfidenceReason
{
    /// <summary>不是低置信：歌手或时长已经提供了佐证。/ Not low confidence: artist or duration corroborates the hit.</summary>
    None = 0,

    /// <summary>
    /// 标题足够相似，但没有歌手与时长佐证：可能是同名翻唱，也可能只是文件没写标签。
    /// The title matches well but nothing corroborates it; this may be a cover, or the file may simply carry no tags.
    /// </summary>
    TitleOnly = 1,

    /// <summary>
    /// 有证据指向「不是这一首」：标题不够相似、文件名与标签的歌手相反，或版本描述冲突。
    /// Evidence points at a different track: a weak title, contradicting artist tags, or conflicting version descriptors.
    /// </summary>
    Contradicted = 2
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
    /// 命中是否为低置信；具体成因见 <see cref="LowConfidenceReason"/>。
    /// Whether the hit is low confidence; see <see cref="LowConfidenceReason"/> for the cause.
    /// </summary>
    public bool LowConfidence { get; init; }

    /// <summary>
    /// 低置信的成因；只有 <see cref="LyricsLowConfidenceReason.TitleOnly"/> 允许在仲裁阶段被分差挽留。
    /// Why this hit is low confidence; only <see cref="LyricsLowConfidenceReason.TitleOnly"/> can be rescued by a margin.
    /// </summary>
    public LyricsLowConfidenceReason LowConfidenceReason { get; init; }

    /// <summary>创建确认无歌词的结果；空文档沿用现有呈现与缓存接口。</summary>
    public static LyricsResult NoLyrics(string source, int score = 0) =>
        new(source, LyricDocument.Empty) { Status = LyricsResultStatus.NoLyrics, MatchScore = score };
}
