using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 解析启用来源；null 表示全部、空数组表示全部关闭，未知来源忽略。
/// 默认规则由 LyricsRetrievalPolicy 仲裁；手动规则采用独立优先级。
/// </summary>
public static class LyricsSourcePolicy
{
    /// <summary>先放指定顺序中的现有来源，遗漏来源按目录顺序追加；未知身份只在运行时忽略。</summary>
    public static IReadOnlyList<string> OrderSourceIds(IReadOnlyList<string> sourceIds, IReadOnlyList<string>? priorityIds) =>
        (priorityIds ?? []).Concat(sourceIds).Where(sourceIds.Contains).Distinct(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<ILyricsProvider> ApplyManualOrder(IReadOnlyList<ILyricsProvider> providers, LyricsSourceSettings settings)
    {
        if (settings.Rule != LyricsSourceRule.Manual)
            return providers;
        var ids = OrderSourceIds(providers.Select(provider => provider.SourceName).ToArray(), settings.PrioritySourceIds);
        return ids.Select(id => providers.First(provider => provider.SourceName == id)).ToArray();
    }

    /// <summary>将播放器标识与歌词目录关联；不判断启用状态，也不依赖播放器适配器实现。</summary>
    public static bool IsCurrentPlaybackSource(string? playbackSourceId, string lyricsSourceId)
    {
        if (string.IsNullOrWhiteSpace(playbackSourceId)) return false;
        string[] markers = lyricsSourceId switch
        {
            LyricsSourceCatalog.QQMusic => ["qqmusic"],
            LyricsSourceCatalog.NetEase or LyricsSourceCatalog.NetEaseSearch => ["cloudmusic", "netease", "163music"],
            LyricsSourceCatalog.Kugou => ["kugou", "kgmusic"],
            LyricsSourceCatalog.SodaMusic => ["sodamusic", "汽水音乐"],
            _ => []
        };
        return markers.Any(marker => playbackSourceId.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析本次取词要用的提供器。
    /// Resolves the providers one retrieval uses.
    /// </summary>
    /// <param name="providers">全部提供器（组合根构造的默认顺序）/ Every provider in the default order built at the composition root.</param>
    /// <param name="settings">用户的来源设置 / The user's source settings.</param>
    /// <returns>已启用的提供器；手动规则按独立优先级排列，全关时为空。</returns>
    public static IReadOnlyList<ILyricsProvider> ResolveActive(
        IReadOnlyList<ILyricsProvider> providers,
        LyricsSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(providers);
        settings = settings.Normalize();
        if (providers.Count == 0)
        {
            return [];
        }

        var enabledIds = settings.Normalize().EnabledSourceIds;
        if (enabledIds is null)
        {
            return ApplyManualOrder(providers, settings);
        }

        if (enabledIds.Count == 0)
        {
            return [];
        }

        var byId = new Dictionary<string, ILyricsProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            byId.TryAdd(provider.SourceName, provider);
        }

        var active = new List<ILyricsProvider>(enabledIds.Count);
        foreach (var id in enabledIds)
        {
            if (byId.TryGetValue(id, out var provider) && !active.Contains(provider))
            {
                active.Add(provider);
            }
        }

        return settings.Rule == LyricsSourceRule.Manual
            ? ApplyManualOrder(providers.Where(active.Contains).ToArray(), settings)
            : active;
    }
}
