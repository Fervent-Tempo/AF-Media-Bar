using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.Layout;
using AFMediaBar.Components.BuiltIn.System;
using AFMediaBar.Layout.Models;
using ComponentMetricKind = AFMediaBar.Components.BuiltIn.System.MetricKind;
using LayoutMetricKind = AFMediaBar.Layout.Models.MetricKind;

namespace AFMediaBar.Layout.Widgets.Schema5;

internal sealed class SpectrumSchema5Codec : Schema5ComponentCodec<SpectrumSettings, SpectrumWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.Spectrum;
    protected override SpectrumWidgetSettings ToSchema5(SpectrumSettings settings) =>
        new(settings.BandCount, settings.RefreshRateHz, settings.SensitivityPercent);
    protected override SpectrumSettings FromSchema5(SpectrumWidgetSettings settings) =>
        new(settings.BandCount, settings.RefreshRateHz, settings.SensitivityPercent);
}

internal sealed class MetricsSchema5Codec : Schema5ComponentCodec<MetricsSettings, MetricsWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.Metrics;
    protected override MetricsWidgetSettings ToSchema5(MetricsSettings settings) =>
        new((LayoutMetricKind)settings.Metric, settings.OpenTaskManagerOnClick, settings.RefreshIntervalMilliseconds,
            settings.EffectiveCycleMetrics.Select(metric => (LayoutMetricKind)metric).ToArray());
    protected override MetricsSettings FromSchema5(MetricsWidgetSettings settings) =>
        new((ComponentMetricKind)settings.Metric, settings.OpenTaskManagerOnClick, settings.RefreshIntervalMilliseconds,
            settings.CycleMetrics.Select(metric => (ComponentMetricKind)metric).ToArray());
}

internal sealed class SeparatorSchema5Codec : Schema5ComponentCodec<SeparatorSettings, SeparatorWidgetSettings>
{
    public override string TypeId => ComponentTypeIds.Separator;
    protected override SeparatorWidgetSettings ToSchema5(SeparatorSettings settings) =>
        new(settings.ThicknessDip, settings.LengthDip);
    protected override SeparatorSettings FromSchema5(SeparatorWidgetSettings settings) =>
        new(settings.ThicknessDip, settings.LengthDip);
}
