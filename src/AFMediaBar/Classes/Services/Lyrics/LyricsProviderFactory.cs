using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 组合根使用的来源目录；目录顺序决定备用结果同分时的稳定选择，优先阶段由取词策略定义。
/// Provider catalogue for the composition root; order breaks fallback score ties, while policy owns the preferred stage.
/// </summary>
public static class LyricsProviderFactory
{
    /// <summary>
    /// 按权威目录创建 Provider；不会在这里内联查询策略。
    /// Creates providers in catalogue order without embedding dispatch policy.
    /// </summary>
    public static IReadOnlyList<ILyricsProvider> CreateDefault()
    {
        var providers = new Dictionary<string, ILyricsProvider>(StringComparer.Ordinal)
        {
            [LyricsSourceCatalog.NetEase] = new NetEaseLyricsProvider(),
            [LyricsSourceCatalog.NetEaseSearch] = new NetEaseSearchLyricsProvider(),
            [LyricsSourceCatalog.Lrclib] = new LrclibLyricsProvider(),
            [LyricsSourceCatalog.QQMusic] = new QQMusicLyricsProvider(),
            [LyricsSourceCatalog.Kugou] = new KugouLyricsProvider(),
            [LyricsSourceCatalog.SodaMusic] = new SodaMusicLyricsProvider()
        };

        var ordered = new List<ILyricsProvider>(providers.Count);
        foreach (var sourceId in LyricsSourceCatalog.DefaultOrder)
        {
            if (providers.TryGetValue(sourceId, out var provider))
            {
                ordered.Add(provider);
            }
        }

        return ordered;
    }

    /// <summary>
    /// 建立使用默认预算与默认提供器序列的歌词服务；本地来源单独传入，不进在线来源列表，也不参与来源排序。
    /// Creates the lyric service with the default budgets and provider sequence; the local source is passed separately,
    /// never joins the online list, and takes no part in source ordering.
    /// </summary>
    public static LyricsService CreateDefaultService() =>
        new(LyricsService.DefaultPerSourceBudget, LyricsService.DefaultTotalBudget, new LocalFileLyricsProvider(), [.. CreateDefault()]);
}
