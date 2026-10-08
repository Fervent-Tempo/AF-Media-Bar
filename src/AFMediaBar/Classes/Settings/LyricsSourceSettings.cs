// 歌词源启用集合的数据契约与归一化；不依赖界面设置、服务或外部资源。
namespace AFMediaBar.Classes.Settings;

/// <summary>
/// 歌词来源开关：列表仅决定启用集合，查询顺序由固定策略拥有。
/// Lyric-source selection; retrieval order belongs to the fixed policy.
///
/// 三种取值含义不同，不能混为一谈：
/// - <c>null</c>：从未配置过 → 全部来源启用，因此旧设置文件与新增来源都会自动生效；
/// - 空数组：用户明确关掉了全部来源 → 不再请求任何歌词服务（这是"我不想联网取词"的表达方式）；
/// - 非空数组：只启用这些来源。
/// 未收录的来源 id 会一直留在设置里（归一化不做白名单，因为设置层不认识提供器），使用时由 <c>LyricsSourcePolicy</c> 忽略，
/// 因此删掉一个来源不会让旧设置文件失效。
/// The three values mean different things and must not be conflated: <c>null</c> was never configured, so every source is enabled and an old settings file as well as a newly added source both keep working; an empty
/// array is the user explicitly turning every source off, which means no lyric service is contacted at all and is how "I do not
/// want lyric lookups over the network" is expressed; a non-empty array enables exactly those sources. An id that is no
/// longer recognized stays in the settings (normalization keeps no allow-list, because the settings layer knows nothing about
/// providers) and is ignored when used, so removing a source never invalidates an old file.
/// </summary>
/// <param name="EnabledSourceIds">启用来源的有序 id 列表；null 表示全部来源，空数组表示全部关闭 / Ordered ids of the enabled sources; null means all of them, an empty array means none.</param>
public readonly record struct LyricsSourceSettings(IReadOnlyList<string>? EnabledSourceIds)
{
    /// <summary>默认值：从未配置，即全部来源启用。</summary>
    public static LyricsSourceSettings Default { get; } = new(null);

    /// <summary>
    /// 去掉空白项与重复项，保留用户给定的顺序，并保持"未配置"与"全部关闭"的区别。
    /// Drops blank and duplicate entries while keeping the user's order and preserving the difference between "never configured"
    /// and "everything turned off".
    /// </summary>
    public LyricsSourceSettings Normalize() => this with
    {
        EnabledSourceIds = EnabledSourceIds is null
            ? null
            : EnabledSourceIds
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
    };
}
