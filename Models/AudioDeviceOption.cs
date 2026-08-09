namespace AFMediaBar.Models;

internal sealed record AudioDeviceOption(
    string Id,
    string PolicyId,
    string DisplayName,
    bool IsDefault);
