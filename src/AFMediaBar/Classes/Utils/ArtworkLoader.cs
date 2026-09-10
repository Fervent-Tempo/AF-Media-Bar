// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Net.Http;
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
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
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
    /// <returns>冻结的 WPF 位图；输入无效时返回 null。/ Frozen WPF bitmap, or null for invalid input.</returns>
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

        BitmapImage image = new();
        using (var imageStream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = maxThumbnailSize;
            image.StreamSource = imageStream;
            image.EndInit();
        }

        image.Freeze();
        ThumbnailCache.Set(hashCode, image);
        SetCurrentHash(hashCode);
        return image;
    }

    /// <summary>
    /// 从 URL 下载并解码封面；失败或非 2xx 时返回 null，取消请求继续向上传播。
    /// Downloads and decodes artwork from a URL; returns null on failure or non-2xx and propagates cancellation.
    /// </summary>
    /// <param name="url">封面 URL。/ Artwork URL.</param>
    /// <param name="cancellationToken">取消标记。/ Cancellation token.</param>
    /// <returns>冻结的 WPF 位图；下载或解码失败时返回 null。/ Frozen WPF bitmap, or null when download or decoding fails.</returns>
    internal static async Task<BitmapImage?> GetImageFromUrlAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        try
        {
            using var response = await HttpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = MaxThumbnailSize;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

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
