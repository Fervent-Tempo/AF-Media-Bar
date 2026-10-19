// 全局音频设备选择策略；只限制 AF 的选择，不修改 Windows 的设备状态。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Settings;

/// <summary>设备按钮操作的目标；成员值参与设置序列化。</summary>
public enum AudioDeviceSwitchTarget { SystemDefault = 0, ForegroundApplication = 1, Disabled = 2 }

/// <summary>被隐藏设备的稳定标识与最后已知名称。</summary>
public sealed record AudioDeviceExclusion(string EndpointId, string DisplayName);

/// <summary>输入、输出设备的全局黑名单和通信角色策略。</summary>
public sealed record AudioDeviceSettings
{
    public IReadOnlyList<AudioDeviceExclusion> OutputBlacklist { get; init; } = [];
    public IReadOnlyList<AudioDeviceExclusion> InputBlacklist { get; init; } = [];
    public string OutputCycleHotkey { get; init; } = string.Empty;
    public string InputCycleHotkey { get; init; } = string.Empty;
    public IReadOnlyList<AudioDevicePreference> Preferences { get; init; } = [];
    public AudioDevicePreference? FindPreference(AudioDeviceDirection direction, string id) => Preferences.FirstOrDefault(entry =>
        entry.Direction == direction && entry.EndpointId.Equals(id, StringComparison.OrdinalIgnoreCase));
    public bool SwitchOutputCommunications { get; init; }
    public bool SwitchInputCommunications { get; init; }
    public static AudioDeviceSettings Default { get; } = new();

    public bool IsAllowed(AudioDeviceDirection direction, string endpointId) =>
        !(direction == AudioDeviceDirection.Output ? OutputBlacklist : InputBlacklist)
        .Any(entry => string.Equals(entry.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));

    public AudioDeviceSettings Normalize() => this with
    {
        OutputCycleHotkey = (OutputCycleHotkey ?? string.Empty).Trim(),
        InputCycleHotkey = (InputCycleHotkey ?? string.Empty).Trim(),
        Preferences = (Preferences ?? []).Where(entry => entry is not null && Enum.IsDefined(entry.Direction) && !string.IsNullOrWhiteSpace(entry.EndpointId))
            .Select(entry => entry with { EndpointId = entry.EndpointId.Trim(), LastKnownName = entry.LastKnownName ?? string.Empty,
                Alias = (entry.Alias ?? string.Empty).Trim() }).DistinctBy(entry => $"{entry.Direction}:{entry.EndpointId}", StringComparer.OrdinalIgnoreCase).ToArray(),
        OutputBlacklist = NormalizeList(OutputBlacklist),
        InputBlacklist = NormalizeList(InputBlacklist)
    };

    private static IReadOnlyList<AudioDeviceExclusion> NormalizeList(IReadOnlyList<AudioDeviceExclusion>? entries) =>
        (entries ?? []).Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.EndpointId))
        .Select(entry => entry with { EndpointId = entry.EndpointId.Trim(), DisplayName = entry.DisplayName ?? string.Empty })
        .DistinctBy(entry => entry.EndpointId, StringComparer.OrdinalIgnoreCase).ToArray();
}
