using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 第二行歌词的取词顺序：**按用户给定的顺序取第一个有内容的来源**，默认顺序是 下一句 → 翻译 → 音译。
///
/// 原来只取一个来源：选了"译文"而这一句没有译文时，第二行就空着——而用户手里往往同时有音译或下一句。
/// 顺序由歌词页按来源列表那种"逐项上移/下移"的方式调整（`LyricsSecondaryLineSettings.Order`），未列出的来源不会被使用。
/// Source order of the second lyric line: **the first source with content wins, following the user's order**, whose default is
/// next line, translation, romanization.
///
/// Previously only one source was read, so choosing "translation" left the second line blank whenever that line had no translation — even
/// though a romanization or the next line was available. The order is adjusted on the Lyrics page the same way the source list is, with
/// per-row move buttons (`LyricsSecondaryLineSettings.Order`), and a source missing from that list is never used.
/// </summary>
public static class LyricsSecondaryLinePolicy
{
    /// <summary>默认顺序：下一句 → 翻译 → 音译。/ The default order: next line, translation, romanization.</summary>
    public static readonly IReadOnlyList<LyricsSecondaryLineMode> DefaultOrder =
    [
        LyricsSecondaryLineMode.NextLine,
        LyricsSecondaryLineMode.Translation,
        LyricsSecondaryLineMode.Romanization
    ];

    /// <summary>设置里生效的顺序：未配置（null）时是默认顺序，显式给出的顺序原样使用（空数组表示一个来源都不用）。/ The effective order from the settings: the default order while nothing is configured (null), otherwise the explicit order as given, where an empty array means no source at all.</summary>
    /// <param name="settings">第二行顺序设置。/ Second-line order settings.</param>
    public static IReadOnlyList<LyricsSecondaryLineMode> ResolveOrder(LyricsSecondaryLineSettings settings) =>
        settings.Order ?? DefaultOrder;

    /// <summary>
    /// 按顺序挑出第二行的内容，全部来源都为空时返回空串（调用方据此隐藏第二行，不留空白占位）。
    /// Picks the second line's content along the order, returning an empty string when every source is empty so that the caller hides the row
    /// instead of leaving a blank placeholder.
    /// </summary>
    /// <param name="settings">第二行顺序设置。/ Second-line order settings.</param>
    /// <param name="nextLine">下一句歌词。/ The next lyric line.</param>
    /// <param name="translation">当前句译文。/ Translation of the active line.</param>
    /// <param name="romanization">当前句音译。/ Romanization of the active line.</param>
    public static string Resolve(
        LyricsSecondaryLineSettings settings,
        string? nextLine,
        string? translation,
        string? romanization)
        => ResolveSelection(settings, nextLine, translation, romanization)?.Text ?? string.Empty;

    /// <summary>
    /// 按设置解析第二行，同时保留命中的来源种类，供呈现引擎选择“下一句”或“原文+附属行”布局。
    /// Resolves the second line while retaining the selected source kind so the presentation engine can choose between
    /// a next-line layout and an original-plus-secondary layout.
    /// </summary>
    public static LyricsSecondaryLineSelection? ResolveSelection(
        LyricsSecondaryLineSettings settings,
        string? nextLine,
        string? translation,
        string? romanization)
    {
        foreach (var mode in ResolveOrder(settings))
        {
            if (Pick(mode, nextLine, translation, romanization) is { Length: > 0 } value)
            {
                return new LyricsSecondaryLineSelection(mode, value);
            }
        }

        return null;
    }

    /// <summary>取某一种来源的文本；空白一律按"没有内容"处理。/ Reads one source's text, treating whitespace as absent.</summary>
    private static string? Pick(
        LyricsSecondaryLineMode mode,
        string? nextLine,
        string? translation,
        string? romanization)
    {
        var value = mode switch
        {
            LyricsSecondaryLineMode.Translation => translation,
            LyricsSecondaryLineMode.Romanization => romanization,
            _ => nextLine
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>第二行最终选中的来源及文本。/ The selected source and text of the second lyric row.</summary>
public readonly record struct LyricsSecondaryLineSelection(LyricsSecondaryLineMode Mode, string Text);
