using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets;

public static class WidgetMeasurementService
{
    public static (int Width, int Height) MeasureRequiredCells(
        LayoutProfile profile,
        LayoutWidgetElement widget,
        IComponentSettingsMapper? settingsMapper = null)
    {
        var mapper = settingsMapper ?? ComponentDefinitionAdapter.Default;
        if (mapper.TryMeasure(profile, widget, out var migratedMeasurement))
        {
            return migratedMeasurement;
        }

        return (1, 1);
    }
}
