using System.Net;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 可直接发送给 Web 歌词呈现引擎的目标帧。歌词检索和文本解析已经在上游完成；该帧只表达目标画面。
/// A target frame ready for the web lyrics presentation engine. Retrieval and parsing are complete upstream; this frame only
/// describes the target visual state.
/// </summary>
public sealed record LyricsPresentationFrame(
    bool IsVisible,
    string TrackId,
    int CurrentLineIndex,
    string Current,
    string Next,
    string CurrentTranslation,
    string NextTranslation,
    bool TranslationMode,
    double LineProgress,
    double? WordScanProgress,
    double? ScrollProgress,
    bool IsPlaying)
{
    public static LyricsPresentationFrame Hidden { get; } = new(
        false,
        string.Empty,
        -1,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        0,
        null,
        null,
        false);
}

/// <summary>
/// 把不可变媒体快照投影成 Web 歌词目标帧。调用者只需要提交快照、设置和当前时间；行选择、第二行语义、
/// 位置外推与真实逐字进度全部封装在本模块中。
/// Projects an immutable media snapshot into a web-lyrics target frame. Callers submit only the snapshot, settings, and current
/// time; line selection, secondary-row semantics, timeline extrapolation, and real syllable progress stay inside this module.
/// </summary>
public static class LyricsPresentationProjector
{
    public static LyricsPresentationFrame Project(
        MediaSnapshot snapshot,
        AppSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);

        var lines = snapshot.Lyrics?.Document.Lines;
        if (!settings.LyricsEnabled || !snapshot.IsConnected || lines is not { Count: > 0 })
        {
            return LyricsPresentationFrame.Hidden;
        }

        // 播放器没有时间轴时立即隐藏歌词：换歌后时长未到的空窗与空闲未播放，都不该按位置 0 卡在第一句。
        // 「不提供播放进度」的说明由第二行在稳定期后另行显示，不在这里等待。
        if (MediaPlaybackProgressPolicy.ShouldHideLyrics(snapshot, settings.LyricsEnabled))
        {
            return LyricsPresentationFrame.Hidden;
        }

        var position = TaskbarExperiencePolicy.GetPosition(snapshot, now);

        var index = LyricLineIndexPolicy.FindIndex(lines, position);
        if (index < 0)
        {
            return LyricsPresentationFrame.Hidden;
        }

        var currentLine = DisplayLine(lines[index], settings);
        var nextLine = index + 1 < lines.Count ? DisplayLine(lines[index + 1], settings) : null;
        var followingLine = index + 2 < lines.Count ? DisplayLine(lines[index + 2], settings) : null;
        var currentSecondary = settings.TwoLineLyricsEnabled
            ? LyricsSecondaryLinePolicy.ResolveSelection(
                settings.LyricsSecondaryLine,
                nextLine?.Text,
                currentLine.Translation,
                currentLine.Romanization)
            : null;
        var nextSecondary = settings.TwoLineLyricsEnabled && nextLine is not null
            ? LyricsSecondaryLinePolicy.ResolveSelection(
                settings.LyricsSecondaryLine,
                followingLine?.Text,
                nextLine.Translation,
                nextLine.Romanization)
            : null;
        var translationMode = currentSecondary is { Mode: not LyricsSecondaryLineMode.NextLine };
        var visibleNext = currentSecondary is { Mode: LyricsSecondaryLineMode.NextLine } selection
            ? selection.Text
            : string.Empty;
        var currentTranslation = translationMode ? currentSecondary?.Text ?? string.Empty : string.Empty;
        var nextTranslation = nextSecondary is { Mode: not LyricsSecondaryLineMode.NextLine }
            ? nextSecondary.Value.Text
            : string.Empty;
        var lineProgress = ResolveLineProgress(currentLine, position);
        // 逐字时间轴进度同时驱动擦亮与长行横向滚动：擦亮只是叠在同一轨迹上的视觉层，
        // 因此关闭擦亮不得让长歌词失去滚动——时间轴照常推进，只是不再绘制高亮。
        // The syllable timeline drives both the highlight and the long-line scroll: the highlight is only a visual
        // layer over that same trajectory, so turning it off must not stop a long line from scrolling.
        var syllableProgress = LyricHighlightPolicy.ResolveProgress(currentLine, position);
        var wordScanProgress = settings.LyricsSyllableHighlightEnabled ? syllableProgress : null;

        // 繁简转换落在"文本流向呈现层的最后一步"：当前行、下一句、译文与下一句译文都走同一个设置，
        // 因此不会出现"主行转了、第二行没转"的半截画面。逐字擦亮是 CSS 按渲染宽度百分比裁剪的，
        // 不依赖字符索引，所以转换改变字数也不会错位。
        // Conversion happens where text leaves for the presentation layer: the active line, the next line, and both translations all
        // follow the one setting, so there is never a half-converted frame. Syllable highlighting clips by rendered-width percentage in
        // CSS and does not index characters, so a changed character count cannot misalign it.
        var conversion = settings.LyricsChineseConversion;
        var stripBracketSymbols = settings.LyricsBracketSymbolsHidden;

        return new LyricsPresentationFrame(
            true,
            BuildTrackId(snapshot),
            index,
            LyricsChineseConverter.Convert(currentLine.Text, conversion),
            LyricsChineseConverter.Convert(visibleNext, conversion),
            Present(currentTranslation, stripBracketSymbols, conversion),
            Present(nextTranslation, stripBracketSymbols, conversion),
            translationMode,
            lineProgress,
            wordScanProgress,
            syllableProgress,
            snapshot.IsPlaying);
    }

    /// <summary>
    /// 为桌面媒体卡片解析当前行的罗马音与译文：两者都经过与任务栏歌词相同的括号清理与繁简转换，
    /// 因此卡片上的副行不会和主栏的歌词处理脱节。媒体卡片同时展示两行副行，不受「二选一」副行策略约束，
    /// 所以这里直接读行上的两个字段，而不是走 <see cref="LyricsSecondaryLinePolicy"/>。
    /// Resolves the active line's romanization and translation for the desktop media card: both run through the same
    /// bracket cleaning and Chinese conversion as the taskbar lyrics, so the card's secondary rows never drift from the
    /// main bar's lyric handling. The card shows both secondary rows at once and is not bound by the one-or-the-other
    /// secondary policy, so this reads the two fields on the line directly instead of <see cref="LyricsSecondaryLinePolicy"/>.
    /// </summary>
    /// <param name="line">当前行，可为 null / The active line, which may be null.</param>
    /// <param name="settings">歌词处理设置 / Lyric-handling settings.</param>
    public static (string Romanization, string Translation) ResolveSecondary(LyricLine? line, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (line is null)
        {
            return (string.Empty, string.Empty);
        }

        var stripBracketSymbols = settings.LyricsBracketSymbolsHidden;
        var conversion = settings.LyricsChineseConversion;
        return (
            Present(line.Romanization ?? string.Empty, stripBracketSymbols, conversion),
            Present(PresentableTranslation(line.Translation), stripBracketSymbols, conversion));
    }

    /// <summary>翻译缺失时来源常写一个「//」占位，这里按空处理，避免卡片上冒出一个孤零零的斜杠。</summary>
    private static string PresentableTranslation(string? translation) =>
        string.IsNullOrWhiteSpace(translation) || translation.Trim() == "//"
            ? string.Empty
            : translation;

    /// <summary>
    /// 为桌面媒体卡片返回经过主程序同样处理（注音过滤、括号清理）的显示行，供逐字擦亮与横向滚动使用。
    /// 媒体卡片需要和 <see cref="Project"/> 产出的 <c>Current</c> 文本对应的逐字时间轴，因此不能直接读原始行。
    /// Returns the display line after the same main-bar processing (furigana filter and bracket cleaning), so the card's
    /// syllable wipe and horizontal scroll use the timeline that matches the <c>Current</c> text from <see cref="Project"/>.
    /// </summary>
    public static LyricLine ResolveDisplayLine(LyricLine line, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(settings);
        return DisplayLine(line, settings);
    }

    private static string BuildTrackId(MediaSnapshot snapshot) => string.Join(
        "\u001f",
        snapshot.SourceId.Trim().ToUpperInvariant(),
        snapshot.Title.Trim().ToUpperInvariant(),
        snapshot.Artist.Trim().ToUpperInvariant());

    private static LyricLine DisplayLine(LyricLine line, AppSettings settings)
    {
        var display = settings.LyricsFuriganaFilterEnabled ? LyricsFuriganaFilter.Apply(line) : line;
        return settings.LyricsBracketSymbolsHidden ? LyricsTranslationFormatFilter.Apply(display) : display;
    }

    /// <summary>
    /// 把一段副行文本送到呈现层：先还原在线接口的 HTML 实体，再按设置清理来源附加的格式标记
    /// （行首斜杠、整行括号），最后做繁简转换。主行不走这里。
    /// Sends a secondary line to the presentation layer: decodes HTML entities first, strips source format markers,
    /// then applies Chinese conversion. The main line never passes through here.
    /// </summary>
    private static string Present(string text, bool stripBracketSymbols, LyricsChineseConversionMode conversion)
    {
        // 在线接口的译文/音译常带 HTML 实体（&amp; 等）：主歌词在 QRC/LRC 解析层已解码，副行走的是另一条链，
        // 所以在这里统一解码。放在共享的 Present 里，任务栏副行与媒体卡片副行会一起受益，
        // 且不会触碰主行文本及其逐字时间轴（解码会改变字数，主行解码反而会让音节错位）。
        var decoded = WebUtility.HtmlDecode(text);
        return LyricsChineseConverter.Convert(
            stripBracketSymbols ? LyricsTranslationFormatFilter.Clean(decoded) : decoded, conversion);
    }

    private static double ResolveLineProgress(LyricLine line, double position)
    {
        if (!double.IsFinite(line.Start) || !double.IsFinite(line.End) || line.End <= line.Start)
        {
            return 0;
        }

        return Math.Clamp((position - line.Start) / (line.End - line.Start), 0, 1);
    }
}
