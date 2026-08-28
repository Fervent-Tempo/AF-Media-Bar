namespace AFMediaBar.Layout.Widgets.Schema5;

internal static class Schema5ComponentCodecs
{
    public static IReadOnlyList<ISchema5ComponentCodec> All { get; } =
    [
        new ArtworkSchema5Codec(),
        new MediaTextSchema5Codec(),
        new MediaSourceSchema5Codec(),
        new PlaybackCommandSchema5Codec(),
        new OutputDeviceSchema5Codec(),
        new VolumeSchema5Codec(),
        new SpectrumSchema5Codec(),
        new MetricsSchema5Codec(),
        new SeparatorSchema5Codec()
    ];
}
