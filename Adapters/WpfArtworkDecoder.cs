using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using AFMediaBar.Abstractions;

namespace AFMediaBar.Adapters;

internal sealed class WpfArtworkDecoder : IArtworkDecoder
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

        memoryStream.Position = 0;
        if (!memoryStream.TryGetBuffer(out var buffer))
        {
            return default;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            buffer.AsSpan(0, checked((int)memoryStream.Length))));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = DecodeWidth;
        bitmap.StreamSource = memoryStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new ArtworkDecodeResult(new WpfArtworkImage(bitmap), fingerprint);
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
                var bytesToRead = Math.Min(buffer.Length, MaximumBytes - totalBytes + 1);
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
