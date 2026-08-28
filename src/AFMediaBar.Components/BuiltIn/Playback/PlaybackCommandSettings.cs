using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Playback;

public enum PlaybackCommandKind
{
    Previous = 0,
    PlayPause = 1,
    Next = 2,
    SelectSource = 3,
    AdjustVolume = 4,
    SelectOutputDevice = 5
}

public sealed record PlaybackCommandSettings(
    PlaybackCommandKind Command = PlaybackCommandKind.PlayPause,
    int ButtonSizeDip = 24) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.PlaybackCommand;
}
