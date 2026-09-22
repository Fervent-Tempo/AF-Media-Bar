using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊歌词槽的**跟随式**滚动：只有一份文本，窗口起点由"亮区已经有多宽"反推，亮区因此永远停在文字槽右缘附近。
/// The capsule lyric slot's **follow** scroll: a single copy of the text whose window start is solved backwards from how wide the reveal has
/// already become, which keeps the reveal resting near the right edge of the text slot.
///
/// 为什么不能直接调用任务栏那套：<c>TaskBarMediaControl.Animation.cs</c> 的 <c>EnsurePrefixWidths</c> / <c>AdvanceFollow</c> /
/// <c>WriteMarqueeWindow</c> 都是 <c>private static</c>，岛无法复用。这里把**同一条算式**搬成可测的纯函数：量宽用注入的
/// <c>Func&lt;string, double&gt;</c>，测试里喂假字宽，因此这一层既不需要 WPF 也能被 MSTest 钉住。
/// Why the taskbar's implementation cannot be called: <c>EnsurePrefixWidths</c>, <c>AdvanceFollow</c>, and <c>WriteMarqueeWindow</c> in
/// <c>TaskBarMediaControl.Animation.cs</c> are all <c>private static</c>, so the island cannot reuse them. This moves **the same arithmetic**
/// into testable pure functions, with the width measurement injected as a <c>Func&lt;string, double&gt;</c> so a test can feed it fake advances
/// and pin the behaviour without WPF.
///
/// 与胶囊**歌名**那份旋转式（两份文本 + 循环位移）语义完全不同，两者绝不混用：跟随式永不回绕，窗口永远是原文的后缀，
/// 已唱段因此始终是窗口里的**前缀**，裁剪矩形才能表达它（任务书 §4.3）。
/// This is a different mode from the capsule **title**'s rotation (two copies plus a cyclic shift) and the two are never mixed: the follow mode
/// never wraps, its window is always a suffix of the content, and the sung run therefore stays a **prefix** of the window, which is the only
/// shape a clip rectangle can express (task book §4.3).
/// </summary>
public static class CapsuleLyricFollowPolicy
{
    /// <summary>
    /// 一行文本的前缀宽度表：第 i 项是前 i 个字符的宽度。
    /// The prefix-width table of one line: the i-th entry is the width of the first i characters.
    ///
    /// 表按需增长（表随演唱向前长，均摊每唱一个字量一次），并记着它属于哪一行文本**与哪一套字形**：
    /// 只比长度或只比文本都不够——换一行而长度恰好相同时旧字宽会被当成新行的字宽；而 <c>ApplyFontScales</c> 在 DPI 或显示器变化时
    /// 改掉字号之后，同一张表里会混进两套度量（字形宽度、位移与裁剪边界全部错位），直到换行才恢复。
    /// The table grows on demand (it follows the singing, amortised to one measurement per sung character) and remembers which line **and which set
    /// of glyphs** it belongs to: comparing lengths or text alone is not enough — a new line of exactly the same length would be measured with the old
    /// advances, and ApplyFontScales rewriting the font size on a DPI or monitor change would mix two sets of advances into one table (glyph widths,
    /// offsets, and clip edges all misaligned) until the next line change.
    /// </summary>
    /// <param name="Content">这张表对应的文本 / The text this table belongs to.</param>
    /// <param name="MeasureKey">字形键（字号|字重|字体族）；键一变整表作废 / The glyph key (size|weight|family); a change invalidates the whole table.</param>
    /// <param name="Widths">前缀宽度，长度 = 文本长度 + 1 / Prefix widths, of length text length plus one.</param>
    /// <param name="MeasuredCharacters">已经量过的字符数 / How many characters have been measured.</param>
    public readonly record struct PrefixWidthTable(string Content, string MeasureKey, double[] Widths, int MeasuredCharacters);

    /// <summary>
    /// 一帧的跟随结果。
    /// One frame of the follow scroll.
    /// </summary>
    /// <param name="Table">这一帧之后的前缀宽度表（已按需增长）/ The prefix-width table after this frame, grown on demand.</param>
    /// <param name="Window">要写进文字的窗口（原文的后缀）/ The window to write into the text block, a suffix of the content.</param>
    /// <param name="WindowStart">窗口起点（字符，已对齐到文本元素边界）/ The window start in characters, snapped to a text-element boundary.</param>
    /// <param name="OffsetDip">渲染位移（DIP，向左侧为负）/ The render offset in DIP, negative to the left.</param>
    /// <param name="ClipWidth">亮区的裁剪宽度（DIP，窗口自身的坐标系）/ The reveal's clip width in DIP, in the window's own coordinate space.</param>
    /// <param name="RevealWidth">呈现层使用的已唱宽度（只前进、每帧封顶）/ The presented sung width, forward-only and capped per frame.</param>
    public readonly record struct FollowFrame(
        PrefixWidthTable Table,
        string Window,
        int WindowStart,
        double OffsetDip,
        double ClipWidth,
        double RevealWidth);

    /// <summary>还没有量过任何前缀的空表。/ The empty table, with no prefix measured yet.</summary>
    public static PrefixWidthTable EmptyTable { get; } = new(string.Empty, string.Empty, [0], 0);

    /// <summary>
    /// 按需把前缀宽度表长到至少 <paramref name="requiredCharacters"/> 个字符。
    ///
    /// 量与任务栏同一口径：第 i 项量的是**前缀字符串本身**（<c>content[..i]</c>）的宽度，而不是逐字符宽度累加——
    /// 连字与字距调整会让两者不同，而亮区边界、位移与比例尺必须来自同一张表才对齐。
    /// Grows the prefix-width table to at least the requested number of characters.
    ///
    /// The measurement follows the taskbar's basis: entry i is the width of the **prefix string itself** (<c>content[..i]</c>) rather than a sum
    /// of per-character advances, because kerning and ligatures make the two differ, and the reveal edge, the offset, and the scale all have to
    /// come from one table to line up.
    /// </summary>
    /// <param name="table">上一帧的表；文本或字形键不同时整表重测。/ The previous frame's table, rebuilt entirely when the text or the glyph key differs.</param>
    /// <param name="content">这一行的完整文本。/ The line's whole text.</param>
    /// <param name="measureKey">字形键（字号|字重|字体族）：它变了整表重测，因为每个前缀的宽度都会跟着变。/ The glyph key (size|weight|family): a change re-measures everything, because every prefix's width changes with it.</param>
    /// <param name="requiredCharacters">本次至少需要的字符数（越界时夹到文本长度）。/ The characters required this time, clamped to the text length.</param>
    /// <param name="measureWidth">量宽函数（前缀字符串 → DIP），注入以便测试喂假字宽。/ The width function (prefix string to DIP), injected so tests can feed fake advances.</param>
    public static PrefixWidthTable EnsurePrefixWidths(
        PrefixWidthTable table,
        string content,
        string measureKey,
        int requiredCharacters,
        Func<string, double> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(measureWidth);

        measureKey ??= string.Empty;
        var widths = table.Widths;
        var measured = table.MeasuredCharacters;
        if (widths is null || widths.Length != content.Length + 1 ||
            !string.Equals(table.Content, content, StringComparison.Ordinal) ||
            !string.Equals(table.MeasureKey, measureKey, StringComparison.Ordinal))
        {
            widths = new double[content.Length + 1];
            measured = 0;
        }

        var target = Math.Clamp(requiredCharacters, measured, content.Length);
        for (var index = measured + 1; index <= target; index++)
        {
            widths[index] = measureWidth(content[..index]);
        }

        return new PrefixWidthTable(content, measureKey, widths, target);
    }

    /// <summary>
    /// 推进跟随式滚动一帧。算式与任务栏 <c>AdvanceFollow</c> 逐条相同：
    /// 已唱位置 → 前缀宽度 → 亮区宽度（只前进 + 每帧封顶）→ 需要丢弃的宽度 → 窗口位置 → 字符边界对齐 →
    /// 位移 = −(当前位置宽度 − 窗口左缘宽度)。
    ///
    /// **裁剪宽度用同一张表算出的 <c>已唱宽度 − 窗口左缘宽度</c>**，不是"进度 × 整行宽度"：后者在混排字宽下与真实字形宽度无关，
    /// 会让亮区停在错的地方（裁定 7）。位移与裁剪同口径还带来一个可验证的后果——窗口开始跟随后，
    /// <c>裁剪宽度 + 位移</c>（亮区右缘在文字槽里的位置）恒等于文字槽宽度的 <see cref="MarqueeFollowPolicy.RevealEdgeRatio"/>。
    /// Advances the follow scroll by one frame, item for item the same arithmetic the taskbar's <c>AdvanceFollow</c> uses: sung position,
    /// prefix width, reveal width (forward only, capped per frame), width to drop, window position, text-element snap, and finally
    /// offset = minus (width at the position minus width at the window's left edge).
    ///
    /// **The clip width comes from that same table as revealed minus window-left**, never from "progress times the whole line": the latter has
    /// nothing to do with the real glyph widths on mixed-advance content and parks the reveal in the wrong place (ruling 7). Sharing one basis
    /// also has a checkable consequence — once the window follows, the clip width plus the offset (the reveal's right edge inside the slot) is
    /// exactly <see cref="MarqueeFollowPolicy.RevealEdgeRatio"/> of the slot's width.
    /// </summary>
    /// <param name="content">这一行的完整文本。/ The line's whole text.</param>
    /// <param name="measureKey">字形键（字号|字重|字体族），随 <see cref="EnsurePrefixWidths"/> 一起决定表的有效性。/ The glyph key (size|weight|family), which decides the table's validity together with the text.</param>
    /// <param name="table">上一帧的前缀宽度表。/ The previous frame's prefix-width table.</param>
    /// <param name="previousRevealWidth">上一帧呈现的已唱宽度（0 表示这是一行的第一帧）。/ The revealed width presented last frame; zero means this is the line's first frame.</param>
    /// <param name="progress">擦亮进度（0–1，越界夹取）。/ The reveal progress, zero to one, clamped.</param>
    /// <param name="availableWidth">文字槽的实测可用宽度（DIP）；不可用时窗口停在开头。/ The text slot's measured available width in DIP; while unusable the window stays at the head.</param>
    /// <param name="fontSize">当前字号（DIP），每帧的追赶上限按它换算。/ The current font size in DIP, which converts the per-frame catch-up cap.</param>
    /// <param name="measureWidth">量宽函数（前缀字符串 → DIP）。/ The width function (prefix string to DIP).</param>
    public static FollowFrame Advance(
        string content,
        string measureKey,
        PrefixWidthTable table,
        double previousRevealWidth,
        double progress,
        double availableWidth,
        double fontSize,
        Func<string, double> measureWidth)
    {
        content ??= string.Empty;

        // 已唱位置按**字符数**从进度换算（与任务栏同一个入口），因此音节时间轴的加权结果直接落在这张表上。
        // The sung position converts from the progress by **character count** through the same entry the taskbar uses, so a syllable-weighted
        // timeline lands straight on this table.
        var sung = MarqueeFollowPolicy.ResolveSungPosition(progress, content.Length);

        // 只需要测到"已经唱到的那一个字"（再往前一个，因为插值会读到下一项）。
        // Only the characters sung so far have to be measured, one beyond the sung character because the interpolation reads the next entry.
        table = EnsurePrefixWidths(table, content, measureKey, (int)Math.Ceiling(sung) + 1, measureWidth);
        var widths = table.Widths;
        var measured = table.MeasuredCharacters;

        var rawWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, measured, sung);
        var revealWidth = MarqueeFollowPolicy.ResolveRevealWidth(
            previousRevealWidth,
            rawWidth,
            MarqueeFollowPolicy.MaximumRevealAdvanceEm * fontSize);

        var dropped = MarqueeFollowPolicy.ResolveDroppedWidth(revealWidth, availableWidth);
        var position = MarqueeFollowPolicy.ResolvePositionAtWidth(widths, measured, dropped);

        // 窗口起点对齐到文本元素边界：代理对与组合记号不会被拆到窗口两端（每个元素都可能一次跨过一个字符）。
        // The window start snaps to a text-element boundary so a surrogate pair or a combining mark is never split across the window's ends.
        var windowStart = MarqueeFollowPolicy.SnapStart(content, (int)Math.Floor(position), Math.Max(0, content.Length - 1));
        var windowLeftWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, measured, windowStart);

        return new FollowFrame(
            table,
            MarqueeFollowPolicy.BuildWindow(content, windowStart),
            windowStart,
            -(MarqueeFollowPolicy.ResolveWidthAt(widths, measured, position) - windowLeftWidth),
            Math.Max(0, revealWidth - windowLeftWidth),
            revealWidth);
    }
}
