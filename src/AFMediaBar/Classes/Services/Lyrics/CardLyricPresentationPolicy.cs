using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 灵动岛卡片歌词区的呈现判定：第一行取当前句，第二行按用户排的来源顺序取词。
/// Presentation decision for the capsule island's card lyric area: the first row takes the active line and the second one
/// picks its source along the order the user arranged.
///
/// 判定与任务栏模式逐条同源（见 <c>TaskBarMediaControl.ApplyLyricPresentation</c>）：总开关关掉时两行都不显示，总开关打开
/// 而当前句为空时第一行落到调用方给的回落文本（卡片是作者），第二行还要求双行开关打开且来源真的取到了内容，否则整行隐藏，
/// 不留空白占位。取词本身一律交给 <see cref="LyricsSecondaryLinePolicy"/>，这里不另写一套顺序。
/// The decision is line for line the same one the taskbar mode makes (see <c>TaskBarMediaControl.ApplyLyricPresentation</c>): with
/// the master switch off neither row shows, with it on but no active line the first row falls back to the text the caller
/// supplies (the artist on the card), and the second row additionally needs the two-line switch on and a source that really has
/// content — otherwise the row is hidden rather than left empty. Picking the source is always
/// <see cref="LyricsSecondaryLinePolicy"/>'s job; no second copy of the order lives here.
/// </summary>
public static class CardLyricPresentationPolicy
{
    /// <summary>
    /// 卡片歌词区第一行的文本。
    /// Text of the card lyric area's first row.
    /// </summary>
    /// <param name="Text">要写进第一行的文本（回落文本包含在内）。/ The text to write into the first row, fallback included.</param>
    /// <param name="ShowActiveLine">第一行是否为真正的歌词句（落回作者时为 false，擦亮层据此不显示）。/ Whether the first row is a real lyric line; false when it fell back to the artist, which keeps the highlight layer off.</param>
    public readonly record struct LineState(string Text, bool ShowActiveLine);

    /// <summary>
    /// 卡片歌词区的完整呈现状态。
    /// The card lyric area's whole presentation state.
    /// </summary>
    /// <param name="FirstLine">第一行 / The first row.</param>
    /// <param name="SecondaryText">第二行文本，未启用或没有内容时为空 / The second row's text, empty while it is off or has no content.</param>
    /// <param name="ShowSecondaryLine">是否显示第二行（为 false 时整行 <c>Collapsed</c>，不留空白占位）/ Whether the second row shows; when false the whole row collapses instead of leaving blank space.</param>
    public readonly record struct PresentationState(LineState FirstLine, string SecondaryText, bool ShowSecondaryLine);

    /// <summary>
    /// 解析卡片歌词区的两行呈现。
    /// Resolves the card lyric area's two rows.
    /// </summary>
    /// <param name="settings">应用设置：歌词总开关、双行开关与第二行来源顺序。/ Application settings: the lyrics master switch, the two-line switch, and the second row's source order.</param>
    /// <param name="update">歌词取词结果（<see cref="LyricLinePresenter"/> 产出）。/ The lyric selection result produced by <see cref="LyricLinePresenter"/>.</param>
    /// <param name="fallbackText">当前句没有可用歌词（空、空白或命中占位文本）时的回落文本（卡片是作者）。/ Text to fall back to when the active line is unusable — empty, whitespace, or a placeholder — the artist on the card.</param>
    public static PresentationState Resolve(AppSettings settings, LyricLineUpdate update, string? fallbackText)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var activeText = TextOrEmpty(update.Text);
        // 占位文本不算歌词：来源用"[00:00.00]暂无歌词"这类整行表示"这首歌没有歌词"，它带着时间戳能通过解析，
        // 若不加这一层判定就会顶替歌词位置、还会把 CurrentLine 认成有效当前行，于是第二行与擦亮层一起被激活。
        // 判定一律复用 <see cref="LyricPlaceholderPolicy"/>（与任务栏同一份语义），空白行同样按占位处理。
        // A placeholder is not a lyric: a source answers "[00:00.00]暂无歌词" for a track without lyrics, which passes parsing with its
        // timestamp, so without this check it would take the lyric slot and make CurrentLine look like a real active line, activating both
        // the second row and the reveal layer. The check always reuses <see cref="LyricPlaceholderPolicy"/> (the same semantics the taskbar
        // uses), and a blank line counts as a placeholder too.
        var isPlaceholder = LyricPlaceholderPolicy.IsPlaceholder(activeText);
        // 总开关关掉时第一行也是空的：调用方据此落回回落文本，与"没有歌词"是同一条路。
        // With the master switch off the first row is empty as well, so the caller falls back exactly as it does without lyrics.
        var showLyrics = settings.LyricsEnabled;
        var usableLine = showLyrics && !isPlaceholder;
        var firstLine = usableLine ? activeText : string.Empty;
        var showActiveLine = usableLine && !string.IsNullOrEmpty(activeText);

        var secondary = usableLine
            ? TextOrEmpty(LyricsSecondaryLinePolicy.Resolve(
                settings.LyricsSecondaryLine,
                update.NextText,
                update.TranslationText,
                update.RomanizationText))
            : string.Empty;
        var showSecondary = showActiveLine && settings.TwoLineLyricsEnabled && secondary.Length > 0;

        return new PresentationState(
            new LineState(
                firstLine.Length > 0 ? firstLine : fallbackText ?? string.Empty,
                showActiveLine),
            showSecondary ? secondary : string.Empty,
            showSecondary);
    }

    /// <summary>把 null 归成空串：歌词取词在无内容时给的是空串，只有文档缺失时才可能是 null。/ Turns null into an empty string: lyric selection answers with empty text, and only a missing document can produce null.</summary>
    private static string TextOrEmpty(string? text) => text ?? string.Empty;
}
