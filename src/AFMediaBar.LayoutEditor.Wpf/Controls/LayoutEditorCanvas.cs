using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AFMediaBar.LayoutEditor.Wpf.Controls;

/// <summary>
/// Owns the editor viewport and logical grid surface. Business mutations stay
/// in the host until the command/session migration is complete.
/// </summary>
public sealed class LayoutEditorCanvas : Grid
{
    public LayoutEditorCanvas()
    {
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        AllowDrop = false;

        GridSurface = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AllowDrop = false,
            Focusable = true
        };
        Children.Add(GridSurface);

        GridSurface.MouseLeftButtonDown += (_, e) => MouseLeftButtonDown?.Invoke(this, e);
        GridSurface.MouseMove += (_, e) => MouseMove?.Invoke(this, e);
        GridSurface.MouseLeftButtonUp += (_, e) => MouseLeftButtonUp?.Invoke(this, e);
        GridSurface.MouseLeave += (_, e) => MouseLeave?.Invoke(this, e);
        GridSurface.PreviewKeyDown += (_, e) => PreviewKeyDown?.Invoke(this, e);
    }

    public Canvas GridSurface { get; }

    public new event MouseButtonEventHandler? MouseLeftButtonDown;

    public new event MouseEventHandler? MouseMove;

    public new event MouseButtonEventHandler? MouseLeftButtonUp;

    public new event MouseEventHandler? MouseLeave;

    public new event KeyEventHandler? PreviewKeyDown;

    public void Configure(
        double width,
        double height,
        Brush background,
        Transform renderTransform)
    {
        Width = width;
        Height = height;
        GridSurface.Width = width;
        GridSurface.Height = height;
        GridSurface.Background = background;
        GridSurface.RenderTransform = renderTransform;
    }
}
