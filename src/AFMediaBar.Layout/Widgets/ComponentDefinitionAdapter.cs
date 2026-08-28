using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.Layout;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.System;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets;

/// <summary>
/// Bridges migrated component definitions to schema-5 layout settings without changing persisted types.
/// </summary>
public static class ComponentDefinitionAdapter
{
    private static readonly BuiltInComponentRegistry Registry = new();

    internal static bool TryCreateDefaultSettings(string typeId, out WidgetSettings settings)
    {
        settings = null!;
        if (!Registry.TryGet(typeId, out var definition))
        {
            return false;
        }

        settings = definition.CreateDefaultSettings() switch
        {
            ArtworkSettings artwork => new ArtworkWidgetSettings(artwork.CornerRadiusDip, artwork.UseMediaPrimaryColor, artwork.OpenSourceOnClick),
            MediaTextSettings text => new MediaTextWidgetSettings(ToLayoutTextKind(text.TextKind), text.EnableMarquee, text.FontSizeDip, text.MaxLines),
            MediaSourceSettings source => new MediaTextWidgetSettings(MediaTextKind.Source, false, source.FontSizeDip, source.MaxLines),
            PlaybackCommandSettings command => new CommandWidgetSettings((MediaCommandKind)command.Command, command.ButtonSizeDip),
            OutputDeviceSettings output => new CommandWidgetSettings(MediaCommandKind.SelectOutputDevice, output.ButtonSizeDip),
            VolumeSettings volume => new CommandWidgetSettings(MediaCommandKind.AdjustVolume, volume.ButtonSizeDip),
            SpectrumSettings spectrum => new SpectrumWidgetSettings(spectrum.BandCount, spectrum.RefreshRateHz, spectrum.SensitivityPercent),
            MetricsSettings metrics => new MetricsWidgetSettings(
                (AFMediaBar.Layout.Models.MetricKind)metrics.Metric,
                metrics.OpenTaskManagerOnClick,
                metrics.RefreshIntervalMilliseconds,
                metrics.EffectiveCycleMetrics.Select(x => (AFMediaBar.Layout.Models.MetricKind)x).ToArray()),
            SeparatorSettings separator => new SeparatorWidgetSettings(separator.ThicknessDip, separator.LengthDip),
            _ => null!
        };
        return settings is not null;
    }

    internal static bool TryMeasure(
        LayoutProfile profile,
        LayoutWidgetElement widget,
        out (int Width, int Height) measurement)
    {
        measurement = default;
        if (!TryResolveDefinition(widget, out var definition) ||
            !TryMapSettings(widget.TypeId, widget.Settings, out var settings))
        {
            return false;
        }

        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var result = definition.Measure(
            settings,
            new ComponentMeasureContext(
                grid.Columns,
                grid.Rows,
                grid.CellSizeDip,
                profile.LayoutMode == PlayerLayoutMode.Vertical));
        var cell = Math.Max(grid.CellSizeDip, 1);
        var width = result.PreferredWidth;
        var height = result.PreferredHeight;
        if (widget.Settings is MediaTextWidgetSettings &&
            widget.Geometry is { WidthDip: not null, HeightDip: not null } geometry)
        {
            width = ToCells(geometry.WidthDip ?? 0, cell);
            height = ToCells(geometry.HeightDip ?? 0, cell);
        }

        measurement = (width, height);
        return true;
    }

    private static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / cellSizeDip));

    public static bool TryMapSettings(LayoutWidgetElement widget, out IComponentSettings componentSettings) =>
        TryMapSettings(widget.TypeId, widget.Settings, out componentSettings);

    private static bool TryMapSettings(string typeId, WidgetSettings settings, out IComponentSettings componentSettings)
    {
        componentSettings = (typeId, settings) switch
        {
            (BuiltInWidgetTypeIds.Artwork, ArtworkWidgetSettings artwork) =>
                new ArtworkSettings(artwork.CornerRadiusDip, artwork.UseMediaPrimaryColor, artwork.OpenSourceOnClick),
            (BuiltInWidgetTypeIds.MediaText, MediaTextWidgetSettings text) =>
                new MediaTextSettings(ToComponentTextKind(text.TextKind), text.EnableMarquee, text.FontSizeDip, text.MaxLines),
            (BuiltInWidgetTypeIds.MediaSource, MediaTextWidgetSettings source) =>
                new MediaSourceSettings(source.FontSizeDip, source.MaxLines),
            (BuiltInWidgetTypeIds.Command, CommandWidgetSettings command) =>
                command.Command switch
                {
                    MediaCommandKind.SelectOutputDevice => new OutputDeviceSettings(command.ButtonSizeDip),
                    MediaCommandKind.AdjustVolume => new VolumeSettings(command.ButtonSizeDip),
                    _ => new PlaybackCommandSettings((PlaybackCommandKind)command.Command, command.ButtonSizeDip)
                },
            (BuiltInWidgetTypeIds.Spectrum, SpectrumWidgetSettings spectrum) =>
                new SpectrumSettings(spectrum.BandCount, spectrum.RefreshRateHz, spectrum.SensitivityPercent),
            (BuiltInWidgetTypeIds.Metrics, MetricsWidgetSettings metrics) =>
                new MetricsSettings(
                    (AFMediaBar.Components.BuiltIn.System.MetricKind)metrics.Metric,
                    metrics.OpenTaskManagerOnClick,
                    metrics.RefreshIntervalMilliseconds,
                    metrics.CycleMetrics.Select(x => (AFMediaBar.Components.BuiltIn.System.MetricKind)x).ToArray()),
            (BuiltInWidgetTypeIds.Separator, SeparatorWidgetSettings separator) =>
                new SeparatorSettings(separator.ThicknessDip, separator.LengthDip),
            _ => null!
        };
        return componentSettings is not null;
    }

    private static bool TryResolveDefinition(LayoutWidgetElement widget, out IComponentDefinition definition)
    {
        var typeId = widget.Settings is CommandWidgetSettings command
            ? command.Command switch
            {
                MediaCommandKind.SelectOutputDevice => ComponentTypeIds.OutputDevice,
                MediaCommandKind.AdjustVolume => ComponentTypeIds.Volume,
                _ => widget.TypeId
            }
            : widget.TypeId;
        return Registry.TryGet(typeId, out definition);
    }

    private static MediaTextKind ToLayoutTextKind(MediaTextContentKind kind) => kind switch
    {
        MediaTextContentKind.Artist => MediaTextKind.Artist,
        MediaTextContentKind.TitleAndArtist => MediaTextKind.TitleAndArtist,
        _ => MediaTextKind.Title
    };

    private static MediaTextContentKind ToComponentTextKind(MediaTextKind kind) => kind switch
    {
        MediaTextKind.Artist => MediaTextContentKind.Artist,
        MediaTextKind.TitleAndArtist => MediaTextContentKind.TitleAndArtist,
        _ => MediaTextContentKind.Title
    };
}
