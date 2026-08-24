using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;
using AFMediaBar.Abstractions;
using Windows.Graphics.Imaging;

namespace AFMediaBar.WinUI;

internal sealed class WinUiArtworkDecoder : IArtworkDecoder
{
    private const int DecodeWidth = 96;
    private const int MaximumBytes = 16 * 1024 * 1024;

    public async Task<ArtworkDecodeResult> DecodeAsync(
        Stream source,
        long? sourceLength,
        CancellationToken cancellationToken)
    {
        if (sourceLength is < 1 or > MaximumBytes)
        {
            return default;
        }

        var initialCapacity = sourceLength is > 0 and <= MaximumBytes
            ? checked((int)sourceLength.Value)
            : 0;
        using var memoryStream = new MemoryStream(initialCapacity);
        if (!await CopyBoundedAsync(source, memoryStream, cancellationToken) ||
            memoryStream.Length == 0)
        {
            return default;
        }

        if (!memoryStream.TryGetBuffer(out var buffer))
        {
            return default;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            buffer.AsSpan(0, checked((int)memoryStream.Length))));
        memoryStream.Position = 0;
        using var decoderStream = memoryStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(decoderStream);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
        {
            return default;
        }

        var scaledWidth = Math.Min((uint)DecodeWidth, decoder.PixelWidth);
        var scaledHeight = Math.Max(
            1u,
            (uint)Math.Round(
                decoder.PixelHeight * scaledWidth / (double)decoder.PixelWidth));
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight
            },
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return new ArtworkDecodeResult(
            new WinUiArtworkImage(softwareBitmap),
            fingerprint);
    }

    private static async Task<bool> CopyBoundedAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var totalBytes = 0;
            while (true)
            {
                var bytesToRead = Math.Min(
                    buffer.Length,
                    MaximumBytes - totalBytes + 1);
                var bytesRead = await source.ReadAsync(
                    buffer.AsMemory(0, bytesToRead),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    return true;
                }

                totalBytes += bytesRead;
                if (totalBytes > MaximumBytes)
                {
                    return false;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
