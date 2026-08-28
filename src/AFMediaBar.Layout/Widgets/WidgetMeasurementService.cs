using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets;

public static class WidgetMeasurementService
{
    public static (int Width, int Height) MeasureRequiredCells(LayoutProfile profile, LayoutWidgetElement widget)
    {
        if (ComponentDefinitionAdapter.TryMeasure(profile, widget, out var migratedMeasurement))
        {
            return migratedMeasurement;
        }

        return (1, 1);
    }
}
