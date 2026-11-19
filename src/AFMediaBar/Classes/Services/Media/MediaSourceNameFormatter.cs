using AFMediaBar.Resources;
using System.IO;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 已知来源的类别，取值顺序就是「音乐程序优先」的取源顺序。
/// Categories of known sources; the declaration order is the tier order used by the music-application-first mode.
/// </summary>
public enum MediaSourceCategory
{
    /// <summary>纯音频播放器：网易云、QQ 音乐、酷狗、Spotify、Folia、NONS、foobar2000。</summary>
    Music = 0,
    /// <summary>通用媒体播放器：PotPlayer、mpv、VLC、Windows Media Player。</summary>
    Video = 1,
    /// <summary>浏览器：分不清在放歌还是放视频，排在最后。</summary>
    Browser = 2,
    /// <summary>未登记来源；不参与分层，只在三层都空时按通用规则处理。</summary>
    Other = 3
}

/// <summary>
/// 把 SMTC 来源标识（AppUserModelId）格式化为用户可读的名称。
/// Formats a SMTC source identifier (AppUserModelId) into a user-readable name.
/// </summary>
public static class MediaSourceNameFormatter
{
    /// <summary>
    /// 来源标识的匹配令牌。名字要么是**文案键**，要么是品牌名，两者都只在这里出现一次：
    /// 键在调用时按当前语言解析（静态字段只初始化一次，把翻译结果存进来会让来源名永远停在启动时的语言上），
    /// 品牌名（Spotify、Chrome、Firefox、VLC、mpv 一类）在三种语言下写法相同，因此直接写在表里。
    /// Matching tokens for source identifiers. A name is either a *localization key* or a brand name, and each appears
    /// exactly once here: a key is resolved per call in the active language (a static field initializes once, and storing a
    /// translation in it would pin the source name to the language the application started in), while a brand name — Spotify,
    /// Chrome, Firefox, VLC, mpv and the like — reads the same in all three languages and is therefore written out here.
    /// </summary>
    private static readonly (string? Key, string Name, string[] Tokens, MediaSourceCategory Category)[] SourceNames =
    [
        ("Service.MediaSource.NetEaseCloudMusic", string.Empty, ["cloudmusic", "netease", "163music"], MediaSourceCategory.Music),
        ("Service.MediaSource.QQMusic", string.Empty, ["qqmusic"], MediaSourceCategory.Music),
        ("Service.MediaSource.Kugou", string.Empty, ["kugou", "kgmusic"], MediaSourceCategory.Music),
        // Folia 的 SMTC 标识是打包标识 top.izuna.foliamajor；只登记已确认的完整标识，避免 top.izuna/folia 短前缀通过 Contains 误配其他来源。
        // Folia reports top.izuna.foliamajor as its SMTC identity; without this mapping the package prefix leaks into the UI as "top.izuna".
        (null, "Folia", ["top.izuna.foliamajor", "folia.exe"], MediaSourceCategory.Music),
        // NonsPlayer 的 SMTC 标识与进程名都是 Nons；不登记时首页会把包名原样显示成 nons 并归到 other。
        // NonsPlayer reports Nons as both its SMTC identity and its process name; without this the home page shows the bare package name and files it under other.
        (null, "NONS", ["nons"], MediaSourceCategory.Music),
        (null, "Spotify", ["spotify"], MediaSourceCategory.Music),
        (null, "Google Chrome", ["chrome"], MediaSourceCategory.Browser),
        (null, "Microsoft Edge", ["msedge", "microsoftedge"], MediaSourceCategory.Browser),
        (null, "Firefox", ["firefox"], MediaSourceCategory.Browser),
        (null, "VLC", ["vlc"], MediaSourceCategory.Video),
        (null, "PotPlayer", ["potplayer", "daum"], MediaSourceCategory.Video),
        (null, "Windows Media Player", ["zunemusic", "media.player", "wmplayer"], MediaSourceCategory.Video),
        (null, "mpv", ["mpv"], MediaSourceCategory.Video),
        (null, "foobar2000", ["foobar"], MediaSourceCategory.Music)
    ];

    /// <summary>
    /// 将来源标识规范化为稳定的用户可见名称，并为未知或空标识提供本地化回退。
    /// Normalizes a source identifier into a stable display name with a localized fallback for unknown or empty identifiers.
    /// </summary>
    /// <param name="sourceId">来源应用标识。/ Source application identifier.</param>
    /// <param name="unknownSourceName">无法识别时的回退名称，由调用方按当前语言给出。/ Fallback name when the identifier is unrecognized, given by the caller in the active language.</param>
    public static string GetDisplayName(string? sourceId, string unknownSourceName)
    {
        var value = sourceId?.Trim() ?? string.Empty;
        foreach (var mapping in SourceNames)
        {
            if (mapping.Tokens.Any(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return mapping.Key is null ? mapping.Name : Translations.Get(mapping.Key);
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

    /// <summary>返回已知来源的固定类别；未知来源不携带原始应用标识。</summary>
    public static string GetKnownSourceKey(string? sourceId)
    {
        var value = sourceId?.Trim() ?? string.Empty;
        foreach (var mapping in SourceNames)
            if (mapping.Tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                return mapping.Key ?? mapping.Name;
        return "other";
    }

    /// <summary>
    /// 返回来源所属的类别，供「音乐程序优先」分层取源；未登记的来源归为 <see cref="MediaSourceCategory.Other"/>，
    /// 不参与分层，只在三层都没有候选时按通用规则处理。
    /// Returns the category a source belongs to, used by the music-application-first tiering. Unregistered sources fall into
    /// <see cref="MediaSourceCategory.Other"/>: they take no tier and are only considered when all three tiers are empty.
    /// </summary>
    public static MediaSourceCategory GetCategory(string? sourceId)
    {
        var value = sourceId?.Trim() ?? string.Empty;
        foreach (var mapping in SourceNames)
            if (mapping.Tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                return mapping.Category;
        return MediaSourceCategory.Other;
    }
}
