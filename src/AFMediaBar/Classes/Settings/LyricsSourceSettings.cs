// 歌词源启用集合的数据契约与归一化；不依赖界面设置、服务或外部资源。
namespace AFMediaBar.Classes.Settings;

/// <summary>歌词来源仲裁规则。</summary>
public enum LyricsSourceRule
{
    Default = 0,
    Manual = 1
}

/// <summary>手动规则下来源请求的启动方式。</summary>
public enum LyricsSourceAttemptMode
{
    Sequential = 0,
    Parallel = 1
}

/// <summary>手动规则下有效歌词结果的采纳方式。</summary>
public enum LyricsSourceResultMode
{
    Ordered = 0,
    FirstAvailable = 1
}

/// <summary>
/// 歌词来源开关及规则；启用集合与手动优先级分别保存。
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
    public LyricsSourceRule Rule { get; init; } = LyricsSourceRule.Default;
    public LyricsSourceAttemptMode AttemptMode { get; init; } = LyricsSourceAttemptMode.Sequential;
    public LyricsSourceResultMode ResultMode { get; init; } = LyricsSourceResultMode.Ordered;
    /// <summary>手动来源顺序，null 使用目录顺序；关闭来源也保留其位置。</summary>
    public IReadOnlyList<string>? PrioritySourceIds { get; init; }

    /// <summary>默认值：从未配置，即全部来源启用。</summary>
    public static LyricsSourceSettings Default { get; } = new(null);

    /// <summary>
    /// 去掉空白项与重复项，保留用户给定的顺序，并保持"未配置"与"全部关闭"的区别。
    /// Drops blank and duplicate entries while keeping the user's order and preserving the difference between "never configured"
    /// and "everything turned off".
    /// </summary>
    public LyricsSourceSettings Normalize() => this with
    {
        Rule = Enum.IsDefined(Rule) ? Rule : LyricsSourceRule.Default,
        AttemptMode = Enum.IsDefined(AttemptMode) ? AttemptMode : LyricsSourceAttemptMode.Sequential,
        ResultMode = Enum.IsDefined(ResultMode) ? ResultMode : LyricsSourceResultMode.Ordered,
        EnabledSourceIds = NormalizeIds(EnabledSourceIds),
        PrioritySourceIds = NormalizeIds(PrioritySourceIds)
    };

    private static string[]? NormalizeIds(IReadOnlyList<string>? ids) => ids?
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
