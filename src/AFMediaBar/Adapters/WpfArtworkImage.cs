using System.Windows.Media;
using AFMediaBar.Abstractions;

namespace AFMediaBar.Adapters;

internal sealed record WpfArtworkImage(ImageSource Source) : IArtworkImage;

internal static class WpfArtworkImageExtensions
{
    internal static ImageSource? AsImageSource(this IArtworkImage? artwork) =>
        (artwork as WpfArtworkImage)?.Source;
}
