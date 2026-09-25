using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 一次取词的查询策略与采纳参数。由 <see cref="LyricsService"/> 在发起前从设置解析，测试因此可以显式注入而不碰全局设置。
/// Retrieval options of one lookup: the query strategy plus adoption. The <see cref="LyricsService"/> resolves them from the
/// settings before dispatching, so tests can inject them explicitly without touching the global settings.
/// </summary>
/// <param name="QueryStrategy">查询策略：按序或并发 / The query strategy: sequential or concurrent.</param>
/// <param name="AdoptionMode">结果采纳策略 / The result-adoption mode.</param>
/// <param name="BatchSize">优先级来源每批并发个数 / How many priority sources are dispatched per batch.</param>
/// <param name="AdoptionDeadline">候补出现后留给默认接口的倒计时 / Countdown left to the default interface once a candidate exists.</param>
public sealed record LyricsRetrievalOptions(
    LyricsQueryStrategy QueryStrategy,
    LyricsAdoptionMode AdoptionMode,
    int BatchSize,
    TimeSpan AdoptionDeadline)
{
    /// <summary>内置默认：并发 + 偏心默认来源 + 2 秒倒计时 + 批次 3。/ The built-in default: concurrent, prefer the default source with a 2 s deadline and batches of three.</summary>
    public static LyricsRetrievalOptions Default { get; } = new(
        LyricsQueryStrategy.Concurrent,
        LyricsAdoptionMode.PreferDefaultSourceWithDeadline,
        LyricsConcurrencyDefaults.BatchSizeDefault,
        TimeSpan.FromMilliseconds(LyricsConcurrencyDefaults.AdoptionDeadlineMillisecondsDefault));

    /// <summary>
    /// 按序查询的等价参数：批次 1 + 先到先得，链路逐个尝试来源——与并发机制同一代码路径，语义即传统串行链。
    /// The sequential strategy's equivalent options: a batch of one plus first-arrival, so the chain tries sources one by
    /// one — the same code path as concurrency with the classic serial chain's semantics.
    /// </summary>
    public static LyricsRetrievalOptions Sequential { get; } = new(
        LyricsQueryStrategy.Sequential,
        LyricsAdoptionMode.FirstArrival,
        1,
        TimeSpan.Zero);

    /// <summary>
    /// 按当前设置解析参数。
    /// Resolves the options from the current settings.
    /// </summary>
    public static LyricsRetrievalOptions FromSettings()
    {
        var settings = SettingsManager.Current;
        return new(
            settings.LyricsQueryStrategy,
            settings.LyricsAdoptionMode,
            settings.LyricsConcurrencyBatchSize,
            TimeSpan.FromMilliseconds(settings.LyricsAdoptionDeadlineMilliseconds));
    }
}

/// <summary>
/// 内置的默认取词接口绑定：一个播放器的真实 AppID → 默认来源。
/// A built-in default-interface binding: one player's real AppID mapped onto a default source.
///
/// 每个播放器只有一条 AppID；内置表只负责在配置文件尚未生成（首次创建或重置）时预填初始列表，
/// 写回后一切操作都发生在持久化的绑定表上，对初始表不再有任何特判。
/// Each player carries exactly one AppID; the built-in table only prefills the initial list while the settings file does
/// not exist yet (first creation or reset), and every later operation happens on the persisted table with no special casing.
/// </summary>
/// <param name="AppId">播放器的真实 AppID / The player's real AppID.</param>
/// <param name="NameKey">播放器显示名的文案键尾段（Lyrics.DefaultBindings.Player.{NameKey}） / The tail of the player's display-name text key (Lyrics.DefaultBindings.Player.{NameKey}).</param>
/// <param name="DefaultSourceId">内置默认来源 id / The built-in default source id.</param>
public sealed record LyricsBuiltInBinding(
    string AppId,
    string NameKey,
    string DefaultSourceId);

/// <summary>
/// 并发取词的两个静态决策：AppID 到默认来源的三层映射（用户绑定表 → 内置表 → 无），与优先级来源的批次计划。
/// The two static decisions of concurrent retrieval: the three-layer AppID-to-default-source mapping (user table, built-in
/// table, none) and the batching plan of the priority sources.
///
/// 批次规则：用户关掉的来源不进批次（来源开关只管优先级列表）；默认接口不占批次名额，且若它恰好也在优先级列表里
/// 则去重只发一次；顺序完全保留用户给定次序。
/// Batching: sources the user turned off never enter a batch (the source toggles govern the priority list only), the
/// default interface takes no batch slot and is de-duplicated when it also sits in the list, and the user's order is kept as-is.
/// </summary>
public static class LyricsConcurrencyPolicy
{
    /// <summary>内置默认接口绑定表：每行一个真实 AppID。/ The built-in default-interface bindings: one real AppID per row.</summary>
    public static IReadOnlyList<LyricsBuiltInBinding> BuiltInBindings { get; } =
    [
        new("cloudmusic.exe", "Netease", LyricsSourceCatalog.NetEaseSearch),
        new("qqmusic.exe", "QQMusic", LyricsSourceCatalog.QQMusic),
        new("kugou", "Kugou", LyricsSourceCatalog.Kugou),
        new("汽水音乐", "SodaMusic", LyricsSourceCatalog.SodaMusic),
    ];

    /// <summary>
    /// 把请求携带的来源应用标识映射成默认取词来源 id：用户绑定表先于内置表，移除标记压制内置绑定。
    /// Maps the request's source-application identifier onto the default lyric-source id: the user's table runs before the
    /// built-in one, and a removal marker suppresses the built-in binding.
    /// </summary>
    /// <param name="sourceAppId">SMTC 的 SourceAppUserModelId 原文，未知为 null / The raw SMTC SourceAppUserModelId, null when unknown.</param>
    /// <param name="netEaseSongId">网易云歌曲 id；非空说明走网易直连路径，按 id 精确取词 / NetEase song id; non-null means the NetEase direct path, which retrieves by id.</param>
    /// <param name="bindings">用户绑定表；null 表示未配置，全部走内置映射 / The user's binding table; null means unconfigured, which follows the built-in mapping.</param>
    /// <returns>默认来源 id；识别不出或被用户移除时为 null / The default source id, or null when unrecognized or removed by the user.</returns>
    public static string? MapDefaultSource(
        string? sourceAppId,
        string? netEaseSongId,
        LyricsDefaultBindingSettings? bindings = null)
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

        // 用户绑定表一旦存在（非 null）就是唯一权威：命中给出来源（或移除），未命中的播放器没有默认接口。
        // 内置表只在"从未配置"时生效——它只是设置文件尚未生成时的预填，不是与用户条目并行的另一层。
        // Once the user's table exists (non-null) it is the only authority: a hit supplies the source (or a removal), and
        // players without a hit have no default interface. The built-in table applies only when never configured — it is
        // the prefill for a settings file that does not exist yet, not a layer beside the user's entries.
        if (bindings?.Bindings is not null)
        {
            foreach (var entry in bindings.Bindings)
            {
                if (!sourceAppId.Contains(entry.AppId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return LyricsSourceCatalog.IsKnown(entry.SourceId) ? entry.SourceId : null;
            }

            return null;
        }

        // 内置表（仅未配置时）。 / The built-in table (only while never configured).
        foreach (var binding in BuiltInBindings)
        {
            if (sourceAppId.Contains(binding.AppId, StringComparison.OrdinalIgnoreCase))
            {
                return binding.DefaultSourceId;
            }
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
