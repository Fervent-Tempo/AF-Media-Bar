using System.Windows;

namespace AFMediaBar.LayoutEditor.Wpf.Input;

/// <summary>
/// Pure grid coordinate conversion used by the WPF editor. Keeping this out of
/// SettingsWindow makes pointer math testable without constructing a window.
/// </summary>
public static class LayoutPointerMapper
{
    public static (int X, int Y) ToCell(Point canvasPoint, int cellSizeDip, int paddingCells)
    {
        var cell = Math.Max(cellSizeDip, 1);
        return (
            (int)Math.Floor(canvasPoint.X / cell) - paddingCells,
            (int)Math.Floor(canvasPoint.Y / cell) - paddingCells);
    }
}
