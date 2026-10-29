using AFMediaBar.Resources;
using System.IO;

namespace AFMediaBar.Classes.Services;

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
    private static readonly (string? Key, string Name, string[] Tokens)[] SourceNames =
    [
        ("Service.MediaSource.NetEaseCloudMusic", string.Empty, ["cloudmusic", "netease", "163music"]),
        ("Service.MediaSource.QQMusic", string.Empty, ["qqmusic"]),
        ("Service.MediaSource.Kugou", string.Empty, ["kugou", "kgmusic"]),
        (null, "Spotify", ["spotify"]),
        (null, "Google Chrome", ["chrome"]),
        (null, "Microsoft Edge", ["msedge", "microsoftedge"]),
        (null, "Firefox", ["firefox"]),
        (null, "VLC", ["vlc"]),
        (null, "PotPlayer", ["potplayer", "daum"]),
        (null, "Windows Media Player", ["zunemusic", "media.player", "wmplayer"]),
        (null, "mpv", ["mpv"]),
        (null, "foobar2000", ["foobar"])
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
}
