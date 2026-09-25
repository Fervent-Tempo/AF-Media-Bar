namespace AFMediaBar.Classes.Settings;

/// <summary>
/// 取词的查询策略：按序逐个尝试来源，或并发派发。
/// The retrieval query strategy: try the sources one by one, or dispatch them concurrently.
/// </summary>
public enum LyricsQueryStrategy
{
    /// <summary>按序查询：按备用来源的优先级顺序逐个尝试，前一个未命中或超时才轮到下一个（传统串行链）。
    /// Sequential: sources are tried one by one in the fallback priority order; the next one runs only after the previous
    /// misses or times out (the classic serial chain).</summary>
    Sequential = 0,

    /// <summary>并发查询：默认接口与优先级批次并发派发，由采纳策略决定用哪个结果。
    /// Concurrent: the default interface and the priority batches run together, and the adoption mode picks the result.</summary>
    Concurrent = 1
}

/// <summary>
/// 并发取词的结果采纳策略：多个请求同时飞行时，链路按这里选定的规则决定用哪一个结果。
/// Adoption mode for concurrent retrieval: while several requests are in flight, the chain picks the result to use by
/// the rule chosen here.
/// </summary>
public enum LyricsAdoptionMode
{
    /// <summary>先到先得：任一来源先返回非空结果即采纳，其余请求全部放弃。
    /// First arrival: the first non-null result from any source is adopted and every other request is dropped.</summary>
    FirstArrival = 0,

    /// <summary>偏心默认来源：AppID 匹配的默认接口命中随时取代优先级批次里先到的候补结果；默认接口未命中时候补转正。
    /// 候补出现后默认接口一直等到它自己的单源预算为止。
    /// Prefer the default source: a hit from the AppID-matched default interface replaces the earliest candidate from the
    /// priority batches at any time, while the candidate wins when the default misses. Once a candidate exists the default
    /// interface is waited for until its own per-source budget runs out.</summary>
    PreferDefaultSource = 1,

    /// <summary>偏心默认来源（限时）：在偏心默认的基础上，候补出现后给默认接口一个倒计时；倒计时内没有命中就候补转正，
    /// 不让慢的默认接口拖住整链。
    /// Prefer the default source with a deadline: on top of preferring the default, a countdown starts once a candidate
    /// exists; without a hit inside the countdown the candidate wins, so a slow default interface cannot stall the chain.</summary>
    PreferDefaultSourceWithDeadline = 2
}

/// <summary>
/// 并发取词的权威取值区间与默认值：批次大小与默认接口倒计时的设置项都绑定这里，不在别处重复字面量。
/// Authoritative ranges and defaults of the concurrency retrieval: the batch-size and default-interface-deadline settings
/// bind here and no literal is repeated elsewhere.
/// </summary>
public static class LyricsConcurrencyDefaults
{
    /// <summary>并发批次的默认大小：默认接口之外，优先级列表每次并发发出的来源个数。/ Default concurrency batch size: besides the default interface, how many priority sources are dispatched per batch.</summary>
    public const int BatchSizeDefault = 3;

    /// <summary>批次大小的最小值。/ Minimum batch size.</summary>
    public const int BatchSizeMinimum = 1;

    /// <summary>批次大小的最大值：来源总数目前为 6，超过它只会让所有来源挤进同一批，失去分批的意义。
    /// Maximum batch size: with six sources at present a larger value only packs every source into one batch, defeating batching.</summary>
    public const int BatchSizeMaximum = 6;

    /// <summary>默认接口倒计时的默认值（毫秒）：候补结果出现后，默认接口还有这么长时间来完成命中。
    /// Default deadline (milliseconds) of the default interface: after a candidate appears, the default interface has this long to land a hit.</summary>
    public const int AdoptionDeadlineMillisecondsDefault = 2000;

    /// <summary>倒计时的最小值（毫秒）：低于它倒计时失去等待的意义。/ Minimum deadline (milliseconds): below it the countdown stops being a wait.</summary>
    public const int AdoptionDeadlineMillisecondsMinimum = 500;

    /// <summary>倒计时的最大值（毫秒）：再长就接近默认接口自己的单源预算，限时失去意义。
    /// Maximum deadline (milliseconds): beyond it the countdown approaches the default interface's own per-source budget and the limit means nothing.</summary>
    public const int AdoptionDeadlineMillisecondsMaximum = 10000;

    /// <summary>把任意输入夹进批次大小区间。/ Clamps any input into the batch-size range.</summary>
    public static int NormalizeBatchSize(int batchSize) => Math.Clamp(batchSize, BatchSizeMinimum, BatchSizeMaximum);

    /// <summary>把任意输入夹进倒计时区间。/ Clamps any input into the deadline range.</summary>
    public static int NormalizeAdoptionDeadlineMilliseconds(int milliseconds) =>
        Math.Clamp(milliseconds, AdoptionDeadlineMillisecondsMinimum, AdoptionDeadlineMillisecondsMaximum);
}

/// <summary>
/// 搜索型歌词来源的匹配严格度：搜索结果必须达到的最低匹配等级。
/// Match strictness for search-based lyric sources: the minimum match level a search result has to reach.
///
/// 三档分别对应库的 <c>CompareHelper.MatchType</c>：均衡 = High、严格 = VeryHigh、精确 = Perfect。三档都要求曲名与歌手
/// 匹配，区别只在"有多难通过"：均衡用来救回更多冷门曲目，精确用来避免同名现场版、翻唱或纯伴奏串词。
/// The three levels map onto the library's <c>CompareHelper.MatchType</c>: balanced = High, strict = VeryHigh, exact = Perfect.
/// All three require a title and artist match; they differ only in how hard they are to pass — balanced rescues more obscure
/// tracks, exact keeps a same-named live version, cover, or instrumental from supplying the wrong lyrics.
/// </summary>
public enum LyricsMatchStrictness
{
    /// <summary>均衡：匹配等级 High，默认档。/ Balanced: match level High, the default.</summary>
    Balanced = 0,

    /// <summary>严格：匹配等级 VeryHigh。/ Strict: match level VeryHigh.</summary>
    Strict = 1,

    /// <summary>精确：匹配等级 Perfect。/ Exact: match level Perfect.</summary>
    Exact = 2
}

/// <summary>
/// 未唱部分不透明度的权威取值区间、步长与默认值：界面滑杆与数值读出都绑定这里，不在 XAML 里另写字面量。
/// The authoritative range, step, and default of the unsung-part opacity: the slider and its readout bind to these and no
/// literal is repeated in XAML.
///
/// 该值就是启用逐字擦亮时底色层（未唱部分）的不透明度：值越小对比越强（20%），越大越接近不擦亮（80%）。
/// 上限刻意不到 100%：两层同色时擦亮不再可见，设置到那个位置只会让人以为功能坏了。
/// The value is the opacity of the base layer (the unsung part) while syllable highlighting is on: smaller means a stronger
/// contrast (20%) and larger approaches "no reveal at all" (80%). The ceiling deliberately stops short of 100%, where both
/// layers share one colour and the reveal disappears, which would only look like a broken feature.
/// </summary>
public static class LyricsUnsungOpacity
{
    /// <summary>不透明度的最小值（百分比）。/ Minimum opacity in percent.</summary>
    public const int MinimumPercent = 20;

    /// <summary>不透明度的最大值（百分比）。/ Maximum opacity in percent.</summary>
    public const int MaximumPercent = 80;

    /// <summary>不透明度的步长（百分比）。/ Opacity step in percent.</summary>
    public const int StepPercent = 5;

    /// <summary>不透明度的默认值（百分比）：与升级前的 0.45 一致，因此默认外观不变。
    /// Default opacity in percent, matching the 0.45 used before this setting existed so the default look is unchanged.</summary>
    public const int DefaultPercent = 45;

    /// <summary>
    /// 把任意输入吸附到步长网格并夹进区间。
    /// Snaps any input onto the step grid and clamps it into range.
    /// </summary>
    /// <param name="percent">原始百分比 / Raw percentage.</param>
    /// <returns>可直接写入设置的百分比 / The percentage that can be written into the settings.</returns>
    public static int Normalize(int percent) => Math.Clamp(
        (int)Math.Round(percent / (double)StepPercent, MidpointRounding.AwayFromZero) * StepPercent,
        MinimumPercent,
        MaximumPercent);
}

/// <summary>
/// 歌词字距：按字号的百分比拉开字与字之间的空隙。
///
/// Web 歌词视图用 CSS 的 <c>letter-spacing</c> 实现（值为 <c>百分比 / 100</c> 个 em），因此空隙随字号等比缩放、
/// 连续无级；中文与英文都按字母拉开，但不会拆断单词。0 表示不改动字距，默认外观与升级前逐像素一致。
/// Lyric character spacing: extra space between characters as a percentage of the font size.
///
/// The web lyrics view implements it with CSS <c>letter-spacing</c> (the value is <c>percent / 100</c> em), so the gap scales
/// with the font size and is continuously adjustable; CJK and Latin both spread by letter, and no word is broken apart. Zero
/// leaves the spacing untouched, keeping the default look pixel-identical to the version before this setting existed.
/// </summary>
public static class LyricsCharacterSpacing
{
    /// <summary>字距的持久化安全下限（百分比）。/ Persistence-safe lower bound in percent.</summary>
    public const int MinimumPercent = 0;

    /// <summary>字距的持久化安全上限（百分比）：20% 字号在 13 px 字号下约 2.6 px，已是任务栏上的可读上限。
    /// Persistence-safe upper bound in percent: 20% of a 13 px font is about 2.6 px, already the readability ceiling on a taskbar.</summary>
    public const int MaximumPercent = 20;

    /// <summary>字距的步长（百分比）：CSS 排版是亚像素的，1% 一档也能看清变化。
    /// Character-spacing step in percent: CSS layout is sub-pixel, so a one-percent step is still visible.</summary>
    public const int StepPercent = 1;

    /// <summary>字距的默认值：0 表示不改动字距，与升级前一致。/ Default spacing: zero, matching the look before this setting existed.</summary>
    public const int DefaultPercent = 0;

    /// <summary>
    /// 把任意输入吸附到步长网格并夹进区间。
    /// Snaps any input onto the step grid and clamps it into range.
    /// </summary>
    /// <param name="percent">原始百分比 / Raw percentage.</param>
    /// <returns>可直接写入设置的百分比 / The percentage that can be written into the settings.</returns>
    public static int Normalize(int percent) => Math.Clamp(
        (int)Math.Round(percent / (double)StepPercent, MidpointRounding.AwayFromZero) * StepPercent,
        MinimumPercent,
        MaximumPercent);
}

/// <summary>
/// 双行歌词的行距：在现有两行布局之上**额外**增加的间距，按字号的百分比表示。
///
/// 0 表示与升级前的观感逐像素一致（两行各占文字区一半并居中）；调大时两行等距分开，间距随字号等比缩放，
/// 因此不同厚度/字号下的观感一致。运行时按文字区的可用高度自动夹取，且滑杆全程有效：即使调到上限也不会
/// 裁切文字或缩小字号（见 Web 歌词视图的 <c>resolveRowMetrics</c>）。
/// Line gap of two-line lyrics: **extra** spacing added on top of the existing two-row layout, expressed as a percentage of the
/// font size.
///
/// Zero keeps the look of the version before this setting existed pixel-identical (each line centred in one half of the
/// media-text area); increasing it moves both lines apart by the configured proportion, which scales with the font size so the
/// look stays consistent across thickness and font-size settings. It is clamped at runtime against the available height, and the
/// slider stays effective end to end: even at its maximum the text is never clipped or shrunk (see <c>resolveRowMetrics</c> in
/// the web lyrics view).
/// </summary>
public static class LyricsLineGap
{
    /// <summary>行距的持久化安全下限：0 表示不额外增加间距。/ Persistence-safe lower bound of the line gap; zero adds nothing.</summary>
    public const int MinimumPercent = 0;

    /// <summary>
    /// 行距的持久化安全上限（百分比）：实测"不裁切、不缩小字号"的可用上限在标准厚度下约 30%、最薄的预设下约 25%，
    /// 取 24% 使得任何厚度下每一档都有真实变化，滑杆不会出现调到顶也没有效果的空档。
    /// Persistence-safe upper bound in percent: the measured ceiling that neither clips text nor shrinks the font is about 30% of
    /// the font size at standard thickness and about 25% at the thinnest preset, so 24% keeps every slider step effective at any
    /// thickness and no step goes to waste.
    /// </summary>
    public const int MaximumPercent = 24;

    /// <summary>行距的步长（百分比）：CSS 排版是亚像素的，2% 一档也能看清变化。
    /// Line-gap step in percent: CSS layout is sub-pixel, so a two-percent step is still visible.</summary>
    public const int StepPercent = 2;

    /// <summary>行距的默认值：0 表示不额外增加间距，默认外观与升级前一致。
    /// Default line gap: zero adds nothing, keeping the default look unchanged.</summary>
    public const int DefaultPercent = 0;

    /// <summary>
    /// 把任意输入吸附到步长网格并夹进区间。
    /// Snaps any input onto the step grid and clamps it into range.
    /// </summary>
    /// <param name="percent">原始百分比 / Raw percentage.</param>
    /// <returns>可直接写入设置的百分比 / The percentage that can be written into the settings.</returns>
    public static int Normalize(int percent) => Math.Clamp(
        (int)Math.Round(percent / (double)StepPercent, MidpointRounding.AwayFromZero) * StepPercent,
        MinimumPercent,
        MaximumPercent);
}

/// <summary>
/// 歌词框的固定长度：开启后媒体文字区（歌词框）保持固定长度，不再随每句歌词伸缩。
///
/// 关闭（默认）时歌词框随当前歌词自动伸缩；开启后长度由用户给定（DIP），任务栏空间不足时按可用长度夹取。
/// 固定长度可以彻底避免换行瞬间"文字先出现、媒体栏稍后才变长"的短暂截断，也让每句歌词的对齐位置保持稳定。
/// Fixed width of the lyric box: while enabled, the media-text area (the lyric box) keeps a fixed length instead of resizing with every line.
///
/// Disabled (the default) lets the box follow the current lyric; enabled, the length comes from the user in DIP and is clamped by the
/// available taskbar room. A fixed box removes the brief clip between a line change and the bar catching up, and keeps every line's
/// alignment stable.
/// </summary>
public static class LyricsFixedWidth
{
    /// <summary>固定长度的下限（DIP）：比最短的歌词行略宽，保证开启后仍有可读空间。
    /// Lower bound in DIP, a little wider than the shortest lyric line so the box stays readable.</summary>
    public const int MinimumDip = 80;

    /// <summary>固定长度的上限（DIP）：任务栏空间不足时运行时还会按可用长度夹取。
    /// Upper bound in DIP; the runtime clamps it further to the available taskbar room.</summary>
    public const int MaximumDip = 600;

    /// <summary>固定长度的步长（DIP）。/ Fixed-width step in DIP.</summary>
    public const int StepDip = 10;

    /// <summary>固定长度的默认值（DIP）：容纳常见短句，也不至于占满任务栏。
    /// Default fixed width in DIP: roomy enough for common short lines without filling the taskbar.</summary>
    public const int DefaultDip = 240;

    /// <summary>
    /// 把任意输入吸附到步长网格并夹进区间。
    /// Snaps any input onto the step grid and clamps it into range.
    /// </summary>
    /// <param name="dip">原始 DIP / Raw DIP.</param>
    /// <returns>可直接写入设置的 DIP / The DIP value that can be written into the settings.</returns>
    public static int Normalize(int dip) => Math.Clamp(
        (int)Math.Round(dip / (double)StepDip, MidpointRounding.AwayFromZero) * StepDip,
        MinimumDip,
        MaximumDip);
}

/// <summary>
/// 第二行歌词的来源顺序：列表顺序就是优先级，未列出的来源不会被使用。
/// The second lyric line's source order: the list order is the priority and a source missing from the list is never used.
///
/// <c>null</c> 表示从未配置过，此时按 <c>LyricsSecondaryLinePolicy.DefaultOrder</c>（下一句 → 翻译 → 音译）；
/// 非空数组按给定顺序只用这些来源。与来源列表一样，归一化不去白名单，因此以后新增的来源不会让旧设置文件失效。
/// <c>null</c> was never configured, which follows <c>LyricsSecondaryLinePolicy.DefaultOrder</c> (next line, translation, romanization);
/// a non-empty array uses exactly those sources in that order. As with the source list, normalization keeps no allow-list, so a source added
/// later never invalidates an old settings file.
/// </summary>
/// <param name="Order">按优先级排列的第二行来源；null 表示未配置，即使用默认顺序 / Second-line sources in priority order; null means unconfigured, which uses the default order.</param>
public readonly record struct LyricsSecondaryLineSettings(IReadOnlyList<LyricsSecondaryLineMode>? Order)
{
    /// <summary>默认值：从未配置，即按默认顺序（下一句 → 翻译 → 音译）。/ The default: never configured, meaning the default order (next line, translation, romanization).</summary>
    public static LyricsSecondaryLineSettings Default { get; } = new(null);

    /// <summary>
    /// 去掉重复项与非法枚举值，保留用户给定的顺序，并保持"未配置"与"显式顺序"的区别。
    /// Drops duplicates and undefined enum values while keeping the user's order and preserving the difference between "never configured" and an
    /// explicit order.
    /// </summary>
    public LyricsSecondaryLineSettings Normalize() => this with
    {
        Order = Order is null
            ? null
            : Order
                .Where(static mode => Enum.IsDefined(mode))
                .Distinct()
                .ToArray()
    };
}

/// <summary>
/// 一条默认取词接口绑定：播放器标识（AUMID 或它包含的片段）→ 来源 id。
/// One default-lyrics binding: a player identifier (an AUMID or a fragment it contains) mapped onto a source id.
///
/// <paramref name="SourceId"/> 为 null 表示该播放器**没有默认接口**（删除或暂不绑定）：绑定表一旦非 null 就是唯一权威，
/// 内置映射不再参与；"未配置"（整个绑定表为 null）与"用户明确不要默认接口"是两种不同的意图，必须分别保存。
/// A null <paramref name="SourceId"/> means the player has **no default interface** (deleted or not bound): a non-null
/// table is the only authority and the built-in mapping no longer applies; "never configured" (the whole table is null) and
/// "the user does not want a default here" are two different intents and have to be stored differently.
/// </summary>
/// <param name="AppId">播放器标识，匹配规则为"来源标识包含此片段"；创建后不可改，改 = 删除重加 / The player identifier, matched as "the source id contains this fragment"; immutable once created — changing it means delete and re-add.</param>
/// <param name="SourceId">绑定的来源 id；null 表示该播放器没有默认接口 / The bound source id, or null when the player has no default interface.</param>
/// <param name="Remark">界面上显示的备注名；null 时回落到内置播放器名或 AppID 原文 / The remark shown on the interface; null falls back to the built-in player's name or the raw AppID.</param>
public sealed record LyricsDefaultBinding(string AppId, string? SourceId, string? Remark = null)
{
    /// <summary>该条目是否是"没有默认接口"的标记。/ Whether this entry marks "no default interface".</summary>
    public bool IsRemoval => SourceId is null;
}

/// <summary>
/// 默认取词接口的用户绑定表，先于内置映射生效。
/// The user's default-interface binding table, applied before the built-in mapping.
///
/// 三种取值含义不同：null 表示从未配置，全部播放器按内置映射；非空数组按条目匹配播放器，
/// 命中的条目给出来源（或"移除"），未命中的播放器继续走内置映射。归一化不做来源白名单
/// （设置层不认识提供器），未知来源 id 在使用时按"无默认接口"处理，因此删除一个来源不会让旧设置失效。
/// The three values mean different things: null was never configured, so every player follows the built-in mapping; a non-empty
/// array matches players by entry, a hit supplies the source (or a removal), and players without a hit keep the built-in mapping.
/// Normalization keeps no source allow-list (the settings layer knows nothing about providers): an unknown source id counts as
/// "no default interface" when used, so removing a source never invalidates an old settings file.
/// </summary>
/// <param name="Bindings">绑定条目；null 表示未配置，即全部走内置映射 / The binding entries; null means unconfigured, which follows the built-in mapping.</param>
public sealed record LyricsDefaultBindingSettings(IReadOnlyList<LyricsDefaultBinding>? Bindings)
{
    /// <summary>默认值：从未配置，全部播放器按内置映射。/ The default: never configured, every player follows the built-in mapping.</summary>
    public static LyricsDefaultBindingSettings Default { get; } =
        new((IReadOnlyList<LyricsDefaultBinding>?)null);

    /// <summary>
    /// 去掉空白条目与重复的播放器标识（保留先出现的），并保持"未配置"与"显式绑定"的区别。
    /// Drops blank entries and duplicate player identifiers (keeping the first), preserving the difference between
    /// "never configured" and an explicit table.
    /// </summary>
    public LyricsDefaultBindingSettings Normalize() => this with
    {
        Bindings = Bindings is null
            ? null
            : Bindings
                .Where(static binding => binding is not null && !string.IsNullOrWhiteSpace(binding.AppId))
                .Select(static binding => new LyricsDefaultBinding(
                    binding.AppId.Trim(),
                    string.IsNullOrWhiteSpace(binding.SourceId) ? null : binding.SourceId,
                    string.IsNullOrWhiteSpace(binding.Remark) ? null : binding.Remark.Trim()))
                .Distinct(new AppIdComparer())
                .ToArray()
    };

    private sealed class AppIdComparer : IEqualityComparer<LyricsDefaultBinding>
    {
        public bool Equals(LyricsDefaultBinding? left, LyricsDefaultBinding? right) =>
            string.Equals(left?.AppId, right?.AppId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(LyricsDefaultBinding binding) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(binding.AppId);
    }
}

/// <summary>
/// 歌词来源偏好：<paramref name="EnabledSourceIds"/> 的顺序就是优先级。
/// Lyric-source preference: the order of <paramref name="EnabledSourceIds"/> is the priority.
///
/// 三种取值含义不同，不能混为一谈：
/// - <c>null</c>：从未配置过 → 全部来源按 <c>LyricsSourceCatalog.DefaultOrder</c>，因此旧设置文件与新增来源都会自动生效；
/// - 空数组：用户明确关掉了全部来源 → 不再请求任何歌词服务（这是"我不想联网取词"的表达方式）；
/// - 非空数组：按给定顺序只用这些来源。
/// 未收录的来源 id 会一直留在设置里（归一化不做白名单，因为设置层不认识提供器），使用时由 <c>LyricsSourcePolicy</c> 忽略，
/// 因此删掉一个来源不会让旧设置文件失效。
/// The three values mean different things and must not be conflated: <c>null</c> was never configured, so every source follows
/// <c>LyricsSourceCatalog.DefaultOrder</c> and an old settings file as well as a newly added source both keep working; an empty
/// array is the user explicitly turning every source off, which means no lyric service is contacted at all and is how "I do not
/// want lyric lookups over the network" is expressed; a non-empty array uses exactly those sources in that order. An id that is no
/// longer recognized stays in the settings (normalization keeps no allow-list, because the settings layer knows nothing about
/// providers) and is ignored when used, so removing a source never invalidates an old file.
/// </summary>
/// <param name="EnabledSourceIds">启用来源的有序 id 列表；null 表示全部来源，空数组表示全部关闭 / Ordered ids of the enabled sources; null means all of them, an empty array means none.</param>
public readonly record struct LyricsSourceSettings(IReadOnlyList<string>? EnabledSourceIds)
{
    /// <summary>默认值：从未配置，即全部来源按默认顺序。/ The default: never configured, meaning every source in the default order.</summary>
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
