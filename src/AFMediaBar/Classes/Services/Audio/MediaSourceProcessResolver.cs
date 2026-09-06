using System.IO;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 在 SMTC 来源标识与进程名之间建立共享映射。
/// Provides the shared mapping between SMTC source identifiers and process names.
/// </summary>
public sealed class MediaSourceProcessResolver
{
    private const int MaximumRememberedSources = 64;
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

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _rememberedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _rememberedOrder = new();

    public IReadOnlyList<string> ResolveProcessNames(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in KnownSources)
        {
            if (mapping.Tokens.Any(token => sourceId.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                names.UnionWith(mapping.ProcessNames);
            }
        }

        if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(Path.GetFileNameWithoutExtension(sourceId));
        }

        return names.ToArray();
    }

    public bool Matches(string? sourceId, string? sourceName, string processName, string displayName)
    {
        var key = GetSourceKey(sourceId, sourceName);
        lock (_gate)
        {
            if (key is not null && _rememberedProcesses.TryGetValue(key, out var remembered))
            {
                return string.Equals(remembered, processName, StringComparison.OrdinalIgnoreCase);
            }
        }

        return ResolveProcessNames(sourceId).Contains(processName, StringComparer.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(sourceId) && sourceId.Contains(processName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(sourceName) &&
             (string.Equals(sourceName, displayName, StringComparison.OrdinalIgnoreCase) ||
              processName.Contains(sourceName, StringComparison.OrdinalIgnoreCase) ||
              sourceName.Contains(processName, StringComparison.OrdinalIgnoreCase)));
    }

    public void Remember(string? sourceId, string? sourceName, string processName)
    {
        var key = GetSourceKey(sourceId, sourceName);
        if (key is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_rememberedProcesses.ContainsKey(key))
            {
                while (_rememberedProcesses.Count >= MaximumRememberedSources &&
                       _rememberedOrder.TryDequeue(out var oldest))
                {
                    _rememberedProcesses.Remove(oldest);
                }

                _rememberedOrder.Enqueue(key);
            }

            _rememberedProcesses[key] = processName;
        }
    }

    private static string? GetSourceKey(string? sourceId, string? sourceName) =>
        !string.IsNullOrWhiteSpace(sourceId)
            ? $"id:{sourceId}"
            : string.IsNullOrWhiteSpace(sourceName) ? null : $"name:{sourceName}";
}
