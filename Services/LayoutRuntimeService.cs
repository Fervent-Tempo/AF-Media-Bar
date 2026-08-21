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
        bool vertical)
    {
        var key = ResolveProfileKey(vertical);
        return document.Get(key);
    }

    internal static LayoutProfileKey ResolveProfileKey(bool vertical)
    {
        return vertical ? LayoutProfileKey.Vertical : LayoutProfileKey.Horizontal;
    }

    internal static bool ContainsWidget(LayoutProfile profile, string typeId)
    {
        return EnumerateWidgets(profile)
            .Any(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 从当前布局派生需要启动的组件能力；旧注册表布尔值只保留低 GPU 全局选项，不再覆盖可视化布局。
    /// Derives component capabilities from the active layout; legacy registry booleans no longer override the visual layout except for global low-GPU mode.
    /// </summary>
    internal static MetricSettings ResolveComponentSettings(
        LayoutProfile? profile,
        MetricSettings persisted)
    {
        if (profile is null)
        {
            return persisted;
        }

        var metricWidgets = FindWidgets(profile, BuiltInWidgetTypeIds.Metrics)
            .Select(widget => widget.Settings)
            .OfType<MetricsWidgetSettings>()
            .ToArray();
        var requestedMetrics = metricWidgets
            .SelectMany(settings => settings.CycleMetrics is { Count: > 0 }
                ? settings.CycleMetrics
                : [settings.Metric])
            .Distinct()
            .ToArray();
        var commands = FindWidgets(profile, BuiltInWidgetTypeIds.Command)
            .Select(widget => widget.Settings)
            .OfType<CommandWidgetSettings>()
            .Select(settings => settings.Command)
            .ToHashSet();
        return new MetricSettings(
            requestedMetrics.Length > 0,
            requestedMetrics.Contains(MetricKind.SystemMemory),
            requestedMetrics.Contains(MetricKind.SystemCpu),
            requestedMetrics.Contains(MetricKind.SystemGpu),
            requestedMetrics.Contains(MetricKind.ProcessMemory),
            persisted.LowGpuMode,
            ContainsWidget(profile, BuiltInWidgetTypeIds.Spectrum),
            commands.Contains(MediaCommandKind.SelectOutputDevice),
            commands.Contains(MediaCommandKind.AdjustVolume),
            metricWidgets.Any(settings => settings.OpenTaskManagerOnClick));
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
        var inlineSizes = profile.InlineContainers
            .Where(container => container.Enabled)
            .Select(container => MeasureContainer(
                container,
                orientation,
                profile.Surface.GapDip))
            .ToArray();
        var size = Combine(inlineSizes, orientation, profile.Surface.GapDip);
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

    internal static LayoutSize CalculateCompositionSize(
        LayoutProfile profile,
        LayoutEdge? unavailableEdge = null,
        IReadOnlySet<string>? expandedEdgeContainerIds = null)
    {
        var strip = CalculateDesiredSize(profile);
        var edgeSizes = profile.EdgeContainers
            .Where(container => container.Enabled &&
                container.Edge != unavailableEdge)
            .Select(container =>
            {
                var expanded = expandedEdgeContainerIds is null ||
                    expandedEdgeContainerIds.Contains(container.InstanceId);
                return (container.Edge, Size: expanded
                    ? MeasureEdgeContainer(profile, container)
                    : MeasureEdgeTrigger(profile, container));
            })
            .ToArray();
        var left = edgeSizes.Where(item => item.Edge == LayoutEdge.Left)
            .Select(item => item.Size.WidthDip).DefaultIfEmpty().Max();
        var right = edgeSizes.Where(item => item.Edge == LayoutEdge.Right)
            .Select(item => item.Size.WidthDip).DefaultIfEmpty().Max();
        var top = edgeSizes.Where(item => item.Edge == LayoutEdge.Top)
            .Select(item => item.Size.HeightDip).DefaultIfEmpty().Max();
        var bottom = edgeSizes.Where(item => item.Edge == LayoutEdge.Bottom)
            .Select(item => item.Size.HeightDip).DefaultIfEmpty().Max();
        return new LayoutSize(
            Math.Max(1, strip.WidthDip + left + right),
            Math.Max(1, strip.HeightDip + top + bottom));
    }

    internal static LayoutSize MeasureEdgeContainer(LayoutProfile profile, LayoutEdgeContainer container)
    {
        return MeasureSlot(
            container.ExpandedSlot,
            profile.LayoutMode == PlayerLayoutMode.Vertical
                ? LayoutFlowOrientation.Vertical
                : LayoutFlowOrientation.Horizontal,
            profile.Surface.GapDip);
    }

    /// <summary>
    /// 计算折叠状态只需保留的触发区尺寸；展开内容不应参与窗口碰撞边界。
    /// Measures the trigger-only footprint so expanded content does not remain in the window collision bounds while collapsed.
    /// </summary>
    internal static LayoutSize MeasureEdgeTrigger(
        LayoutProfile profile,
        LayoutEdgeContainer container)
    {
        var expanded = MeasureEdgeContainer(profile, container);
        var trigger = Math.Clamp(container.TriggerThicknessDip, 2, 24);
        return container.Edge is LayoutEdge.Top or LayoutEdge.Bottom
            ? new LayoutSize(Math.Min(Math.Max(36, expanded.WidthDip), 72), trigger)
            : new LayoutSize(trigger, Math.Min(Math.Max(36, expanded.HeightDip), 72));
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
                LayoutWidgetElement widget => MeasureWidget(widget, orientation),
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

    private static LayoutSize Combine(
        IReadOnlyList<LayoutSize> sizes,
        LayoutFlowOrientation orientation,
        int gap)
    {
        if (sizes.Count == 0)
        {
            return new LayoutSize(1, 1);
        }

        return orientation == LayoutFlowOrientation.Horizontal
            ? new LayoutSize(
                sizes.Sum(size => size.WidthDip) + Math.Max(0, sizes.Count - 1) * gap,
                sizes.Max(size => size.HeightDip))
            : new LayoutSize(
                sizes.Max(size => size.WidthDip),
                sizes.Sum(size => size.HeightDip) + Math.Max(0, sizes.Count - 1) * gap);
    }

    private static LayoutSize MeasureWidget(
        LayoutWidgetElement widget,
        LayoutFlowOrientation orientation)
    {
        var settings = widget.Settings;
        var isVertical = orientation == LayoutFlowOrientation.Vertical;
        var size = widget.TypeId switch
        {
            BuiltInWidgetTypeIds.Artwork => new LayoutSize(40, 40),
            BuiltInWidgetTypeIds.MediaText when settings is MediaTextWidgetSettings text =>
                text.TextKind == MediaTextKind.TitleAndArtist
                    ? new LayoutSize(isVertical ? 68 : 150, 40)
                    : new LayoutSize(isVertical ? 68 : 210, 40),
            BuiltInWidgetTypeIds.MediaSource => new LayoutSize(isVertical ? 68 : 150, 18),
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
        return EnumerateWidgets(profile)
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

    private static IEnumerable<LayoutWidgetElement> EnumerateWidgets(LayoutProfile profile)
    {
        foreach (var container in profile.InlineContainers.Where(container => container.Enabled))
        {
            foreach (var widget in EnumerateWidgets(container))
            {
                yield return widget;
            }
        }

        foreach (var edge in profile.EdgeContainers.Where(edge => edge.Enabled))
        {
            foreach (var widget in EnumerateSlot(edge.ExpandedSlot))
            {
                yield return widget;
            }
        }
    }

    private static IEnumerable<LayoutWidgetElement> EnumerateWidgets(
        LayoutContainerElement container)
    {
        if (!container.Enabled)
        {
            yield break;
        }

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
