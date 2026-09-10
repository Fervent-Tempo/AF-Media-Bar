// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 提供媒体封面加载、缓存、裁剪和主色提取的兼容入口。
/// Compatibility entry point for artwork loading, caching, cropping, and dominant-color extraction.
/// </summary>
internal static class BitmapHelper
{
    // LRU cache implementation for caching thumbnails and their dominant colors
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lruList = [];
        private readonly object _sync = new();

        private sealed class CacheEntry(TKey key, TValue value)
        {
            public TKey Key { get; } = key;
            public TValue Value { get; set; } = value;
        }

        public LruCache(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    existing.Value.Value = value;
                    _lruList.Remove(existing);
                    _lruList.AddFirst(existing);
                    return;
                }

                var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
                _lruList.AddFirst(node);
                _map[key] = node;

                if (_map.Count <= _capacity)
                    return;

                var leastRecent = _lruList.Last;
                if (leastRecent == null)
                    return;

                _lruList.RemoveLast();
                _map.Remove(leastRecent.Value.Key);
            }
        }
    }

    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private const int _cacheEntryLimit = 5;

    // cached thumbnails to prevent reprocessing
    private static readonly LruCache<int, BitmapImage> _thumbnailCache = new(_cacheEntryLimit);

    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<int, List<SolidColorBrush>> _dominantColorsCache = new(_cacheEntryLimit);

    private static int _currentHashCode = 0;
    private static readonly AsyncLocal<int> _currentHashCodeContext = new();

    // current or latest dominant colors
    private static List<SolidColorBrush>? _currentDominantColors;

    // cover URL downloads (memory player artwork), short timeout so a hung cover host cannot stall the poll
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// 是否使用专辑封面提取强调色（后续可接入设置，默认开启）。
    /// Whether to extract accent colors from album artwork (enabled by default).
    /// </summary>
    public static bool UseAlbumArtAsAccentColor { get; set; } = true;

    /// <summary>
    /// 获取最近一次计算出的主色画刷。
    /// Gets the dominant-color brushes calculated most recently.
    /// </summary>
    public static List<SolidColorBrush> SavedDominantColors
    {
        get => _currentDominantColors ??= [];
    }

    /// <summary>
    /// 读取缩略图内容并计算稳定哈希，用于缓存键。
    /// Reads thumbnail content and computes a stable hash for cache keys.
    /// </summary>
    /// <param name="thumbnail">缩略图流引用。/ Thumbnail stream reference.</param>
    /// <returns>内容哈希；读取失败时返回对象哈希。/ Content hash, or the object hash when reading fails.</returns>
    public static int GetStableThumbnailHash(IRandomAccessStreamReference thumbnail)
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

    internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference? thumbnail,
        int maxThumbnailSize = _maxThumbnailSize)
    {
        if (thumbnail == null)
            return null;

        int hashCode = GetStableThumbnailHash(thumbnail);

        if (hashCode == 0)
            return null;

        if (_thumbnailCache.TryGetValue(hashCode, out var cachedImage) && cachedImage != null)
        {
            _currentHashCode = hashCode;
            _currentHashCodeContext.Value = hashCode;
            return cachedImage;
        }

        BitmapImage image = new();
        using (var imageStream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())
        {
            // initialize the BitmapImage
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = maxThumbnailSize;
            image.StreamSource = imageStream;
            image.EndInit();
        }

        image.Freeze();

        // add bitmap to thumbnail cache with empty brush
        _thumbnailCache.Set(hashCode, image);

        _currentHashCode = hashCode;
        _currentHashCodeContext.Value = hashCode;
        return image;
    }

    /// <summary>
    /// 从 URL 下载并解码封面（内存播放器路径使用）；失败或非 2xx 时返回 null。
    /// Downloads and decodes artwork from a URL (memory player path); null on failure or non-2xx.
    /// </summary>
    internal static async Task<BitmapImage?> GetImageFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = _maxThumbnailSize;
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

    internal static CroppedBitmap? CropToSquare(BitmapImage? sourceImage)
    {
        if (sourceImage == null)
            return null;

        int size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
        int x = (sourceImage.PixelWidth - size) / 2;
        int y = (sourceImage.PixelHeight - size) / 2;

        var rect = new Int32Rect(x, y, size, size);

        // create a CroppedBitmap (this is a lightweight object)
        var croppedBitmap = new CroppedBitmap(sourceImage, rect);

        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    /// <summary>
    /// 从最近一次 GetThumbnail 缓存的位图提取主色；单色使用直方图峰值，多色使用 K-means。
    /// Gets dominant colors from the bitmap cached by the latest GetThumbnail call;
    /// uses a histogram peak for one color and K-means for multiple colors.
    /// </summary>
    /// <param name="colorCount">需要的颜色数量。/ Number of colors needed.</param>
    /// <param name="maxIterations">K-means 最大迭代次数。/ Maximum K-means iterations.</param>
    /// <returns>缓存位图对应的主色画刷列表。/ Dominant-color brushes for the cached bitmap.</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int maxIterations = 15)
    {
        int hashCode = _currentHashCodeContext.Value != 0 ? _currentHashCodeContext.Value : _currentHashCode;

        if (!UseAlbumArtAsAccentColor || hashCode == 0)
        {
            // control color (buttons, etc.)
            var accent =
                (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorSecondary");
            if (!accent.IsFrozen)
                accent = accent.Clone();
            accent.Freeze();

            // accent color (for non-control elements)
            var accent2 =
                (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            if (!accent2.IsFrozen)
                accent2 = accent2.Clone();
            accent2.Freeze();

            _currentDominantColors = [accent, accent2];
            return _currentDominantColors;
        }

        // start timing
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        try
        {
            // check if we've already calculated colors for this thumbnail by checking
            // the current hash with cache (dumb method because we're assuming it's always the latest)
            if (_dominantColorsCache.TryGetValue(hashCode, out var cachedColors) && cachedColors != null)
            {
                _currentDominantColors = cachedColors;
                return _currentDominantColors;
            }

            // convert BitmapImage to BGRA byte array
            if (!_thumbnailCache.TryGetValue(hashCode, out var sourceBitmap) || sourceBitmap == null)
            {
                Debug.WriteLine("[BitmapHelper] Thumbnail cache miss while extracting dominant colors");
                return _currentDominantColors ?? [];
            }

            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = sourceBitmap;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;

            byte[] pixels = new byte[height * stride];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            var result = DominantColorCalculator.Calculate(
                pixels,
                width,
                height,
                colorCount,
                maxIterations,
                ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark);

            // convert to brushes
            var brushes = result.Select(c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze(); // makes it immutable & thread-safe
                return brush;
            }).ToList();

            _currentDominantColors = brushes;

            // save brushes to cache with current hash as key
            _dominantColorsCache.Set(hashCode, _currentDominantColors);

#if DEBUG
            stopwatch.Stop();
            Debug.WriteLine($"Dominant color extraction took {stopwatch.Elapsed.TotalMilliseconds} ms");
#endif
            return _currentDominantColors;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting dominant colors: {ex}");
            return [];
        }
    }

}
