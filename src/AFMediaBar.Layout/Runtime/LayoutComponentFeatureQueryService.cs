using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.System;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using LayoutMetricKind = AFMediaBar.Layout.Models.MetricKind;

namespace AFMediaBar.Layout.Runtime;

/// <summary>
/// Derives platform-neutral runtime capabilities declared by an enabled layout.
/// Schema-5 DTO inspection stays inside Layout; Core only consumes this result.
/// </summary>
public static class LayoutComponentFeatureQueryService
{
    public static LayoutComponentFeatureSet Resolve(
        LayoutProfile profile,
        IComponentSettingsMapper? settingsMapper = null)
    {
        var mapper = settingsMapper ?? ComponentDefinitionAdapter.Default;
        var metrics = LayoutProfileQueryService.FindWidgets(profile, ComponentTypeIds.Metrics)
            .Select(widget => mapper.TryMapSettings(widget, out var settings) ? settings : null)
            .OfType<MetricsSettings>()
            .ToArray();
        var requestedMetrics = metrics
            .SelectMany(settings => settings.CycleMetrics is { Count: > 0 }
                ? settings.CycleMetrics
                : [settings.Metric])
            .Select(metric => (LayoutMetricKind)(int)metric)
            .ToHashSet();
        var commands = LayoutProfileQueryService.FindWidgets(profile, ComponentTypeIds.PlaybackCommand)
            .Concat(LayoutProfileQueryService.FindWidgets(profile, ComponentTypeIds.OutputDevice))
            .Concat(LayoutProfileQueryService.FindWidgets(profile, ComponentTypeIds.Volume))
            .Select(widget => mapper.TryMapSettings(widget, out var settings) ? settings : null)
            .ToHashSet();

        return new LayoutComponentFeatureSet(
            requestedMetrics,
            LayoutProfileQueryService.ContainsWidget(profile, ComponentTypeIds.Spectrum),
            commands.Any(settings => settings is OutputDeviceSettings),
            commands.Any(settings => settings is VolumeSettings),
            metrics.Any(settings => settings.OpenTaskManagerOnClick),
            metrics.Length == 0
                ? null
                : metrics.Min(settings => Math.Clamp(settings.RefreshIntervalMilliseconds, 250, 30_000)));
    }
}

public sealed record LayoutComponentFeatureSet(
    IReadOnlySet<LayoutMetricKind> RequestedMetrics,
    bool SpectrumEnabled,
    bool OutputDeviceEnabled,
    bool VolumeEnabled,
    bool OpenTaskManagerOnClick,
    int? MinimumMetricRefreshIntervalMilliseconds);
