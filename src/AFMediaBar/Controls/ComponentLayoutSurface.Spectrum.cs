using System.Windows;
using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildSpectrum(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SpectrumWidgetSettings ?? new SpectrumWidgetSettings(9, 20, 100);
        return new SpectrumView(
            Math.Clamp(settings.BandCount, 1, AudioMonitorService.BandCount),
            Math.Clamp(settings.RefreshRateHz, 5, 30),
            Math.Clamp(settings.SensitivityPercent, 1, 400),
            ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
    }
}
