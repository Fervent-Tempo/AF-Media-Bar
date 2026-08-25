using AFMediaBar.Models;

namespace AFMediaBar.Services;

public readonly record struct LayoutSize(double WidthDip, double HeightDip);

/// <summary>
/// 选择当前窗口上下文的布局档案并提供只读组件查询；schema 4 的尺寸全部来自网格联合边界。
/// Selects the profile for the current window context and exposes read-only widget queries; schema 4 sizes come from grid union bounds.
/// </summary>
public sealed class LayoutRuntimeService
{
    public const double EmptyContainerMinWidthDip = 64;
    public const double EmptyContainerMinHeightDip = 32;

    public LayoutProfile ResolveProfile(
        LayoutDocument document,
        bool vertical)
    {
        var key = ResolveProfileKey(vertical);
        return document.Get(key);
    }

    public static LayoutProfileKey ResolveProfileKey(bool vertical)
    {
        return vertical ? LayoutProfileKey.Vertical : LayoutProfileKey.Horizontal;
    }

    public static bool ContainsWidget(LayoutProfile profile, string typeId)
    {
        return EnumerateWidgets(profile)
            .Any(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 从当前布局派生需要启动的组件能力；旧注册表布尔值只保留低 GPU 全局选项，不再覆盖可视化布局。
    /// Derives component capabilities from the active layout; legacy registry booleans no longer override the visual layout except for global low-GPU mode.
    /// </summary>
    public static MetricSettings ResolveComponentSettings(
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
    /// 求所有启用非折叠容器的占用联合矩形；实际窗口以联合矩形左上角为局部原点，不含前导空白。
    /// Returns the union rectangle of enabled non-collapse containers; the real window uses its top-left corner as the local origin with no leading blank space.
    /// </summary>
    public static LayoutGridRect? CalculateBodyGridBounds(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        LayoutGridRect? union = null;
        foreach (var container in profile.Containers)
        {
            if (!container.Enabled || container.GridBounds is not { } bounds)
            {
                continue;
            }

            union = union is { } current ? Union(current, ClampToGrid(bounds, grid)) : ClampToGrid(bounds, grid);
        }

        return union;
    }

    /// <summary>
    /// 求非折叠容器和当前展开/折叠状态折叠容器的占用联合矩形；折叠容器折叠时只保留触发条。
    /// Returns the union of non-collapse containers and collapse containers in their current expanded or collapsed (trigger-only) state.
    /// </summary>
    public static LayoutGridRect? CalculateCompositionGridBounds(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
    {
        var union = CalculateBodyGridBounds(profile);
        foreach (var collapse in profile.CollapseContainers)
        {
            if (!collapse.Enabled || collapse.GridBounds is not { } bounds)
            {
                continue;
            }

            var expanded = expandedCollapseIds is null ||
                expandedCollapseIds.Contains(collapse.InstanceId);
            var footprint = expanded
                ? bounds
                : CalculateCollapseTriggerBounds(collapse, profile);
            union = union is { } current ? Union(current, footprint) : footprint;
        }

        return union;
    }

    /// <summary>
    /// 折叠容器的触发条占用矩形：沿公共边保留触发厚度，长度限制在公共边交集内。
    /// Collapsed footprint of a collapse container: trigger thickness along the shared edge, length limited to the shared-edge intersection.
    /// </summary>
    public static LayoutGridRect CalculateCollapseTriggerBounds(
        LayoutCollapseContainer collapse,
        LayoutProfile profile)
    {
        var bounds = collapse.GridBounds;
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var info = LayoutGridConstraintService.ResolveAttachment(collapse, profile);
        if (!info.Valid || info.SharedEdge.IsEmpty)
        {
            // 依附失效时退化为整个展开矩形，避免负尺寸。
            // Fall back to the full expanded rect when the attachment is invalid so the footprint stays positive.
            return ClampToGrid(bounds, grid);
        }

        var cellSize = Math.Max(grid.CellSizeDip, 1);
        var trigger = Math.Max(
            1,
            (int)Math.Ceiling(Math.Clamp(collapse.TriggerThicknessDip, 2, 24) / (double)cellSize));
        var shared = info.SharedEdge;
        var side = LayoutGridConstraintService.ConnectionSide(collapse.Attachment);
        var rect = side switch
        {
            LayoutEdge.Top => new LayoutGridRect(
                shared.X,
                bounds.Y,
                shared.Width,
                Math.Min(trigger, bounds.Height)),
            LayoutEdge.Bottom => new LayoutGridRect(
                shared.X,
                bounds.Bottom - Math.Min(trigger, bounds.Height),
                shared.Width,
                Math.Min(trigger, bounds.Height)),
            LayoutEdge.Left => new LayoutGridRect(
                bounds.X,
                shared.Y,
                Math.Min(trigger, bounds.Width),
                shared.Height),
            _ => new LayoutGridRect(
                bounds.Right - Math.Min(trigger, bounds.Width),
                shared.Y,
                Math.Min(trigger, bounds.Width),
                shared.Height)
        };
        return ClampToGrid(rect, grid);
    }

    /// <summary>
    /// 网格矩形乘以单格尺寸得到 DIP 尺寸。
    /// Multiplies a grid rectangle by the cell size to produce DIP dimensions.
    /// </summary>
    public static LayoutSize GridRectToDip(LayoutGridRect rect, int cellSizeDip)
    {
        var cell = Math.Max(cellSizeDip, 1);
        return new LayoutSize(rect.Width * cell, rect.Height * cell);
    }

    /// <summary>
    /// 估算宿主 DIP 尺寸：启用非折叠容器联合矩形乘单格尺寸。
    /// Estimates host DIP size from the enabled non-collapse container union rectangle times the cell size.
    /// </summary>
    public static LayoutSize CalculateDesiredSize(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var union = CalculateBodyGridBounds(profile);
        if (union is null)
        {
            return new LayoutSize(grid.CellSizeDip, grid.CellSizeDip);
        }

        return GridRectToDip(union, grid.CellSizeDip);
    }

    /// <summary>
    /// 估算含折叠容器展开/折叠状态的组合 DIP 尺寸。
    /// Estimates the combined DIP size including collapse containers in their current state.
    /// </summary>
    public static LayoutSize CalculateCompositionSize(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var union = CalculateCompositionGridBounds(profile, expandedCollapseIds);
        if (union is null)
        {
            return new LayoutSize(grid.CellSizeDip, grid.CellSizeDip);
        }

        return GridRectToDip(union, grid.CellSizeDip);
    }

    public static IReadOnlyList<LayoutWidgetElement> FindWidgets(
        LayoutProfile profile,
        string typeId)
    {
        return EnumerateWidgets(profile)
            .Where(widget => widget.Enabled &&
                string.Equals(widget.TypeId, typeId, StringComparison.Ordinal))
            .ToArray();
    }

    public static MetricSettings ResolveMetricSamplingSettings(
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

    public static int ResolveMetricRefreshInterval(LayoutProfile? profile, int fallbackMilliseconds)
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
        foreach (var container in profile.Containers.Where(container => container.Enabled))
        {
            foreach (var widget in EnumerateContainerWidgets(container))
            {
                yield return widget;
            }
        }

        foreach (var collapse in profile.CollapseContainers.Where(collapse => collapse.Enabled))
        {
            foreach (var widget in collapse.ExpandedSlot.Children.OfType<LayoutWidgetElement>())
            {
                yield return widget;
            }
        }
    }

    private static IEnumerable<LayoutWidgetElement> EnumerateContainerWidgets(
        LayoutContainerElement container)
    {
        foreach (var widget in container.PrimarySlot.Children.OfType<LayoutWidgetElement>())
        {
            yield return widget;
        }

        foreach (var widget in container.SecondarySlot.Children.OfType<LayoutWidgetElement>())
        {
            yield return widget;
        }
    }

    private static LayoutGridRect ClampToGrid(LayoutGridRect rect, LayoutGridSettings grid)
    {
        var left = Math.Max(0, rect.X);
        var top = Math.Max(0, rect.Y);
        var right = Math.Min(grid.Columns, rect.Right);
        var bottom = Math.Min(grid.Rows, rect.Bottom);
        return new LayoutGridRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static LayoutGridRect Union(LayoutGridRect a, LayoutGridRect b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X),
            Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));
}