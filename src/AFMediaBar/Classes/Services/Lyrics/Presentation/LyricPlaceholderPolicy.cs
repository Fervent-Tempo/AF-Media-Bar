namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>占位行的处理类别：空行、普通无歌词提示、纯音乐提示和临时状态。</summary>
/// <remarks>
/// 临时状态（歌词加载中/制作中）只丢弃、不能被当作"确认无歌词"缓存；纯音乐标记是 #267 中唯一允许确认无歌词的证据，
/// 普通的"暂无歌词"仍只按占位行丢弃。
/// Transient states are dropped but never cached as confirmed no lyrics; only the pure-instrumental markers may confirm
/// no lyrics for #267, while a generic "暂无歌词" placeholder is still only dropped.
/// </remarks>
internal enum LyricPlaceholderKind
{
    /// <summary>不是占位文本。/ Not a placeholder.</summary>
    None,

    /// <summary>空行或空白行。/ Blank or whitespace-only line.</summary>
    Blank,

    /// <summary>普通"暂无歌词"一类占位提示，不用于确认无歌词。/ Generic "no lyrics" placeholder; not used to confirm.</summary>
    NoLyricsPlaceholder,

    /// <summary>明确的纯音乐提示，可确认无歌词。/ Explicit instrumental marker, may confirm no lyrics.</summary>
    Instrumental,

    /// <summary>歌词加载中/制作中一类临时状态，不能确认无歌词。/ Transient loading or production state.</summary>
    Transient
}

/// <summary>
/// 歌词占位文本的识别：来源用一行字表示"这首歌没有歌词"，那种行不是歌词，不能占着静置层的歌词位置。
/// Recognition of placeholder lyric text: a source sometimes answers with a single line that means "this song has no lyrics",
/// and such a line is not a lyric and must not take up the rest layer's lyric slot.
///
/// 实测（2026-09-17）：网易云两个端点在无词曲目上都返回 `[00:00.00]暂无歌词`，它会带着时间戳通过解析，于是静置层会把它
/// 当成唯一一句歌词显示出来。识别按标记词做，并要求整行就是一个标记（去掉空白与首尾标点后相等），或整行很短且含有标记：
/// 真正的歌词偶尔也会出现"歌词"二字，但不会短到只剩一句"暂无歌词"。
/// Measured on 2026-09-17: both NetEase endpoints answer `[00:00.00]暂无歌词` for a track without lyrics, which passes parsing
/// with a timestamp and would then be shown as the only lyric line. Detection works on marker phrases and requires either that
/// the whole line is one marker (equal after whitespace and edge punctuation are removed) or that a short line contains one:
/// real lyrics may mention the word "歌词", but never as a line as short as "暂无歌词".
/// </summary>
public static class LyricPlaceholderPolicy
{
    /// <summary>整行含有标记词时允许的最大长度（字符）；超过它的行不再按占位文本处理。
    /// Maximum length in characters for a line containing a marker to still count as a placeholder; longer lines are left alone.</summary>
    public const int MaximumContainedMarkerLength = 24;

    /// <summary>普通的"暂无歌词"一类占位；不含纯音乐与临时状态，避免与专用清单重复维护。</summary>
    private static readonly string[] NoLyricsPlaceholderMarkers =
    [
        "暂无歌词",
        "暫無歌詞",
        "暂无歌词。",
        "该歌曲暂无歌词",
        "該歌曲暫無歌詞",
        "此歌曲暂无歌词",
        "no lyrics",
        "lyrics not available",
        "no lyrics available",
        "lyrics unavailable"
    ];

    /// <summary>明确的纯音乐标记；只有这些标记可以参与"确认无歌词"（#267）。</summary>
    private static readonly string[] InstrumentalMarkers =
    [
        "純音樂，請欣賞",
        "纯音乐，请欣赏",
        "纯音乐请欣赏",
        "此歌曲为没有填词的纯音乐",
        "此歌曲為沒有填詞的純音樂",
        "instrumental"
    ];

    /// <summary>临时状态：只丢弃占位行，不能确认无歌词，否则会把暂时失败永久缓存。</summary>
    private static readonly string[] TransientMarkers =
    [
        "歌词加载中",
        "歌詞載入中",
        "歌词制作中"
    ];

    /// <summary>
    /// 判断一行文本是否为"没有歌词"的占位。
    /// Decides whether one line of text is a "no lyrics" placeholder.
    /// </summary>
    /// <param name="text">行文本 / Line text.</param>
    /// <returns>是占位文本时为 true / True when the text is a placeholder.</returns>
    public static bool IsPlaceholder(string? text) => Classify(text) != LyricPlaceholderKind.None;

    /// <summary>
    /// 区分空行、普通占位、纯音乐标记与临时状态；空行不算纯音乐标记。
    /// Classifies blank lines, generic placeholders, instrumental markers, and transient states; blank lines are not markers.
    /// </summary>
    internal static LyricPlaceholderKind Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LyricPlaceholderKind.Blank;
        }

        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return LyricPlaceholderKind.Blank;
        }

        if (Matches(normalized, TransientMarkers))
        {
            return LyricPlaceholderKind.Transient;
        }

        if (Matches(normalized, InstrumentalMarkers))
        {
            return LyricPlaceholderKind.Instrumental;
        }

        return Matches(normalized, NoLyricsPlaceholderMarkers)
            ? LyricPlaceholderKind.NoLyricsPlaceholder
            : LyricPlaceholderKind.None;
    }

    /// <summary>整行等于某个标记，或短行包含某个标记。</summary>
    private static bool Matches(string normalized, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (normalized.Equals(Normalize(marker), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (normalized.Length > MaximumContainedMarkerLength)
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (normalized.Contains(Normalize(marker), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 去掉空白与首尾标点，便于按内容比较。
    /// Removes whitespace and edge punctuation so the comparison is about content.
    /// </summary>
    private static string Normalize(string text) => text
        .Trim()
        .Trim('（', '）', '(', ')', '[', ']', '【', '】', '《', '》', '，', ',', '。', '.', '、', '~', '～', '-', '—', ':', '：', ';', '；')
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("\u3000", string.Empty, StringComparison.Ordinal)
        .Replace("\t", string.Empty, StringComparison.Ordinal)
        .Trim();
}
