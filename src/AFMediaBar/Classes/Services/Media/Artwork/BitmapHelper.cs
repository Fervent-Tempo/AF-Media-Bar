// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Buffers;
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
    /// <summary>取色时的最大采样边长（像素）：64 px 足够得到主色分布，而缓冲只有 16 KB。<br/>
    /// Maximum sampling edge length in pixels: 64 px is enough for a dominant-colour distribution and keeps the buffer at 16 KB.</summary>
    private const int MaximumSampleSize = 64;

    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<(int Hash, int ColorCount, int Iterations, bool DarkTheme), List<SolidColorBrush>> _dominantColorsCache = new(5);

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
    /// 丢弃主色缓存，但保留最近一次的结果。
    /// Drops the dominant-color cache while keeping the most recent result.
    ///
    /// `SavedDominantColors` 是界面此刻正在用的画刷（媒体栏的强调色取自它），把它清掉界面就会当场失去颜色；缓存里存的只是"以前某张封面算过什么"，
    /// 那份随时可以重算，而且只在切换曲目时才被读到。
    /// `SavedDominantColors` is what the interface is painting with right now — the media bar takes its accent from it — so clearing it would strip the
    /// colors on the spot. What the cache holds is only "what some earlier cover computed to", which can always be recomputed and is read only when
    /// the track changes.
    /// </summary>
    internal static void ClearCache() => _dominantColorsCache.Clear();

    /// <summary>
    /// 从最近一次 ArtworkLoader.GetThumbnail 缓存的位图提取主色；单色使用直方图峰值，多色使用 K-means。
    /// Gets dominant colors from the bitmap cached by the latest ArtworkLoader.GetThumbnail call;
    /// uses a histogram peak for one color and K-means for multiple colors.
    /// </summary>
    /// <param name="colorCount">需要的颜色数量。/ Number of colors needed.</param>
    /// <param name="maxIterations">K-means 最大迭代次数。/ Maximum K-means iterations.</param>
    /// <returns>缓存位图对应的主色画刷列表。/ Dominant-color brushes for the cached bitmap.</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int hashCode, int maxIterations = 15)
    {
        if (!UseAlbumArtAsAccentColor || hashCode == 0)
        {
            // 强调色回退统一取自应用调色板；取不到时退回系统高亮色，绝不会是空画刷。
            // 旧实现取 MicaWPF 的强调色键，而该键在本项目中并不存在，命中这条回退路径时会直接空引用。
            // The accent fallback comes from the application palette and degrades to the system highlight color, so it can
            // never be null. The previous implementation read MicaWPF accent keys that do not resolve in this project and
            // would throw a null reference whenever this fallback ran.
            var accent = ResolveAccentFallback("AfAccentHoverBrush");
            var accent2 = ResolveAccentFallback("AfAccentBrush");

            _currentDominantColors = [accent, accent2];
            return _currentDominantColors;
        }

        // start timing
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        try
        {
            var darkTheme = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;
            var cacheKey = (hashCode, colorCount, maxIterations, darkTheme);
            if (_dominantColorsCache.TryGetValue(cacheKey, out var cachedColors) && cachedColors != null)
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

            // 取色只需要大致的颜色分布：先把封面缩到 ≤64 px 再采样。临时缓冲因此从 256 KB（会直接进大对象堆）降到 16 KB，
            // 并且从 <see cref="ArrayPool{T}"/> 借还——播放时每切一首歌都要取一次色，这条路径原本会给 LOH 持续制造垃圾。
            // Sampling only needs the rough colour distribution: the cover is scaled to at most 64 px first. That drops the temporary
            // buffer from 256 KB, which lands straight on the large object heap, to 16 KB, and it is rented from an ArrayPool instead:
            // this path runs once per track, and it used to feed the LOH continuously.
            var convertSource = CreateSampledSource(sourceBitmap, MaximumSampleSize);
            var formattedBitmap = new FormatConvertedBitmap(convertSource, PixelFormats.Bgra32, null, 0);

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;
            var pixels = ArrayPool<byte>.Shared.Rent(height * stride);
            IReadOnlyList<Color> result;
            try
            {
                formattedBitmap.CopyPixels(pixels, stride, 0);
                result = DominantColorCalculator.Calculate(
                    pixels,
                    width,
                    height,
                    colorCount,
                    maxIterations,
                    darkTheme);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }

            // convert to brushes
            var brushes = result.Select(c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze(); // makes it immutable & thread-safe
                return brush;
            }).ToList();

            _currentDominantColors = brushes;

            _dominantColorsCache.Set(cacheKey, _currentDominantColors);

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

    /// <summary>
    /// 取色前把封面缩到不超过给定边长；已经足够小时直接返回原图。
    /// Scales the cover down to at most the given edge length before sampling, returning the original when it already is small enough.
    /// </summary>
    /// <param name="source">缓存的封面位图（已冻结）。/ The cached cover bitmap, already frozen.</param>
    /// <param name="maximumSize">采样用的最大边长（像素）。/ Maximum edge length in pixels for sampling.</param>
    private static BitmapSource CreateSampledSource(BitmapSource source, int maximumSize)
    {
        var longestEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longestEdge <= maximumSize)
        {
            return source;
        }

        var scale = maximumSize / (double)longestEdge;
        var transformed = new TransformedBitmap();
        transformed.BeginInit();
        transformed.Source = source;
        transformed.Transform = new ScaleTransform(scale, scale);
        transformed.EndInit();
        transformed.Freeze();
        return transformed;
    }

    /// <summary>
    /// 读取应用统一调色板中的强调色画刷，缺失时退回系统高亮色，并保证返回冻结的非空画刷。
    /// Reads an accent brush from the application palette, degrading to the system highlight color and always returning a
    /// frozen, non-null brush.
    /// </summary>
    private static SolidColorBrush ResolveAccentFallback(string resourceKey)
    {
        var brush = Application.Current?.TryFindResource(resourceKey) as SolidColorBrush
            ?? new SolidColorBrush(SystemColors.HighlightColor);
        if (!brush.IsFrozen)
        {
            brush = brush.Clone();
            brush.Freeze();
        }

        return brush;
    }
}
