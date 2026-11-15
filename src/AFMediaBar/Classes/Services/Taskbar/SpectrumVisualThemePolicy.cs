namespace AFMediaBar.Classes.Services;

/// <summary>频谱在当前环境下的透明度、遮罩和峰值帽策略。 / Opacity, mask, and peak-marker policy for the current environment.</summary>
public readonly record struct SpectrumVisualTheme(
    bool UseMasks,
    bool ShowPeakMarkers,
    double BarBaseOpacity,
    double DotOpacity,
    double UnlitPixelOpacity)
{
    /// <summary>柱体透明度：正常档随电平从基础值升到 1。 / Bar opacity: in the normal tier it rises from the base value to one with the level.</summary>
    public double ResolveBarOpacity(float level)
    {
        var normalized = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        return BarBaseOpacity + (1 - BarBaseOpacity) * normalized;
    }
}

/// <summary>
/// 高对比度和低性能只改变频谱的装饰层，不改变几何。高对比度使用系统画刷，因此不额外压低主体透明度；
/// 低性能关闭渐变遮罩和峰值帽，保留可读的实心形状。
/// High contrast and low performance only change the spectrum's decorative layer, never its geometry. High contrast uses the
/// system brush, so the main shapes are not additionally dimmed; low performance disables gradient masks and peak caps while
/// keeping the solid shapes readable.
/// </summary>
public static class SpectrumVisualThemePolicy
{
    /// <summary>按当前环境解析频谱视觉主题。 / Resolves the spectrum visual theme for the current environment.</summary>
    /// <param name="highContrast">是否启用高对比度。/ Whether high contrast is enabled.</param>
    /// <param name="useDecorativeEffects">是否允许装饰性效果。/ Whether decorative effects are allowed.</param>
    public static SpectrumVisualTheme Resolve(bool highContrast, bool useDecorativeEffects)
    {
        if (highContrast)
            return new SpectrumVisualTheme(false, false, 1, 1, 0);

        return useDecorativeEffects
            ? new SpectrumVisualTheme(true, true, 0.55, 0.85, 0.14)
            : new SpectrumVisualTheme(false, false, 1, 1, 0.14);
    }
}
