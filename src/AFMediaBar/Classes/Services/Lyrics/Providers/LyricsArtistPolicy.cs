// Normalizes artist lists for lyric matching and queries; never changes the displayed player metadata.
// 用户分隔符为逐行字面量，保留空格；所有播放器使用同一份规则，不补充来源专用规则。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>按本次请求的统一分隔符解析参与歌词匹配的歌手列表。</summary>
internal static class LyricsArtistPolicy
{
    internal static string[] Split(LyricsRequest request) =>
        Split(request.Artist, request.ArtistSeparators);

    internal static string[] Split(string? artist, string? customSeparators)
    {
        var separators = (customSeparators ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(separator => !string.IsNullOrWhiteSpace(separator));
        var ordered = separators.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(separator => separator.Length).ToArray();
        var text = artist ?? string.Empty;
        if (ordered.Length == 0) return [text];

        // 从左到右消费最长字面量，避免重叠分隔符截断名字；大小写不影响 feat. 等标记。
        var artists = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length;)
        {
            var separator = ordered.FirstOrDefault(value =>
                text.AsSpan(index).StartsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase));
            if (separator is null) { index++; continue; }
            Add(text[start..index]);
            index += separator.Length;
            start = index;
        }
        Add(text[start..]);
        return artists.Count == 0 ? [string.Empty] : artists.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void Add(string value)
        {
            value = value.Trim();
            if (value.Length > 0) artists.Add(value);
        }
    }
}
