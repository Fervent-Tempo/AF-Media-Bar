using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 根据预设生成运行时缩放布局，仅包含可测试的布局数据变换。
/// Creates runtime scaled layouts from presets using testable data-only transformations.
/// </summary>
public static class ScaledLayoutFactory
{
    /// <summary>
    /// 应用主轴间距和横轴粗细缩放，并重新计算相邻组件位置。
    /// Applies primary spacing and cross-axis thickness scaling, recalculating adjacent component positions.
    /// </summary>
    /// <param name="source">基础布局预设 / Base layout preset</param>
    /// <param name="lengthScale">主轴间距缩放系数 / Primary-axis spacing scale</param>
    /// <param name="thicknessScale">横轴粗细缩放系数 / Cross-axis thickness scale</param>
    /// <param name="mediaFontScale">媒体文字字号缩放系数；只影响文字字号，不改变组件尺寸。/ Media-text font-size scale; affects text sizes only, not component bounds.</param>
    /// <param name="crossAxisDip">宿主当前可用横轴；仅夹取运行时几何，不写设置。</param>
    /// <param name="preserveMediaFontSize">让任务栏文字独立于粗细缩放。</param>
    public static LayoutSchema Create(LayoutSchema source, double lengthScale, double thicknessScale, double mediaFontScale = 1, double? crossAxisDip = null, bool preserveMediaFontSize = false)
    {
        lengthScale = Math.Clamp(lengthScale, 0.7, 1.25);
        thicknessScale = Math.Clamp(thicknessScale, 0.7, 1.25);
        mediaFontScale = Math.Clamp(mediaFontScale, 0.5, 2);
        var isVertical = source.Orientation == LayoutOrientation.Vertical;
        var baseCross = isVertical ? source.Canvas.Width : source.Canvas.Height;
        if (crossAxisDip is { } cross && double.IsFinite(cross) && cross > 0 && baseCross > 0)
            thicknessScale = Math.Min(thicknessScale, cross / baseCross);

        var components = source.Components.Select(component => new ComponentConfig
        {
            Id = component.Id,
            Type = component.Type,
            IsVisible = component.IsVisible,
            SpacingAfter = component.SpacingAfter,
            AutoSizePrimary = component.AutoSizePrimary,
            Bounds = new ComponentBounds(
                component.Bounds.X * thicknessScale,
                component.Bounds.Y * thicknessScale,
                component.Bounds.Width * thicknessScale,
                component.Bounds.Height * thicknessScale),
            Properties = ScaleVisualProperties(component.Properties, thicknessScale, mediaFontScale, preserveMediaFontSize)
        }).ToList();

        var primaryGapDelta = 0d;
        ComponentConfig? previousVisible = null;
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            var bounds = component.Bounds;
            if (previousVisible is not null)
            {
                var previousBounds = previousVisible.Bounds;
                var desiredStart = isVertical
                    ? previousBounds.Y + previousBounds.Height + previousVisible.SpacingAfter * lengthScale
                    : previousBounds.X + previousBounds.Width + previousVisible.SpacingAfter * lengthScale;
                bounds = isVertical
                    ? bounds with { Y = desiredStart }
                    : bounds with { X = desiredStart };
            }

            components[index] = component with { Bounds = bounds };

            if (component.IsVisible)
            {
                primaryGapDelta += component.SpacingAfter * (lengthScale - 1);
                previousVisible = components[index];
            }
        }

        var canvas = source.Canvas with
        {
            Width = source.Canvas.Width * thicknessScale + (isVertical ? 0 : primaryGapDelta),
            Height = source.Canvas.Height * thicknessScale + (isVertical ? primaryGapDelta : 0),
            CornerRadius = source.Canvas.CornerRadius * thicknessScale,
            Border = source.Canvas.Border is null
                ? null
                : source.Canvas.Border with { Thickness = source.Canvas.Border.Thickness * thicknessScale },
            Effects = source.Canvas.Effects is null
                ? null
                : source.Canvas.Effects with { Blur = source.Canvas.Effects.Blur * thicknessScale }
        };

        return source with { Canvas = canvas, Components = components };
    }

    private static Dictionary<string, object> ScaleVisualProperties(
        IReadOnlyDictionary<string, object> properties,
        double thicknessScale,
        double mediaFontScale, bool preserveMediaFontSize)
    {
        var result = new Dictionary<string, object>(properties);
        foreach (var key in new[] { "cornerRadius", "placeholderIconSize" })
        {
            if (result.TryGetValue(key, out var value) && value is double number)
                result[key] = number * thicknessScale;
        }

        // 任务栏保持用户字号；其他宿主保留原有粗细缩放语义，渲染引擎只读取结果。
        foreach (var key in new[] { "titleFontSize", "artistFontSize", "lyricsFontSize" })
        {
            if (result.TryGetValue(key, out var value) && value is double number)
                result[key] = number * (preserveMediaFontSize ? 1 : thicknessScale) * mediaFontScale;
        }

        return result;
    }
}
