namespace AFMediaBar.Models;

public sealed record ApplicationVolumeSnapshot(
    string ProcessName,
    string DisplayName,
    int VolumePercent,
    bool IsMuted);
