using System.Windows;
using AFMediaBar.Layout.Models;
using AFMediaBar.Services;

namespace AFMediaBar.LayoutEditor.Wpf.Input;

/// <summary>
/// Mutable pointer interaction state owned by the WPF editor module. The host
/// decides what a committed placement means; this type only tracks the active
/// tool, draw gesture and pan gesture.
/// </summary>
public sealed class LayoutEditorInteractionState
{
    public LayoutPlacementTool? PlacementTool { get; set; }

    public bool IsDrawing { get; set; }

    public bool DragMoved { get; set; }

    public Point DrawStart { get; set; }

    public LayoutGridRect? DrawCandidate { get; set; }

    public bool IsPanning { get; set; }

    public Point PanStart { get; set; }

    public Point PanOrigin { get; set; }

    public void ClearPlacement()
    {
        PlacementTool = null;
        IsDrawing = false;
        DragMoved = false;
        DrawCandidate = null;
    }

    public void BeginDrawing(Point start)
    {
        IsDrawing = true;
        DragMoved = false;
        DrawStart = start;
        DrawCandidate = null;
    }

    public void BeginPanning(Point start, Point origin)
    {
        IsPanning = true;
        PanStart = start;
        PanOrigin = origin;
    }

    public void EndPanning() => IsPanning = false;

    public void EndDrawing()
    {
        IsDrawing = false;
        DragMoved = false;
    }
}
