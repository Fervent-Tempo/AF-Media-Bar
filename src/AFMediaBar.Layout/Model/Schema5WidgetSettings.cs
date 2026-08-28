using System.Text.Json.Serialization;

namespace AFMediaBar.Layout.Models;

/// <summary>
/// Schema-5 polymorphic widget settings retained as the persistence DTO boundary.
/// Runtime composition uses the corresponding AFMediaBar.Components settings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ArtworkWidgetSettings), "artwork")]
[JsonDerivedType(typeof(MediaTextWidgetSettings), "media-text")]
[JsonDerivedType(typeof(CommandWidgetSettings), "command")]
[JsonDerivedType(typeof(MetricsWidgetSettings), "metrics")]
[JsonDerivedType(typeof(SpectrumWidgetSettings), "spectrum")]
[JsonDerivedType(typeof(SeparatorWidgetSettings), "separator")]
public abstract record WidgetSettings;

public sealed record ArtworkWidgetSettings(
    int CornerRadiusDip,
    bool UseMediaPrimaryColor,
    bool OpenSourceOnClick) : WidgetSettings;

public sealed record MediaTextWidgetSettings(
    MediaTextKind TextKind,
    bool EnableMarquee,
    int FontSizeDip,
    int MaxLines) : WidgetSettings;

public sealed record CommandWidgetSettings(
    MediaCommandKind Command,
    int ButtonSizeDip) : WidgetSettings
{
    public const int DefaultButtonSizeDip = 24;
}

public sealed record MetricsWidgetSettings(
    MetricKind Metric,
    bool OpenTaskManagerOnClick,
    int RefreshIntervalMilliseconds,
    IReadOnlyList<MetricKind> CycleMetrics) : WidgetSettings;

public sealed record SpectrumWidgetSettings(
    int BandCount,
    int RefreshRateHz,
    int SensitivityPercent) : WidgetSettings
{
    public const int MaximumBandCount = 9;
}

public sealed record SeparatorWidgetSettings(
    int ThicknessDip,
    int LengthDip) : WidgetSettings;
