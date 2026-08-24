using AFMediaBar.Abstractions;
using Windows.Graphics.Imaging;

namespace AFMediaBar.WinUI;

internal sealed class WinUiArtworkImage(SoftwareBitmap bitmap) : IArtworkImage, IDisposable
{
    internal SoftwareBitmap Bitmap { get; } = bitmap;

    public void Dispose() => Bitmap.Dispose();
}
