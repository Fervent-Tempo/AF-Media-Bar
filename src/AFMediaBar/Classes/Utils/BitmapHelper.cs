// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 将缓存封面转换为主题主色画刷，并保留最近一次呈现结果。
/// Converts cached artwork into theme-aware dominant-color brushes and retains the latest presentation result.
/// </summary>
internal static class BitmapHelper
{
    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<int, List<SolidColorBrush>> _dominantColorsCache = new(5);

    // current or latest dominant colors
    private static List<SolidColorBrush>? _currentDominantColors;

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
    /// 从最近一次 ArtworkLoader.GetThumbnail 缓存的位图提取主色；单色使用直方图峰值，多色使用 K-means。
    /// Gets dominant colors from the bitmap cached by the latest ArtworkLoader.GetThumbnail call;
    /// uses a histogram peak for one color and K-means for multiple colors.
    /// </summary>
    /// <param name="colorCount">需要的颜色数量。/ Number of colors needed.</param>
    /// <param name="maxIterations">K-means 最大迭代次数。/ Maximum K-means iterations.</param>
    /// <returns>缓存位图对应的主色画刷列表。/ Dominant-color brushes for the cached bitmap.</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int maxIterations = 15)
    {
        int hashCode = ArtworkLoader.CurrentThumbnailHash;

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
            if (!ArtworkLoader.TryGetCachedThumbnail(hashCode, out var sourceBitmap) || sourceBitmap == null)
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
