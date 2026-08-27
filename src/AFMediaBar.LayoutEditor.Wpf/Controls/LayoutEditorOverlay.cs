using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.LayoutEditor.Wpf.Controls;

/// <summary>
/// Owns design-only ghost and hover-cell visuals for the editor canvas.
/// </summary>
public sealed class LayoutEditorOverlay
{
    private Border? _ghost;
    private Border? _hoverCell;

    public void ShowGhost(Canvas canvas, LayoutGridRect rect, int cell, int paddingCells, bool valid)
    {
        _ghost ??= new Border
        {
            IsHitTestVisible = false,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(3)
        };
        _ghost.SetResourceReference(Border.BorderBrushProperty, "LayoutEditorAccentBrush");
        EnsureChild(canvas, _ghost);
        Canvas.SetLeft(_ghost, (rect.X + paddingCells) * cell);
        Canvas.SetTop(_ghost, (rect.Y + paddingCells) * cell);
        _ghost.Width = Math.Max(0, rect.Width * cell);
        _ghost.Height = Math.Max(0, rect.Height * cell);
        _ghost.Background = canvas.TryFindResource(valid ? "LayoutEditorDropBrush" : "LayoutEditorInvalidBrush") as Brush
            ?? Brushes.Transparent;
        _ghost.Visibility = Visibility.Visible;
    }

    public void ShowHoverCell(Canvas canvas, int x, int y, int cell, int paddingCells)
    {
        _hoverCell ??= new Border
        {
            IsHitTestVisible = false,
            BorderThickness = new Thickness(1)
        };
        _hoverCell.SetResourceReference(Border.BorderBrushProperty, "LayoutEditorAccentBrush");
        EnsureChild(canvas, _hoverCell);
        Canvas.SetLeft(_hoverCell, (x + paddingCells) * cell);
        Canvas.SetTop(_hoverCell, (y + paddingCells) * cell);
        _hoverCell.Width = cell;
        _hoverCell.Height = cell;
        _hoverCell.Visibility = Visibility.Visible;
    }

    public void HideGhost() => _ghost?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);

    public void HideHoverCell() => _hoverCell?.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);

    public void Reset()
    {
        _ghost = null;
        _hoverCell = null;
    }

    private static void EnsureChild(Canvas canvas, UIElement child)
    {
        if (!canvas.Children.Contains(child))
        {
            canvas.Children.Add(child);
        }
    }
}
