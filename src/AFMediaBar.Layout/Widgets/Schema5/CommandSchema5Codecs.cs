using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets.Schema5;

internal sealed class PlaybackCommandSchema5Codec : Schema5ComponentCodec<PlaybackCommandSettings, CommandWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.PlaybackCommand;
    protected override CommandWidgetSettings ToSchema5(PlaybackCommandSettings settings) =>
        new((MediaCommandKind)settings.Command, settings.ButtonSizeDip);
    protected override IComponentSettings? FromSchema5(CommandWidgetSettings settings) => settings.Command switch
    {
        MediaCommandKind.SelectOutputDevice => new OutputDeviceSettings(settings.ButtonSizeDip),
        MediaCommandKind.AdjustVolume => new VolumeSettings(settings.ButtonSizeDip),
        _ => new PlaybackCommandSettings((PlaybackCommandKind)settings.Command, settings.ButtonSizeDip)
    };
}

internal sealed class OutputDeviceSchema5Codec : Schema5ComponentCodec<OutputDeviceSettings, CommandWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.OutputDevice;
    protected override CommandWidgetSettings ToSchema5(OutputDeviceSettings settings) =>
        new(MediaCommandKind.SelectOutputDevice, settings.ButtonSizeDip);
    protected override OutputDeviceSettings? FromSchema5(CommandWidgetSettings settings) =>
        settings.Command == MediaCommandKind.SelectOutputDevice ? new(settings.ButtonSizeDip) : null;
}

internal sealed class VolumeSchema5Codec : Schema5ComponentCodec<VolumeSettings, CommandWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.Volume;
    protected override CommandWidgetSettings ToSchema5(VolumeSettings settings) =>
        new(MediaCommandKind.AdjustVolume, settings.ButtonSizeDip);
    protected override VolumeSettings? FromSchema5(CommandWidgetSettings settings) =>
        settings.Command == MediaCommandKind.AdjustVolume ? new(settings.ButtonSizeDip) : null;
}
