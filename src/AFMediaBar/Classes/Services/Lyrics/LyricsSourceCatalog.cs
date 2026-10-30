using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词来源的词汇表：稳定 id、目录顺序与显示名。
/// The vocabulary of lyric sources: stable ids, catalog order, and display names.
///
/// 来源 id 就是提供器的 <c>SourceName</c>，它们在设置文件里参与序列化，因此只能追加、不能改名；显示名一律走文案键，
/// 界面因此不会出现内部 id。设置页、备用源同分排序与完整层的来源显示都读这里；优先阶段另由取词策略决定。
/// A source id is a provider's <c>SourceName</c>; ids take part in settings serialization, so they may only be appended and
/// never renamed, while every display name goes through a text key so the interface never shows an internal id. The settings
/// page, fallback tie ordering, and full panel read this table; the preferred stage is owned by the retrieval policy.
/// </summary>
public static class LyricsSourceCatalog
{
    /// <summary>网易云来源专用提供器（播放器本身是网易云时按 id 精确取词）。/ The NetEase source-specific provider, retrieving by id while NetEase itself plays.</summary>
    public const string NetEase = "Netease";

    /// <summary>网易云搜索兜底提供器（其他播放器时按曲名与歌手搜索 song id）。/ The NetEase search fallback, which looks the song id up by title and artist for other players.</summary>
    public const string NetEaseSearch = "NeteaseSearch";

    /// <summary>LRCLIB。/ LRCLIB.</summary>
    public const string Lrclib = "LRCLIB";

    /// <summary>QQ 音乐。/ QQ Music.</summary>
    public const string QQMusic = "QQMusic";

    /// <summary>酷狗音乐。/ Kugou Music.</summary>
    public const string Kugou = "Kugou";

    /// <summary>汽水音乐。/ Soda Music.</summary>
    public const string SodaMusic = "SodaMusic";

    /// <summary>
    /// 本地歌词文件来源。它不在在线目录里，也不参与来源排序：取词时固定先跑这一阶段，未命中才联网。
    /// The local lyric-file source. It is not part of the online catalogue and never takes part in source ordering: retrieval
    /// always runs it first and only falls back to the network when it misses.
    /// </summary>
    public const string LocalFile = "LocalFile";

    /// <summary>
    /// 来源展示与备用结果同分时的稳定顺序，不决定优先阶段。
    /// Stable display and fallback tie order; does not determine the preferred stage.
    /// </summary>
    public static IReadOnlyList<string> DefaultOrder { get; } =
    [
        NetEase,
        NetEaseSearch,
        Lrclib,
        QQMusic,
        Kugou,
        SodaMusic
    ];

    /// <summary>
    /// 判断来源 id 是否已收录。
    /// Decides whether a source id is known.
    /// </summary>
    /// <param name="sourceId">来源 id / Source id.</param>
    public static bool IsKnown(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && DefaultOrder.Contains(sourceId.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// 取来源的显示名。
    /// Resolves a source's display name.
    ///
    /// 未知 id 原样返回：它只会出现在诊断或旧设置文件里，界面显示原始 id 好过显示一片空白。
    /// An unknown id is returned as-is: it can only come from diagnostics or an old settings file, and showing the raw id beats
    /// showing nothing.
    /// </summary>
    /// <param name="sourceId">来源 id / Source id.</param>
    public static string GetDisplayName(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return Translations.Get("Lyrics.Source.Unknown");
        }

        var id = sourceId.Trim();
        var key = $"Lyrics.Source.{id}";
        var text = Translations.Get(key);
        return string.IsNullOrWhiteSpace(text) || string.Equals(text, key, StringComparison.Ordinal) ? id : text;
    }
}
