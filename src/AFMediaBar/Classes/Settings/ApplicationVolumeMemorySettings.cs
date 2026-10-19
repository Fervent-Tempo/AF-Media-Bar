// 仅保存用户选择的应用及最后一次会话音量，不保存 PID、音频会话或静音状态。
using System.IO;
using System.Text.Json.Serialization;

namespace AFMediaBar.Classes.Settings;

/// <summary>按可执行文件路径记忆的应用音量；空音量表示等待首次读取。</summary>
public sealed record ApplicationVolumeMemoryEntry(string ExecutablePath, int? VolumePercent = null)
{
    [JsonIgnore] public string DisplayName => Path.GetFileNameWithoutExtension(ExecutablePath);
}

/// <summary>应用音量记忆的显式选择列表，默认不接管任何应用。</summary>
public sealed record ApplicationVolumeMemorySettings
{
    public IReadOnlyList<ApplicationVolumeMemoryEntry> Entries { get; init; } = [];
    public static ApplicationVolumeMemorySettings Default { get; } = new();

    public ApplicationVolumeMemorySettings Normalize()
    {
        var entries = new List<ApplicationVolumeMemoryEntry>();
        foreach (var entry in Entries ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ExecutablePath)) continue;
            try
            {
                var path = entry.ExecutablePath.Trim();
                if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(entry with { ExecutablePath = Path.GetFullPath(path),
                    VolumePercent = entry.VolumePercent is { } value ? Math.Clamp(value, 0, 100) : null });
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { }
        }
        return this with { Entries = entries.DistinctBy(entry => entry.ExecutablePath, StringComparer.OrdinalIgnoreCase).ToArray() };
    }
}
