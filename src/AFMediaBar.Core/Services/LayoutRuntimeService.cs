using AFMediaBar.Models;
using AFMediaBar.Layout.Runtime;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Services;

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
        => LayoutProfileSelector.ResolveProfile(document, vertical);

    public static LayoutProfileKey ResolveProfileKey(bool vertical) =>
        LayoutProfileSelector.ResolveProfileKey(vertical);

    public static bool ContainsWidget(LayoutProfile profile, string typeId) =>
        LayoutProfileQueryService.ContainsWidget(profile, typeId);

    /// <summary>
    /// 从当前布局派生需要启动的组件能力；旧注册表布尔值只保留低 GPU 全局选项，不再覆盖可视化布局。
    /// Derives component capabilities from the active layout; legacy registry booleans no longer override the visual layout except for global low-GPU mode.
    /// </summary>
    public static MetricSettings ResolveComponentSettings(
        LayoutProfile? profile,
        MetricSettings persisted,
        IComponentSettingsMapper? settingsMapper = null)
    {
        if (profile is null)
        {
            return persisted;
        }

        var features = LayoutComponentFeatureQueryService.Resolve(profile, settingsMapper);
        var requestedMetrics = features.RequestedMetrics;
        return new MetricSettings(
            requestedMetrics.Count > 0,
            requestedMetrics.Contains(MetricKind.SystemMemory),
            requestedMetrics.Contains(MetricKind.SystemCpu),
            requestedMetrics.Contains(MetricKind.SystemGpu),
            requestedMetrics.Contains(MetricKind.ProcessMemory),
            persisted.LowGpuMode,
            features.SpectrumEnabled,
            features.OutputDeviceEnabled,
            features.VolumeEnabled,
            features.OpenTaskManagerOnClick);
    }

    /// <summary>
    /// 求所有启用非折叠容器的占用联合矩形；实际窗口以联合矩形左上角为局部原点，不含前导空白。
    /// Returns the union rectangle of enabled non-collapse containers; the real window uses its top-left corner as the local origin with no leading blank space.
    /// </summary>
    public static LayoutGridRect? CalculateBodyGridBounds(LayoutProfile profile)
        => LayoutGridGeometryService.CalculateBodyGridBounds(profile);

    /// <summary>
    /// 求非折叠容器和当前展开/折叠状态折叠容器的占用联合矩形；折叠容器折叠时只保留触发条。
    /// Returns the union of non-collapse containers and collapse containers in their current expanded or collapsed (trigger-only) state.
    /// </summary>
    public static LayoutGridRect? CalculateCompositionGridBounds(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
        => LayoutCompositionGeometryService.CalculateCompositionGridBounds(profile, expandedCollapseIds);

    /// <summary>
    /// 折叠容器的触发条占用矩形：沿公共边保留触发厚度，长度限制在公共边交集内。
    /// Collapsed footprint of a collapse container: trigger thickness along the shared edge, length limited to the shared-edge intersection.
    /// </summary>
    public static LayoutGridRect CalculateCollapseTriggerBounds(
        LayoutCollapseContainer collapse,
        LayoutProfile profile)
        => LayoutCompositionGeometryService.CalculateCollapseTriggerBounds(collapse, profile);

    /// <summary>
    /// 网格矩形乘以单格尺寸得到 DIP 尺寸。
    /// Multiplies a grid rectangle by the cell size to produce DIP dimensions.
    /// </summary>
    public static LayoutSize GridRectToDip(LayoutGridRect rect, int cellSizeDip)
        => LayoutCompositionGeometryService.GridRectToDip(rect, cellSizeDip);

    /// <summary>
    /// 估算宿主 DIP 尺寸：启用非折叠容器联合矩形乘单格尺寸。
    /// Estimates host DIP size from the enabled non-collapse container union rectangle times the cell size.
    /// </summary>
    public static LayoutSize CalculateDesiredSize(LayoutProfile profile)
        => LayoutCompositionGeometryService.CalculateDesiredSize(profile);

    /// <summary>
    /// 估算含折叠容器展开/折叠状态的组合 DIP 尺寸。
    /// Estimates the combined DIP size including collapse containers in their current state.
    /// </summary>
    public static LayoutSize CalculateCompositionSize(
        LayoutProfile profile,
        IReadOnlySet<string>? expandedCollapseIds = null)
        => LayoutCompositionGeometryService.CalculateCompositionSize(profile, expandedCollapseIds);

    public static IReadOnlyList<LayoutWidgetElement> FindWidgets(
        LayoutProfile profile,
        string typeId)
    {
        return LayoutProfileQueryService.FindWidgets(profile, typeId);
    }

    public static MetricSettings ResolveMetricSamplingSettings(
        LayoutProfile? profile,
        MetricSettings fallback,
        IComponentSettingsMapper? settingsMapper = null)
    {
        if (profile is null)
        {
            return fallback;
        }

        var requested = LayoutComponentFeatureQueryService.Resolve(profile, settingsMapper).RequestedMetrics;
        if (requested.Count == 0)
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

    public static int ResolveMetricRefreshInterval(
        LayoutProfile? profile,
        int fallbackMilliseconds,
        IComponentSettingsMapper? settingsMapper = null)
    {
        if (profile is null)
        {
            return fallbackMilliseconds;
        }

        var interval = LayoutComponentFeatureQueryService.Resolve(profile, settingsMapper).MinimumMetricRefreshIntervalMilliseconds;
        return interval is null
            ? fallbackMilliseconds
            : interval.Value;
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
