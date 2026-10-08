// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 负责媒体封面的内容哈希、加载、解码、裁剪和缩略图缓存。
/// Handles content hashing, loading, decoding, cropping, and thumbnail caching for media artwork.
/// </summary>
internal static class ArtworkLoader
{
    private const int MaxThumbnailSize = 256;
    private const int CacheEntryLimit = 5;
    private static readonly LruCache<int, BitmapImage> ThumbnailCache = new(CacheEntryLimit);
    private static readonly AsyncLocal<int> CurrentHashCodeContext = new();
    private static int _currentHashCode;

    /// <summary>
    /// 获取当前异步上下文最近加载的缩略图哈希；上下文没有值时使用进程内最近值。
    /// Gets the most recently loaded thumbnail hash for the current async context, falling back to the process-wide latest value.
    /// </summary>
    internal static int CurrentThumbnailHash =>
        CurrentHashCodeContext.Value != 0 ? CurrentHashCodeContext.Value : _currentHashCode;

    /// <summary>
    /// 读取缩略图内容并计算稳定哈希，用于缓存键。
    /// Reads thumbnail content and computes a stable hash for cache keys.
    /// </summary>
    /// <param name="thumbnail">缩略图流引用。/ Thumbnail stream reference.</param>
    /// <returns>内容哈希；读取失败时返回对象哈希。/ Content hash, or the object hash when reading fails.</returns>
    internal static int GetStableThumbnailHash(IRandomAccessStreamReference thumbnail)
    {
        if (thumbnail == null)
            return 0;

        try
        {
            using Stream stream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead();
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToInt32(hashBytes, 0);
        }
        catch (Exception)
        {
            return thumbnail.GetHashCode();
        }
    }

    /// <summary>
    /// 同步加载并缓存 WinRT 缩略图，同时更新当前异步上下文的封面哈希。
    /// Synchronously loads and caches a WinRT thumbnail while updating the current async context artwork hash.
    /// </summary>
    /// <param name="thumbnail">缩略图流引用。/ Thumbnail stream reference.</param>
    /// <param name="maxThumbnailSize">最大解码宽度。/ Maximum decoded width.</param>
    /// <returns>冻结的 WPF 位图；输入无效、无法读取或解码时返回 null。</returns>
    internal static BitmapImage? GetThumbnail(
        IRandomAccessStreamReference? thumbnail,
        int maxThumbnailSize = MaxThumbnailSize)
    {
        if (thumbnail == null)
            return null;

        var hashCode = GetStableThumbnailHash(thumbnail);
        if (hashCode == 0)
            return null;

        if (ThumbnailCache.TryGetValue(hashCode, out var cachedImage) && cachedImage != null)
        {
            SetCurrentHash(hashCode);
            return cachedImage;
        }

        BitmapImage image;
        try
        {
            try
            {
                image = DecodeThumbnail(thumbnail, maxThumbnailSize, BitmapCreateOptions.None);
            }
            catch (IOException exception) when (exception.InnerException is COMException { HResult: unchecked((int)0x88982F72) })
            {
                // QQ Music can publish valid JPEG pixels with truncated EXIF metadata. WIC's color-context read
                // then fails with WINCODEC_ERR_STREAMREAD; retry without color management before dropping artwork.
                image = DecodeThumbnail(thumbnail, maxThumbnailSize, BitmapCreateOptions.IgnoreColorProfile);
            }
        }
        catch (Exception exception) when (exception is IOException or COMException or NotSupportedException or FileFormatException)
        {
            // Artwork is optional: a broken player stream or image/color profile must not discard the media snapshot.
            // Failed loads are not cached, so a later refresh can retry the same artwork.
            return null;
        }

        ThumbnailCache.Set(hashCode, image);
        SetCurrentHash(hashCode);
        return image;
    }

    private static BitmapImage DecodeThumbnail(
        IRandomAccessStreamReference thumbnail, int maxThumbnailSize, BitmapCreateOptions options)
    {
        using var imageStream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead();
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = options;
        image.DecodePixelWidth = maxThumbnailSize;
        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// 丢弃缩略图缓存；已经交给界面的位图不受影响（界面自己持有引用），丢掉的只是"下次不用重新解码"的那一份。
    /// Drops the thumbnail cache. Bitmaps already handed to the interface stay alive because it holds its own references; what is dropped is only the
    /// copy that saved a re-decode.
    /// </summary>
    internal static void ClearCache() => ThumbnailCache.Clear();

    /// <summary>
    /// 从缩略图中心裁剪正方形区域。
    /// Crops a centered square region from a thumbnail.
    /// </summary>
    /// <param name="sourceImage">源位图。/ Source bitmap.</param>
    /// <returns>冻结的正方形位图；源为空时返回 null。/ Frozen square bitmap, or null when the source is null.</returns>
    internal static CroppedBitmap? CropToSquare(BitmapImage? sourceImage)
    {
        if (sourceImage == null)
            return null;

        var size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
        var x = (sourceImage.PixelWidth - size) / 2;
        var y = (sourceImage.PixelHeight - size) / 2;
        var croppedBitmap = new CroppedBitmap(sourceImage, new Int32Rect(x, y, size, size));
        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    /// <summary>
    /// 尝试读取指定哈希对应的缓存缩略图。
    /// Attempts to read the cached thumbnail associated with the specified hash.
    /// </summary>
    internal static bool TryGetCachedThumbnail(int hashCode, out BitmapImage? image) =>
        ThumbnailCache.TryGetValue(hashCode, out image);

    private static void SetCurrentHash(int hashCode)
    {
        _currentHashCode = hashCode;
        CurrentHashCodeContext.Value = hashCode;
    }
}
