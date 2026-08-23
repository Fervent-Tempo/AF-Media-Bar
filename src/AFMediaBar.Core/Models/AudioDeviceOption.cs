namespace AFMediaBar.Models;

public sealed record AudioDeviceOption(
    string Id,
    string PolicyId,
    string DisplayName,
    bool IsDefault);
