namespace AFMediaBar.Classes.Settings;

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
/// 「长歌词动态滚动」的宽度封顶：跟随内容模式下，逐字歌词超过封顶宽度时按封顶宽度呈现，
/// 由 Web 层既有的横向滚动机制保证当前演唱内容可见，媒体栏不再被超长歌词撑开。
/// Width cap for the long-lyric auto-scroll: in follow-content mode a syllable-timed line wider than the cap is
/// presented at the capped width, and the web layer's existing horizontal scroll keeps the sung part visible, so an
/// over-long line no longer stretches the media bar.
/// </summary>
public static class LyricsLongLineScroll
{
    /// <summary>
    /// 封顶宽度按全角字个数折算：全角字符宽约等于一个 em，因此封顶值随当前有效字号等比缩放，
    /// 不同字号下可见字数稳定，也不依赖固定字符数阈值。
    /// The cap is expressed in full-width characters: a full-width glyph is about one em wide, so the cap scales
    /// with the active font size, keeps the visible character count stable, and needs no fixed character threshold.
    /// </summary>
    public const int MaximumFullWidthCharacters = 16;

    /// <summary>
    /// 按当前有效歌词字号换算封顶宽度（DIP）。
    /// Converts the cap into DIP against the active effective lyric font size.
    /// </summary>
    public static double ResolveMaximumDip(double effectiveFontSizeDip) =>
        MaximumFullWidthCharacters * Math.Max(0, effectiveFontSizeDip);

    /// <summary>
    /// 跟随内容模式的歌词宽度：开启封顶且当前行有逐字时间轴时取封顶值，否则原样返回。
    /// 行级歌词没有逐字时间轴（Web 层无法滚动），因此不封顶，维持原有行为。
    /// Resolves the lyric width in follow-content mode: capped while the feature is on and the active line carries a
    /// syllable timeline, returned unchanged otherwise. Line-level lyrics cannot scroll in the web layer, so they are
    /// never capped and keep their existing behavior.
    /// </summary>
    public static double ResolveFollowContentWidth(
        double measuredWidthDip,
        bool enabled,
        bool hasSyllableTimeline,
        double effectiveFontSizeDip) =>
        enabled && hasSyllableTimeline
            ? Math.Min(measuredWidthDip, ResolveMaximumDip(effectiveFontSizeDip))
            : measuredWidthDip;

    /// <summary>
    /// 跟随内容模式下主行与次行（译文或下一句）的合成宽度：封顶只作用于主行，次行保持自然宽度，取两者较大值。
    /// 主行是唯一带逐字时间轴、能随演唱进度在 Web 层横向滚动的行；次行没有逐字时间轴也不会随进度滚动，
    /// 封顶只会把它截断（省略号裁掉右侧），因此它保持自然宽度——次行更长时媒体栏跟随次行。
    /// Combines the primary and secondary (translation or next line) widths in follow-content mode: the cap applies to
    /// the primary line only, the secondary keeps its natural width, and the larger of the two wins. The primary is the
    /// only line with a syllable timeline that can scroll horizontally with the singing progress; the secondary has no
    /// timeline and never scrolls, so capping it would only clip it — when the secondary is longer the bar follows it.
    /// </summary>
    public static double ResolveFollowContentWidthPair(
        double primaryWidthDip,
        double secondaryWidthDip,
        bool enabled,
        bool hasSyllableTimeline,
        double effectiveFontSizeDip) =>
        Math.Max(
            ResolveFollowContentWidth(primaryWidthDip, enabled, hasSyllableTimeline, effectiveFontSizeDip),
            secondaryWidthDip);
}

/// <summary>
/// 第二行歌词的来源顺序：列表顺序就是优先级，未列出的来源不会被使用。
/// The second lyric line's source order: the list order is the priority and a source missing from the list is never used.
///
/// <c>null</c> 表示从未配置过，此时按 <c>LyricsSecondaryLinePolicy.DefaultOrder</c>（下一句 → 翻译 → 音译）；
/// 非空数组按给定顺序只用这些来源。与来源列表一样，归一化不去白名单，因此以后新增的来源不会让旧设置文件失效。
/// <c>null</c> was never configured, which follows <c>LyricsSecondaryLinePolicy.DefaultOrder</c> (next line, translation, romanization);
/// a non-empty array enables exactly those sources. As with the source list, normalization keeps no allow-list, so a source added
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


/// <summary>歌词文字对齐方式。 / Lyric text alignment.</summary>
public enum LyricsTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}
