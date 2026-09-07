namespace AFMediaBar.Classes.Models;

/// <summary>应用音频会话的聚合快照。 / Aggregated snapshot of an application's audio sessions.</summary>
public sealed record ApplicationVolumeSnapshot(
    string ProcessName,
    string DisplayName,
    int VolumePercent,
    bool IsMuted,
    bool IsCurrentMedia,
    byte[]? IconData);
