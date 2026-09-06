namespace AFMediaBar.Classes.Models;

/// <summary>当前输出设备的空间音效状态。 / Spatial-audio state for the current render device.</summary>
public sealed record SpatialAudioSnapshot(
    bool IsSupported,
    string DisplayName,
    string? FormatSubtype);
