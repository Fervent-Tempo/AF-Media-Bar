using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 一次取词的并发与采纳参数。由 <see cref="LyricsService"/> 在发起前从设置解析，测试因此可以显式注入而不碰全局设置。
/// Retrieval options of one lookup: concurrency plus adoption. The <see cref="LyricsService"/> resolves them from the
/// settings before dispatching, so tests can inject them explicitly without touching the global settings.
/// </summary>
/// <param name="AdoptionMode">结果采纳策略 / The result-adoption mode.</param>
/// <param name="BatchSize">优先级来源每批并发个数 / How many priority sources are dispatched per batch.</param>
/// <param name="AdoptionDeadline">候补出现后留给默认接口的倒计时 / Countdown left to the default interface once a candidate exists.</param>
public sealed record LyricsRetrievalOptions(
    LyricsAdoptionMode AdoptionMode,
    int BatchSize,
    TimeSpan AdoptionDeadline)
{
    /// <summary>内置默认：偏心默认来源 + 2 秒倒计时 + 批次 3。/ The built-in default: prefer the default source with a 2 s deadline and batches of three.</summary>
    public static LyricsRetrievalOptions Default { get; } = new(
        LyricsAdoptionMode.PreferDefaultSourceWithDeadline,
        LyricsConcurrencyDefaults.BatchSizeDefault,
        TimeSpan.FromMilliseconds(LyricsConcurrencyDefaults.AdoptionDeadlineMillisecondsDefault));

    /// <summary>
    /// 按当前设置解析参数。
    /// Resolves the options from the current settings.
    /// </summary>
    public static LyricsRetrievalOptions FromSettings()
    {
        var settings = SettingsManager.Current;
        return new(
            settings.LyricsAdoptionMode,
            settings.LyricsConcurrencyBatchSize,
            TimeSpan.FromMilliseconds(settings.LyricsAdoptionDeadlineMilliseconds));
    }
}

/// <summary>
/// 并发取词的两个静态决策：AppID 到默认来源的映射，与优先级来源的批次计划。
/// The two static decisions of concurrent retrieval: the AppID-to-default-source mapping and the batching plan of the
/// priority sources.
///
/// 映射表参考 Lyrix 的 id2player（D:\project\Rust\Lyrix\src\models\music_player.rs），但按"包含"匹配而不是全等：
/// SMTC 的 SourceAppUserModelId 各家形态不一（商店版带包名后缀、进程名式、中文显示名式），包含匹配一次覆盖所有形态。
/// 识别不出的 AppID 返回 null——没有默认接口就退化为纯优先级批次，链路不因此失效。
/// The mapping follows Lyrix's id2player but matches by containment rather than equality: SMTC source ids vary in shape
/// (store-package suffixes, process-name style, Chinese display names), and one containment rule covers them all. An
/// unrecognized id maps to null — without a default interface the chain degrades to plain priority batching and keeps working.
///
/// 批次规则：用户关掉的来源不进批次（来源开关只管优先级列表）；默认接口不占批次名额，且若它恰好也在优先级列表里
/// 则去重只发一次；顺序完全保留用户给定次序。
/// Batching: sources the user turned off never enter a batch (the source toggles govern the priority list only), the
/// default interface takes no batch slot and is de-duplicated when it also sits in the list, and the user's order is kept as-is.
/// </summary>
public static class LyricsConcurrencyPolicy
{
    /// <summary>
    /// 把请求携带的来源应用标识映射成默认取词来源 id。
    /// Maps the request's source-application identifier onto the default lyric-source id.
    /// </summary>
    /// <param name="sourceAppId">SMTC 的 SourceAppUserModelId 原文，未知为 null / The raw SMTC SourceAppUserModelId, null when unknown.</param>
    /// <param name="netEaseSongId">网易云歌曲 id；非空说明走网易直连路径，按 id 精确取词 / NetEase song id; non-null means the NetEase direct path, which retrieves by id.</param>
    /// <returns>默认来源 id；识别不出时为 null / The default source id, or null when unrecognized.</returns>
    public static string? MapDefaultSource(string? sourceAppId, string? netEaseSongId)
    {
        // 网易直连路径自带 song id，精确来源就是默认接口，不依赖 AppID。
        // The NetEase direct path carries a song id already, so the exact source is the default interface and no AppID is needed.
        if (!string.IsNullOrWhiteSpace(netEaseSongId))
        {
            return LyricsSourceCatalog.NetEase;
        }

        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return null;
        }

        // SMTC 路径没有 song id（系统只给标题与歌手），因此网易云播放器映射到搜索兜底而不是按 id 的精确来源。
        // The SMTC path has no song id (the system only offers title and artist), so the NetEase player maps onto the
        // search fallback rather than the id-based exact source.
        if (sourceAppId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
            sourceAppId.Contains("netease", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsSourceCatalog.NetEaseSearch;
        }

        if (sourceAppId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsSourceCatalog.QQMusic;
        }

        if (sourceAppId.Contains("kugou", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsSourceCatalog.Kugou;
        }

        if (sourceAppId.Contains("soda", StringComparison.OrdinalIgnoreCase) ||
            sourceAppId.Contains("汽水", StringComparison.Ordinal))
        {
            return LyricsSourceCatalog.SodaMusic;
        }

        return null;
    }

    /// <summary>
    /// 把优先级来源切成并发批次。
    /// Cuts the priority sources into concurrent batches.
    /// </summary>
    /// <param name="priorityProviders">启用的优先级来源，按用户顺序 / The enabled priority sources in the user's order.</param>
    /// <param name="defaultProvider">默认接口的提供器；为 null 表示本轮没有默认接口 / The default interface's provider, or null when this lookup has none.</param>
    /// <param name="batchSize">每批并发个数 / How many sources are dispatched per batch.</param>
    /// <returns>非空的批次序列，依用户顺序切分 / A sequence of non-empty batches cut along the user's order.</returns>
    public static IReadOnlyList<IReadOnlyList<ILyricsProvider>> PlanBatches(
        IReadOnlyList<ILyricsProvider> priorityProviders,
        ILyricsProvider? defaultProvider,
        int batchSize)
    {
        var size = LyricsConcurrencyDefaults.NormalizeBatchSize(batchSize);
        var queue = new List<ILyricsProvider>(priorityProviders.Count);
        foreach (var provider in priorityProviders)
        {
            // 默认接口与优先级批次去重：同一个来源只发一次请求。
            // De-duplicate against the default interface: one source means one request.
            if (ReferenceEquals(provider, defaultProvider) || queue.Contains(provider))
            {
                continue;
            }

            queue.Add(provider);
        }

        var batches = new List<IReadOnlyList<ILyricsProvider>>((queue.Count + size - 1) / size);
        for (var index = 0; index < queue.Count; index += size)
        {
            var count = Math.Min(size, queue.Count - index);
            batches.Add(queue.GetRange(index, count));
        }

        return batches;
    }
}
