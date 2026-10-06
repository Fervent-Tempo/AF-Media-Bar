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

        var position = TaskbarExperiencePolicy.GetPosition(snapshot, now);
        var index = LyricLineIndexPolicy.FindIndex(lines, position);
        if (index < 0)
        {
            return LyricsPresentationFrame.Hidden;
        }

        var currentLine = lines[index];
        var nextLine = index + 1 < lines.Count ? lines[index + 1] : null;
        var followingLine = index + 2 < lines.Count ? lines[index + 2] : null;
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
        var wordScanProgress = settings.LyricsSyllableHighlightEnabled
            ? LyricHighlightPolicy.ResolveProgress(currentLine, position)
            : null;

        // 繁简转换落在"文本流向呈现层的最后一步"：当前行、下一句、译文与下一句译文都走同一个设置，
        // 因此不会出现"主行转了、第二行没转"的半截画面。逐字擦亮是 CSS 按渲染宽度百分比裁剪的，
        // 不依赖字符索引，所以转换改变字数也不会错位。
        // Conversion happens where text leaves for the presentation layer: the active line, the next line, and both translations all
        // follow the one setting, so there is never a half-converted frame. Syllable highlighting clips by rendered-width percentage in
        // CSS and does not index characters, so a changed character count cannot misalign it.
        var conversion = settings.LyricsChineseConversion;

        return new LyricsPresentationFrame(
            true,
            BuildTrackId(snapshot),
            index,
            LyricsChineseConverter.Convert(currentLine.Text, conversion),
            LyricsChineseConverter.Convert(visibleNext, conversion),
            LyricsChineseConverter.Convert(currentTranslation, conversion),
            LyricsChineseConverter.Convert(nextTranslation, conversion),
            translationMode,
            lineProgress,
            wordScanProgress,
            snapshot.IsPlaying);
    }

    private static string BuildTrackId(MediaSnapshot snapshot) => string.Join(
        "\u001f",
        snapshot.SourceId.Trim().ToUpperInvariant(),
        snapshot.Title.Trim().ToUpperInvariant(),
        snapshot.Artist.Trim().ToUpperInvariant());

    private static double ResolveLineProgress(LyricLine line, double position)
    {
        if (!double.IsFinite(line.Start) || !double.IsFinite(line.End) || line.End <= line.Start)
        {
            return 0;
        }

        return Math.Clamp((position - line.Start) / (line.End - line.Start), 0, 1);
    }
}
