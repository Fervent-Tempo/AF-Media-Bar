using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 纯布局尺寸计算：将组件尺寸、间距缩放和文本测量转换为可应用的布局。
/// Pure layout sizing: converts component sizes, spacing scale and text measurement into an applied layout.
/// </summary>
public static class LayoutSizeCalculator
{
    public const double MinimumChangeDip = 2;

    /// <summary>
    /// 根据内容宽度计算目标画布尺寸。长度缩放只影响组件间距，粗细缩放只影响组件自身尺寸。
    /// Calculates the target canvas size. Length scale affects gaps only; thickness scale affects components only.
    /// </summary>
    public static MediaBarSizeRequest Calculate(
        LayoutSchema layout,
        double spacingScale,
        double thicknessScale,
        double measuredTextWidthDip,
        double maximumPrimaryLengthDip,
        string contentFingerprint,
        bool isResetToPreset = false)
    {
        spacingScale = Math.Clamp(spacingScale, 0.7, 1.25);
        thicknessScale = Math.Clamp(thicknessScale, 0.7, 1.25);

        var scaledComponents = layout.Components
            .Where(component => component.IsVisible)
            .Select(component => component with
            {
                Bounds = new ComponentBounds(
                    component.Bounds.X * thicknessScale,
                    component.Bounds.Y * thicknessScale,
                    component.Bounds.Width * thicknessScale,
                    component.Bounds.Height * thicknessScale)
            })
            .ToList();

        var isVertical = layout.Orientation == LayoutOrientation.Vertical;
        var basePrimary = isVertical ? layout.Canvas.Height : layout.Canvas.Width;
        var contentComponent = scaledComponents.FirstOrDefault(component => component.AutoSizePrimary)
            ?? scaledComponents.FirstOrDefault(component => component.Id == "song-info");
        var measuredPrimary = Math.Max(0, measuredTextWidthDip);
        var contentBasePrimary = contentComponent is null
            ? basePrimary
            : isVertical ? contentComponent.Bounds.Height : contentComponent.Bounds.Width;
        var contentDelta = contentComponent is null ? 0 : measuredPrimary - contentBasePrimary;

        var scaledGapDelta = layout.Components
            .Where(component => component.IsVisible)
            .Sum(component => component.SpacingAfter * (spacingScale - 1));
        var targetPrimary = Math.Max(1, basePrimary * thicknessScale + contentDelta + scaledGapDelta);
        if (maximumPrimaryLengthDip > 0)
            targetPrimary = Math.Min(targetPrimary, maximumPrimaryLengthDip);

        var canvas = layout.Canvas with
        {
            Width = isVertical ? layout.Canvas.Width * thicknessScale : targetPrimary,
            Height = isVertical ? targetPrimary : layout.Canvas.Height * thicknessScale,
            CornerRadius = layout.Canvas.CornerRadius * thicknessScale,
            Border = layout.Canvas.Border is null
                ? null
                : layout.Canvas.Border with { Thickness = layout.Canvas.Border.Thickness * thicknessScale },
            Effects = layout.Canvas.Effects is null
                ? null
                : layout.Canvas.Effects with { Blur = layout.Canvas.Effects.Blur * thicknessScale }
        };

        return new MediaBarSizeRequest(
            layout.Orientation,
            canvas.Width,
            canvas.Height,
            contentFingerprint,
            isResetToPreset);
    }

    /// <summary>将当前布局调整到目标主轴长度。/ Resizes an already scaled layout to a target primary length.</summary>
    public static LayoutSchema ResizePrimary(LayoutSchema layout, double primaryLength)
    {
        primaryLength = Math.Max(1, primaryLength);
        var isVertical = layout.Orientation == LayoutOrientation.Vertical;
        var oldPrimary = isVertical ? layout.Canvas.Height : layout.Canvas.Width;
        var delta = primaryLength - oldPrimary;
        if (Math.Abs(delta) < 0.01)
            return layout;

        var autoComponent = layout.Components.FirstOrDefault(component => component.IsVisible && component.AutoSizePrimary)
            ?? layout.Components.FirstOrDefault(component => component.Id == "song-info");
        var components = layout.Components.ToList();
        if (autoComponent is not null)
        {
            var index = components.FindIndex(component => component.Id == autoComponent.Id);
            var bounds = autoComponent.Bounds;
            var resizedPrimary = isVertical
                ? Math.Max(0, bounds.Height + delta)
                : Math.Max(0, bounds.Width + delta);
            var appliedDelta = resizedPrimary - (isVertical ? bounds.Height : bounds.Width);
            components[index] = autoComponent with
            {
                Bounds = isVertical
                    ? bounds with { Height = resizedPrimary }
                    : bounds with { Width = resizedPrimary }
            };

            // 后续组件随自动尺寸组件移动，保持组件间距和排列顺序。
            // Shift following components with the auto-sized component to preserve ordering and gaps.
            if (Math.Abs(appliedDelta) > 0.01)
            {
                for (var following = index + 1; following < components.Count; following++)
                {
                    var followingBounds = components[following].Bounds;
                    components[following] = components[following] with
                    {
                        Bounds = isVertical
                            ? followingBounds with { Y = followingBounds.Y + appliedDelta }
                            : followingBounds with { X = followingBounds.X + appliedDelta }
                    };
                }
            }
        }

        var canvas = layout.Canvas with
        {
            Width = isVertical ? layout.Canvas.Width : primaryLength,
            Height = isVertical ? primaryLength : layout.Canvas.Height
        };
        return layout with { Canvas = canvas, Components = components };
    }
}
