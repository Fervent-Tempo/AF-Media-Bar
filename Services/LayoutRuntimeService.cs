using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal readonly record struct LayoutSize(double WidthDip, double HeightDip);

/// <summary>
/// 选择当前窗口上下文的布局档案并提供只读组件查询；不创建视图、不持有系统资源。
/// Selects the profile for the current window context and exposes read-only widget queries without creating views or owning system resources.
/// </summary>
internal sealed class LayoutRuntimeService
{
    internal LayoutProfile ResolveProfile(
        LayoutDocument document,
        WindowHostMode hostMode,
        bool vertical)
    {
        var key = ResolveProfileKey(hostMode, vertical);
        return document.Get(key);
    }

    internal static LayoutProfileKey ResolveProfileKey(
        WindowHostMode hostMode,
        bool vertical)
    {
        return (hostMode, vertical) switch
        {
            (WindowHostMode.Taskbar, false) => LayoutProfileKey.TaskbarHorizontal,
            (WindowHostMode.Taskbar, true) => LayoutProfileKey.TaskbarVertical,
            (WindowHostMode.Floating, false) => LayoutProfileKey.FloatingHorizontal,
            (WindowHostMode.Floating, true) => LayoutProfileKey.FloatingVertical,
            _ => LayoutProfileKey.TaskbarHorizontal
        };
    }

    internal static bool ContainsWidget(LayoutProfile profile, string typeId)
    {
        return EnumerateWidgets(profile.Root)
            .Any(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 根据布局树估算宿主所需 DIP 尺寸；运行时窗口据此定位，避免自定义组件被旧固定尺寸裁剪。
    /// Estimates the host DIP size from the layout tree so custom widgets are not clipped by legacy fixed dimensions.
    /// </summary>
    internal static LayoutSize CalculateDesiredSize(LayoutProfile profile)
    {
        var orientation = profile.LayoutMode == PlayerLayoutMode.Vertical
            ? LayoutFlowOrientation.Vertical
            : LayoutFlowOrientation.Horizontal;
        var size = MeasureContainer(profile.Root, orientation, profile.Surface.GapDip);
        var lengthScale = Math.Clamp(profile.Surface.LengthScalePercent, 70, 125) / 100d;
        var thicknessScale = Math.Clamp(profile.Surface.ThicknessScalePercent, 70, 125) / 100d;
        var width = orientation == LayoutFlowOrientation.Vertical
            ? size.WidthDip * thicknessScale
            : size.WidthDip * lengthScale;
        var height = orientation == LayoutFlowOrientation.Vertical
            ? size.HeightDip * lengthScale
            : size.HeightDip * thicknessScale;
        if (profile.Surface.WidthDip is { } fixedWidth)
        {
            width = fixedWidth;
        }
        if (profile.Surface.HeightDip is { } fixedHeight)
        {
            height = fixedHeight;
        }

        return new LayoutSize(Math.Max(1, width), Math.Max(1, height));
    }

    private static LayoutSize MeasureContainer(
        LayoutContainerElement container,
        LayoutFlowOrientation fallbackOrientation,
        int gap)
    {
        var orientation = container.Orientation == LayoutFlowOrientation.Automatic
            ? fallbackOrientation
            : container.Orientation;
        var slots = container.ContainerKind switch
        {
            LayoutContainerKind.HoverSwitch => new[] { container.PrimarySlot, container.SecondarySlot },
            LayoutContainerKind.AutoCollapse => new[] { container.PrimarySlot, container.CollapsedSlot },
            _ => new[] { container.PrimarySlot }
        };
        var measured = slots
            .Select(slot => MeasureSlot(slot, orientation, gap))
            .ToArray();
        var width = measured.Length == 0 ? 0 : measured.Max(item => item.WidthDip);
        var height = measured.Length == 0 ? 0 : measured.Max(item => item.HeightDip);
        if (orientation == LayoutFlowOrientation.Horizontal)
        {
            width = measured.Length == 0 ? 0 : measured.Max(item => item.WidthDip);
            height = measured.Length == 0 ? 0 : measured.Max(item => item.HeightDip);
        }

        return ApplyGeometry(new LayoutSize(width, height), container.Geometry);
    }

    private static LayoutSize MeasureSlot(LayoutSlot slot, LayoutFlowOrientation orientation, int gap)
    {
        var sizes = slot.Children
            .Where(child => child.Enabled)
            .Select(child => child switch
            {
                LayoutWidgetElement widget => MeasureWidget(widget),
                LayoutContainerElement container => MeasureContainer(container, orientation, gap),
                _ => new LayoutSize(0, 0)
            })
            .ToArray();
        if (sizes.Length == 0)
        {
            return new LayoutSize(0, 0);
        }

        return orientation == LayoutFlowOrientation.Horizontal
            ? new LayoutSize(
                sizes.Sum(size => size.WidthDip) + Math.Max(0, sizes.Length - 1) * gap,
                sizes.Max(size => size.HeightDip))
            : new LayoutSize(
                sizes.Max(size => size.WidthDip),
                sizes.Sum(size => size.HeightDip) + Math.Max(0, sizes.Length - 1) * gap);
    }

    private static LayoutSize MeasureWidget(LayoutWidgetElement widget)
    {
        var settings = widget.Settings;
        var size = widget.TypeId switch
        {
            BuiltInWidgetTypeIds.Artwork => new LayoutSize(40, 40),
            BuiltInWidgetTypeIds.MediaText when settings is MediaTextWidgetSettings text =>
                new LayoutSize(210, Math.Max(18, text.FontSizeDip * text.MaxLines + 4)),
            BuiltInWidgetTypeIds.MediaSource => new LayoutSize(150, 18),
            BuiltInWidgetTypeIds.Command when settings is CommandWidgetSettings command =>
                new LayoutSize(command.ButtonSizeDip, command.ButtonSizeDip),
            BuiltInWidgetTypeIds.Metrics => new LayoutSize(74, 24),
            BuiltInWidgetTypeIds.Spectrum => new LayoutSize(88, 24),
            BuiltInWidgetTypeIds.Separator when settings is SeparatorWidgetSettings separator =>
                new LayoutSize(separator.ThicknessDip + 16, separator.LengthDip),
            _ => new LayoutSize(24, 24)
        };
        return ApplyGeometry(size, widget.Geometry);
    }

    private static LayoutSize ApplyGeometry(LayoutSize size, LayoutGeometry geometry)
    {
        geometry ??= LayoutGeometry.Auto;
        var width = geometry.WidthDip ?? size.WidthDip;
        var height = geometry.HeightDip ?? size.HeightDip;
        if (geometry.MinWidthDip is { } minWidth)
        {
            width = Math.Max(width, minWidth);
        }
        if (geometry.MaxWidthDip is { } maxWidth)
        {
            width = Math.Min(width, maxWidth);
        }
        if (geometry.MinHeightDip is { } minHeight)
        {
            height = Math.Max(height, minHeight);
        }
        if (geometry.MaxHeightDip is { } maxHeight)
        {
            height = Math.Min(height, maxHeight);
        }

        var margin = geometry.Margin ?? LayoutThickness.Zero;
        return new LayoutSize(
            Math.Max(0, width + margin.Left + margin.Right),
            Math.Max(0, height + margin.Top + margin.Bottom));
    }

    internal static IReadOnlyList<LayoutWidgetElement> FindWidgets(
        LayoutProfile profile,
        string typeId)
    {
        return EnumerateWidgets(profile.Root)
            .Where(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal))
            .ToArray();
    }

    internal static MetricSettings ResolveMetricSamplingSettings(
        LayoutProfile? profile,
        MetricSettings fallback)
    {
        if (profile is null)
        {
            return fallback;
        }

        var requested = FindWidgets(profile, BuiltInWidgetTypeIds.Metrics)
            .Select(widget => widget.Settings)
            .OfType<MetricsWidgetSettings>()
            .SelectMany(settings => settings.CycleMetrics is { Count: > 0 }
                ? settings.CycleMetrics
                : [settings.Metric])
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
        {
            return fallback;
        }

        return fallback with
        {
            Enabled = true,
            ShowSystemMemory = fallback.ShowSystemMemory || requested.Contains(MetricKind.SystemMemory),
            ShowSystemCpu = fallback.ShowSystemCpu || requested.Contains(MetricKind.SystemCpu),
            ShowSystemGpu = fallback.ShowSystemGpu || requested.Contains(MetricKind.SystemGpu),
            ShowProcessMemory = fallback.ShowProcessMemory || requested.Contains(MetricKind.ProcessMemory)
        };
    }

    internal static int ResolveMetricRefreshInterval(LayoutProfile? profile, int fallbackMilliseconds)
    {
        if (profile is null)
        {
            return fallbackMilliseconds;
        }

        var intervals = FindWidgets(profile, BuiltInWidgetTypeIds.Metrics)
            .Select(widget => widget.Settings)
            .OfType<MetricsWidgetSettings>()
            .Select(settings => Math.Clamp(settings.RefreshIntervalMilliseconds, 250, 30_000))
            .ToArray();
        return intervals.Length == 0
            ? fallbackMilliseconds
            : intervals.Min();
    }

    private static IEnumerable<LayoutWidgetElement> EnumerateWidgets(
        LayoutContainerElement container)
    {
        foreach (var widget in EnumerateSlot(container.PrimarySlot))
        {
            yield return widget;
        }

        foreach (var widget in EnumerateSlot(container.SecondarySlot))
        {
            yield return widget;
        }

        foreach (var widget in EnumerateSlot(container.CollapsedSlot))
        {
            yield return widget;
        }
    }

    private static IEnumerable<LayoutWidgetElement> EnumerateSlot(LayoutSlot slot)
    {
        foreach (var child in slot.Children)
        {
            switch (child)
            {
                case LayoutWidgetElement widget:
                    yield return widget;
                    break;
                case LayoutContainerElement container:
                    foreach (var nested in EnumerateWidgets(container))
                    {
                        yield return nested;
                    }
                    break;
            }
        }
    }
}
