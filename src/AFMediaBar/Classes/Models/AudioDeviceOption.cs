namespace AFMediaBar.Classes.Models;

/// <summary>可用的 Windows 音频输出设备。 / Available Windows audio render device.</summary>
public sealed record AudioDeviceOption(
    string Id,
    string PolicyId,
    string DisplayName,
    bool IsDefault);
