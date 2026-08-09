namespace AFMediaBar.Models;

internal sealed record MediaSessionOption(
    string Key,
    string SourceId,
    string DisplayName,
    bool IsPlaying,
    bool IsSelected);
