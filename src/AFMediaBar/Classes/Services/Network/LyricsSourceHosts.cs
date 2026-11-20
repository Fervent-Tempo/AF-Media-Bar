// 歌词来源与请求域名的对应表；代理策略与连通性测试共用。
using AFMediaBar.Classes.Services.Lyrics;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 歌词请求会碰到的域名按来源组归类。来源组 id 直接用来源目录的常量（设置键与显示名同源），
/// 只有 Apple Music 原名解析不是取词提供器，单独占一个组；
/// 网易云与网易云搜索两个提供器共用一组，因为它们请求的域名相同。
/// 这里按域名分组，与取词来源目录的顺序不是一回事。
/// Lyric request hosts grouped by source. Group ids come straight from the source catalogue so settings keys and display
/// names stay single-sourced; only the Apple Music original-name resolver is not a retrieval provider and gets its own
/// group, while the NetEase and NetEase-search providers share one group because they hit the same hosts. The grouping is by
/// host and deliberately unrelated to the retrieval catalogue's order.
/// </summary>
internal static class LyricsSourceHosts
{
    /// <summary>Apple Music 原名解析：只请求原名，不是取词提供器。/ The Apple Music original-name resolver, which only looks up original names.</summary>
    public const string AppleMusic = "AppleMusic";

    private static readonly (string SourceId, string[] Hosts)[] Groups =
    [
        (LyricsSourceCatalog.NetEase, ["music.163.com", "music.126.net"]),
        (LyricsSourceCatalog.QQMusic, ["y.qq.com", "qqmusic.qq.com"]),
        (LyricsSourceCatalog.Kugou, ["kugou.com"]),
        (LyricsSourceCatalog.SodaMusic, ["music.douyin.com", "qishui.com"]),
        (LyricsSourceCatalog.Lrclib, ["lrclib.net"]),
        (AppleMusic, ["music.apple.com", "itunes.apple.com", "mzstatic.com"])
    ];

    /// <summary>设置页按这个顺序列出全部来源组。/ The order the settings page lists every source group in.</summary>
    public static IReadOnlyList<string> AllSourceIds { get; } =
    [
        LyricsSourceCatalog.NetEase,
        LyricsSourceCatalog.QQMusic,
        LyricsSourceCatalog.Kugou,
        LyricsSourceCatalog.SodaMusic,
        LyricsSourceCatalog.Lrclib,
        AppleMusic
    ];

    /// <summary>返回请求域名所属的来源组；不在表里返回 null。/ Resolves the group a host belongs to; null when unlisted.</summary>
    public static string? SourceIdFor(string host)
    {
        foreach (var (sourceId, hosts) in Groups)
        {
            foreach (var candidate in hosts)
            {
                if (host.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase))
                    return sourceId;
            }
        }
        return null;
    }

    /// <summary>探测实际取词 API 的地址，HTTP 响应只表示该地址可达。</summary>
    public static string? TestEndpointFor(string sourceId) => sourceId switch
    {
        LyricsSourceCatalog.NetEase => "https://music.163.com/api/song/lyric",
        LyricsSourceCatalog.QQMusic => "https://u.y.qq.com/cgi-bin/musicu.fcg",
        LyricsSourceCatalog.Kugou => "https://lyrics.kugou.com/download",
        LyricsSourceCatalog.SodaMusic => "https://api.qishui.com/luna/pc/search/track",
        LyricsSourceCatalog.Lrclib => "https://lrclib.net/api/search",
        AppleMusic => "https://itunes.apple.com/search",
        _ => null
    };
}
