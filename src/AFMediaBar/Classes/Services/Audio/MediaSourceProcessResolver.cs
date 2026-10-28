using System.IO;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 在 SMTC 来源标识与进程名之间建立共享映射。
/// Provides the shared mapping between SMTC source identifiers and process names.
/// </summary>
public sealed class MediaSourceProcessResolver
{
    private static readonly (string[] Tokens, string[] ProcessNames)[] KnownSources =
    [
        (["cloudmusic", "netease", "163music"], ["cloudmusic"]),
        (["qqmusic"], ["QQMusic"]),
        (["kugou", "kgmusic"], ["KuGou", "KuGouMusic"]),
        (["spotify"], ["Spotify"]),
        (["chrome"], ["chrome"]),
        (["msedge", "microsoftedge"], ["msedge"]),
        (["firefox"], ["firefox"]),
        (["vlc"], ["vlc"]),
        (["potplayer", "daum"], ["PotPlayerMini64", "PotPlayerMini"]),
        (["zunemusic", "media.player", "wmplayer"], ["Microsoft.Media.Player", "Music.UI", "wmplayer"]),
        (["mpv"], ["mpv"]),
        (["foobar"], ["foobar2000"])
    ];

    /// <summary>返回可执行文件身份或登记别名；这些身份不允许显示名回退覆盖。</summary>
    public IReadOnlyList<string> ResolveProcessNames(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return [NormalizeProcessName(sourceId)];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in KnownSources)
        {
            if (mapping.Tokens.Any(token => sourceId.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                names.UnionWith(mapping.ProcessNames);
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// 对同一次枚举的完整候选集进行匹配。未知来源仅接受完整名称，歧义时不选择进程。
    /// Matches a complete scan using explicit identities or unique full-name fallbacks.
    /// </summary>
    public IReadOnlySet<string> ResolveMatches(string? sourceId, string? sourceName,
        IEnumerable<(string ProcessName, string DisplayName)> candidates)
    {
        var identities = ResolveProcessNames(sourceId);
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var processName = NormalizeProcessName(candidate.ProcessName);
            if (identities.Count > 0)
            {
                if (identities.Contains(processName, StringComparer.OrdinalIgnoreCase))
                    matches.Add(candidate.ProcessName);
                continue;
            }

            if (SameName(sourceId, processName) || SameName(sourceName, processName) ||
                SameName(sourceName, candidate.DisplayName))
                matches.Add(candidate.ProcessName);
        }

        // 不缓存回退结果；候选退出、重启或显示名变化后，下次枚举重新确认身份。
        return identities.Count == 0 && matches.Count > 1
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : matches;
    }

    private static bool SameName(string? source, string candidate) =>
        !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(candidate) &&
        string.Equals(source.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProcessName(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name.Trim() : name.Trim() + ".exe");
}
