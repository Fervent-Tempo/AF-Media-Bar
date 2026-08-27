using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Layout.Editing;
using AFMediaBar.LayoutEditor.Wpf.Controls;
using AFMediaBar.Services;

namespace AFMediaBar.LayoutEditor.Wpf.Input;

/// <summary>
/// Owns the WPF pointer state machine for the layout surface.
/// Layout mutation and preview calculation remain host callbacks so this
/// controller does not depend on SettingsWindow or application services.
/// </summary>
public sealed class LayoutEditorPointerController
{
    private readonly LayoutEditorInteractionState _interaction;
    private readonly LayoutViewportState _viewport;
    private Point _drawStartDip;

    public LayoutEditorPointerController(
        LayoutEditorInteractionState interaction,
        LayoutViewportState viewport)
    {
        _interaction = interaction;
        _viewport = viewport;
    }

    public void HandleMouseLeftButtonDown(
        LayoutEditorCanvas host,
        FrameworkElement viewport,
        LayoutPlacementTool? placementTool,
        Action<Canvas, Point, bool> updateGhost)
    {
        var canvas = host.GridSurface;
        if (placementTool is null)
        {
            _interaction.BeginPanning(
                Mouse.GetPosition(viewport),
                _viewport.Translate);
            canvas.CaptureMouse();
            return;
        }

        _drawStartDip = Mouse.GetPosition(canvas);
        _interaction.BeginDrawing(_drawStartDip);
        updateGhost(canvas, _drawStartDip, false);
        canvas.CaptureMouse();
    }

    public void HandleMouseMove(
        LayoutEditorCanvas host,
        FrameworkElement viewport,
        LayoutPlacementTool? placementTool,
        Action<Point, double> updateTranslate,
        Action<Canvas, Point, bool> updateGhost,
        Action<Canvas, Point> updateHover)
    {
        var canvas = host.GridSurface;
        if (_interaction.IsPanning)
        {
            var point = Mouse.GetPosition(viewport);
            updateTranslate(
                _interaction.PanOrigin + (point - _interaction.PanStart),
                _viewport.Scale);
            return;
        }

        if (_interaction.IsDrawing)
        {
            var point = Mouse.GetPosition(canvas);
            _interaction.DragMoved |=
                Math.Abs(point.X - _drawStartDip.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(point.Y - _drawStartDip.Y) >= SystemParameters.MinimumVerticalDragDistance;
            updateGhost(canvas, point, _interaction.DragMoved);
            return;
        }

        if (placementTool is not null)
        {
            updateGhost(canvas, Mouse.GetPosition(canvas), false);
            return;
        }

        updateHover(canvas, Mouse.GetPosition(canvas));
    }

    public void HandleMouseLeftButtonUp(
        LayoutEditorCanvas host,
        FrameworkElement viewport,
        LayoutPlacementTool? placementTool,
        Action clearSelection,
        Action<Canvas, Point, LayoutPlacementTool> commitPlacement,
        Action<Canvas> hideGhost,
        Action clearPlacementTool)
    {
        var canvas = host.GridSurface;
        if (_interaction.IsPanning)
        {
            _interaction.EndPanning();
            canvas.ReleaseMouseCapture();
            var point = Mouse.GetPosition(viewport);
            if (Math.Abs(point.X - _interaction.PanStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _interaction.PanStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                clearSelection();
            }

            return;
        }

        if (!_interaction.IsDrawing)
        {
            return;
        }

        _interaction.EndDrawing();
        canvas.ReleaseMouseCapture();
        if (placementTool is { } tool)
        {
            commitPlacement(canvas, Mouse.GetPosition(canvas), tool);
            hideGhost(canvas);
            clearPlacementTool();
        }
    }

    public void HandleMouseLeave(
        LayoutEditorCanvas host,
        Action<Canvas> hideGhost,
        Action hideHover)
    {
        var canvas = host.GridSurface;
        if (_interaction.IsDrawing)
        {
            _interaction.EndDrawing();
            canvas.ReleaseMouseCapture();
            hideGhost(canvas);
        }

        hideHover();
    }

    public bool HandlePreviewKeyDown(
        LayoutEditorCanvas host,
        LayoutPlacementTool? placementTool,
        Action clearPlacementTool,
        Action<Canvas> hideGhost,
        Action clearMessage)
    {
        if (placementTool is null)
        {
            return false;
        }

        clearPlacementTool();
        hideGhost(host.GridSurface);
        clearMessage();
        return true;
    }
}
