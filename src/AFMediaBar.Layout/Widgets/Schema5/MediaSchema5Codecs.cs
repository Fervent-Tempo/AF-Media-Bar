using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets.Schema5;

internal sealed class ArtworkSchema5Codec : Schema5ComponentCodec<ArtworkSettings, ArtworkWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.Artwork;
    protected override ArtworkWidgetSettings ToSchema5(ArtworkSettings settings) =>
        new(settings.CornerRadiusDip, settings.UseMediaPrimaryColor, settings.OpenSourceOnClick);
    protected override ArtworkSettings FromSchema5(ArtworkWidgetSettings settings) =>
        new(settings.CornerRadiusDip, settings.UseMediaPrimaryColor, settings.OpenSourceOnClick);
}

internal sealed class MediaTextSchema5Codec : Schema5ComponentCodec<MediaTextSettings, MediaTextWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.MediaText;
    protected override MediaTextWidgetSettings ToSchema5(MediaTextSettings settings) =>
        new(ToLayout(settings.TextKind), settings.EnableMarquee, settings.FontSizeDip, settings.MaxLines);
    protected override MediaTextSettings FromSchema5(MediaTextWidgetSettings settings) =>
        new(ToComponent(settings.TextKind), settings.EnableMarquee, settings.FontSizeDip, settings.MaxLines);

    private static MediaTextKind ToLayout(MediaTextContentKind kind) => kind switch
    {
        MediaTextContentKind.Artist => MediaTextKind.Artist,
        MediaTextContentKind.TitleAndArtist => MediaTextKind.TitleAndArtist,
        _ => MediaTextKind.Title
    };

    private static MediaTextContentKind ToComponent(MediaTextKind kind) => kind switch
    {
        MediaTextKind.Artist => MediaTextContentKind.Artist,
        MediaTextKind.TitleAndArtist => MediaTextContentKind.TitleAndArtist,
        _ => MediaTextContentKind.Title
    };
}

internal sealed class MediaSourceSchema5Codec : Schema5ComponentCodec<MediaSourceSettings, MediaTextWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.MediaSource;
    protected override MediaTextWidgetSettings ToSchema5(MediaSourceSettings settings) =>
        new(MediaTextKind.Source, false, settings.FontSizeDip, settings.MaxLines);
    protected override MediaSourceSettings FromSchema5(MediaTextWidgetSettings settings) =>
        new(settings.FontSizeDip, settings.MaxLines);
}
