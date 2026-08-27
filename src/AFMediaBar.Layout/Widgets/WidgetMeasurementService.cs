using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets;

public static class WidgetMeasurementService
{
    public static (int Width, int Height) MeasureRequiredCells(LayoutProfile profile, LayoutWidgetElement widget)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var vertical = profile.LayoutMode == PlayerLayoutMode.Vertical;
        double width;
        double height;

        switch (widget.TypeId)
        {
            case BuiltInWidgetTypeIds.Artwork:
                width = 40; height = 40; break;
            case BuiltInWidgetTypeIds.MediaText:
            case BuiltInWidgetTypeIds.MediaSource:
            {
                var text = widget.Settings as MediaTextWidgetSettings;
                var fontSize = Math.Clamp(text?.FontSizeDip ?? 14, 6, 72);
                var combined = text?.TextKind == MediaTextKind.TitleAndArtist;
                width = widget.Geometry?.WidthDip ?? (vertical ? 68 : combined ? 150 : 210);
                if (combined)
                {
                    var titleHeight = Math.Max(22, Math.Ceiling(fontSize * 1.25));
                    var artistHeight = Math.Max(18, Math.Ceiling(Math.Max(6, fontSize - 3) * 1.25));
                    height = widget.Geometry?.HeightDip ?? titleHeight + artistHeight;
                }
                else
                {
                    var lineHeight = Math.Max(12, Math.Ceiling(fontSize * 1.25));
                    var lines = Math.Clamp(text?.MaxLines ?? 1, 1, 2);
                    height = widget.Geometry?.HeightDip ?? Math.Max(40, lineHeight * lines);
                }
                break;
            }
            case BuiltInWidgetTypeIds.Command:
                var command = widget.Settings as CommandWidgetSettings;
                var buttonSize = Math.Clamp(command?.ButtonSizeDip ?? CommandWidgetSettings.DefaultButtonSizeDip, 20, 96);
                width = buttonSize; height = buttonSize; break;
            case BuiltInWidgetTypeIds.Metrics:
                width = 74; height = 24; break;
            case BuiltInWidgetTypeIds.Spectrum:
                width = 88; height = 24; break;
            case BuiltInWidgetTypeIds.Separator:
                var separator = widget.Settings as SeparatorWidgetSettings;
                width = (separator?.ThicknessDip ?? 1) + 16;
                height = separator?.LengthDip ?? 22;
                break;
            default:
                width = 24; height = 24; break;
        }

        return (ToCells(width, cell), ToCells(height, cell));
    }

    private static int ToCells(double dip, int cellSizeDip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / cellSizeDip));
}
