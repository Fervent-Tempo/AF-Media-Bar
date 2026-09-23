using System.Windows;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>以岛体左上角为原点的连续几何。/ Continuous geometry relative to the island's top-left corner.</summary>
public readonly record struct IslandGeometry(
    double Width,
    double Height,
    double Radius,
    Rect Artwork,
    Point ActivityOrigin,
    double DetailOpacity,
    double MediaOpacity);

/// <summary>
/// 在空闲胶囊、媒体胶囊和展开卡片间变形，不创建布局对象或动画。
/// Morphs between idle, compact media, and expanded card without creating layout objects or animations.
/// </summary>
public static class DynamicIslandGeometry
{
    /// <summary>
    /// 保留小幅弹簧过冲，但限制尺寸和透明度；详情在有足够高度后才淡入。
    /// Preserves slight spring overshoot while bounding size and opacity; details fade only after enough height opens up.
    /// </summary>
    public static IslandGeometry Calculate(double mediaProgress, double expansionProgress)
    {
        var media = BoundedProgress(mediaProgress, 1);
        var mediaShape = BoundedProgress(mediaProgress, 1.06);
        var expansion = BoundedProgress(expansionProgress, 1.06) * media;
        var width = 126 + 104 * mediaShape + 141 * expansion;
        var height = 37 + 123 * expansion;
        var radius = Math.Min(18.5 + 19.5 * expansion, Math.Min(width, height) / 2);
        var artworkSize = 24 + 30 * expansion;
        var artwork = new Rect(
            10.5 + 9.5 * expansion,
            6.5 + 13.5 * expansion,
            artworkSize,
            artworkSize);
        var activity = new Point(width - (38 + 4 * expansion), 6.5 + 21.5 * expansion);
        var mediaOpacity = SmoothStep(media);
        var detailOpacity = SmoothStep(Math.Clamp((expansion - 0.4) / 0.5, 0, 1)) * mediaOpacity;

        return new IslandGeometry(width, height, radius, artwork, activity, detailOpacity, mediaOpacity);
    }

    private static double BoundedProgress(double value, double maximum)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, maximum) : 0;
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3 - 2 * value);
    }
}
