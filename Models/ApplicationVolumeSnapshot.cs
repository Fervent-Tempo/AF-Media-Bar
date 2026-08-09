namespace AFMediaBar.Models;

internal sealed record ApplicationVolumeSnapshot(
    string ProcessName,
    string DisplayName,
    int VolumePercent,
    bool IsMuted);
