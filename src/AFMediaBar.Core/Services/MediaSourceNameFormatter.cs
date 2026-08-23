namespace AFMediaBar.Services;

public static class MediaSourceNameFormatter
{
    private static readonly (string Name, string[] Tokens)[] SourceNames =
    [
        ("网易云音乐", ["cloudmusic", "netease", "163music"]),
        ("QQ音乐", ["qqmusic"]),
        ("酷狗音乐", ["kugou", "kgmusic"]),
        ("Spotify", ["spotify"]),
        ("Google Chrome", ["chrome"]),
        ("Microsoft Edge", ["msedge", "microsoftedge"]),
        ("Firefox", ["firefox"]),
        ("VLC", ["vlc"]),
        ("PotPlayer", ["potplayer", "daum"]),
        ("Windows Media Player", ["zunemusic", "media.player", "wmplayer"]),
        ("mpv", ["mpv"]),
        ("foobar2000", ["foobar"])
    ];

    public static string GetDisplayName(string? sourceId, string unknownSourceName)
    {
        var value = sourceId?.Trim() ?? string.Empty;
        foreach (var mapping in SourceNames)
        {
            if (mapping.Tokens.Any(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return mapping.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return unknownSourceName;
        }

        var bangIndex = value.LastIndexOf('!');
        if (bangIndex >= 0 && bangIndex < value.Length - 1)
        {
            value = value[(bangIndex + 1)..];
        }

        value = Path.GetFileNameWithoutExtension(value);
        var packageIndex = value.IndexOf('_');
        if (packageIndex > 0)
        {
            value = value[..packageIndex];
        }

        return string.IsNullOrWhiteSpace(value) ? unknownSourceName : value;
    }
}
