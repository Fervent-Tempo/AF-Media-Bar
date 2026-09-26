namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词查询统一使用多歌手文本中的第一位歌手；不改动媒体快照中的显示文本。
/// Uses the first artist for lyric lookups without changing the media snapshot's display text.
/// </summary>
internal static class LyricsArtistPolicy
{
    // 不以空格拆分，避免截断英文歌手名。
    // Spaces belong to artist names and are not separators.
    public static string FirstArtist(string artist) =>
        artist.Split(['/', '／', '、', '&', '＆', ';', '；', ',', '，'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
}
