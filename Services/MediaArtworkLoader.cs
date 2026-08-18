using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace AFMediaBar.Services;

/// <summary>
/// 限量读取 GSMTC 封面流、计算内容指纹并生成可跨线程使用的冻结位图。
/// Reads bounded GSMTC artwork streams, fingerprints their content, and creates frozen bitmaps safe for WPF use.
/// </summary>
internal static class MediaArtworkLoader
{
    private const int DecodeWidth = 96;
    private const int MaximumBytes = 16 * 1024 * 1024;

    internal static async Task<MediaArtworkLoadResult> LoadAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return default;
        }

        using var randomAccessStream = await thumbnail.OpenReadAsync();
        if (randomAccessStream.Size > MaximumBytes)
        {
            return default;
        }

        using var sourceStream = randomAccessStream.AsStreamForRead();
        // 首次回调可能提供不可定位的 WinRT 流；限量缓冲后再交给 WPF 解码。
        // The first callback may expose a non-seekable WinRT stream; buffer it for WPF.
        using var memoryStream = new MemoryStream(checked((int)randomAccessStream.Size));
        if (!await CopyBoundedAsync(sourceStream, memoryStream, cancellationToken) ||
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
        return new MediaArtworkLoadResult(bitmap, fingerprint);
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

internal readonly record struct MediaArtworkLoadResult(
    BitmapImage? Artwork,
    string? Fingerprint);
