using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.Layout;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.System;
using AFMediaBar.Components.Wpf.BuiltIn.Artwork;
using AFMediaBar.Components.Wpf.BuiltIn.MediaSource;
using AFMediaBar.Components.Wpf.BuiltIn.MediaText;
using AFMediaBar.Components.Wpf.BuiltIn.Metrics;
using AFMediaBar.Components.Wpf.BuiltIn.OutputDevice;
using AFMediaBar.Components.Wpf.BuiltIn.PlaybackCommand;
using AFMediaBar.Components.Wpf.BuiltIn.Separator;
using AFMediaBar.Components.Wpf.BuiltIn.Spectrum;
using AFMediaBar.Components.Wpf.BuiltIn.Volume;

namespace AFMediaBar.Components.Wpf;

/// <summary>Creates presentation state for migrated component settings.</summary>
public static class ComponentViewFactory
{
    public static ComponentViewModelBase? Create(
        string instanceId,
        IComponentSettings settings,
        Action<object?>? sourceRequested = null,
        Action<PlaybackCommandKind, object?>? commandRequested = null,
        Action<object?>? deviceRequested = null,
        Action<object?>? volumeRequested = null,
        Action<int, object?>? outputDeviceWheelRequested = null,
        Action<int, object?>? volumeWheelRequested = null,
        Action? metricsRequested = null)
    {
        return settings switch
        {
            ArtworkSettings artwork => new ArtworkViewModel(instanceId, artwork, sourceRequested),
            MediaTextSettings text => new MediaTextViewModel(instanceId, text),
            MediaSourceSettings source => new MediaSourceViewModel(instanceId, source, sourceRequested),
            PlaybackCommandSettings command => new PlaybackCommandViewModel(instanceId, command, commandRequested),
            OutputDeviceSettings device => new OutputDeviceViewModel(instanceId, device, deviceRequested, outputDeviceWheelRequested),
            VolumeSettings volume => new VolumeViewModel(instanceId, volume, volumeRequested, volumeWheelRequested),
            SpectrumSettings spectrum => new SpectrumViewModel(instanceId, spectrum),
            MetricsSettings metrics => new MetricsViewModel(instanceId, metrics, metricsRequested),
            SeparatorSettings separator => new SeparatorViewModel(instanceId, separator),
            _ => null
        };
    }
}
