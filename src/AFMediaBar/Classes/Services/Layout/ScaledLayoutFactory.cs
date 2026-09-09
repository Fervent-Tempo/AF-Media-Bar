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
    public static LayoutSchema Create(LayoutSchema source, double lengthScale, double thicknessScale)
    {
        lengthScale = Math.Clamp(lengthScale, 0.7, 1.25);
        thicknessScale = Math.Clamp(thicknessScale, 0.7, 1.25);
        var isVertical = source.Orientation == LayoutOrientation.Vertical;

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
            Properties = ScaleVisualProperties(component.Properties, thicknessScale)
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
        double thicknessScale)
    {
        var result = new Dictionary<string, object>(properties);
        foreach (var key in new[] { "cornerRadius", "placeholderIconSize", "titleFontSize", "artistFontSize" })
        {
            if (result.TryGetValue(key, out var value) && value is double number)
                result[key] = number * thicknessScale;
        }

        return result;
    }
}
