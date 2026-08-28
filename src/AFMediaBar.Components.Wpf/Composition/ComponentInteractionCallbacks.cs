using AFMediaBar.Components.BuiltIn.Playback;

namespace AFMediaBar.Components.Wpf.Composition;

public sealed record ComponentInteractionCallbacks(
    Action<object?>? SourceRequested = null,
    Action<PlaybackCommandKind, object?>? CommandRequested = null,
    Action<object?>? OutputDeviceRequested = null,
    Action<object?>? VolumeRequested = null,
    Action<int, object?>? OutputDeviceWheelRequested = null,
    Action<int, object?>? VolumeWheelRequested = null,
    Action? MetricsRequested = null);
