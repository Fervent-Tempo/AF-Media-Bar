using System.Windows.Input;
using AFMediaBar.LayoutEditor.Wpf.Controls;

namespace AFMediaBar.LayoutEditor.Wpf.Input;

/// <summary>
/// Routes canvas input through the editor module. The host can subscribe to
/// these events without owning the canvas event hookup or its lifetime.
/// </summary>
public sealed class LayoutEditorInputRouter : IDisposable
{
    private LayoutEditorCanvas? _canvas;

    public event MouseButtonEventHandler? MouseLeftButtonDown;
    public event MouseEventHandler? MouseMove;
    public event MouseButtonEventHandler? MouseLeftButtonUp;
    public event MouseEventHandler? MouseLeave;
    public event KeyEventHandler? PreviewKeyDown;

    public void Attach(LayoutEditorCanvas canvas)
    {
        Dispose();
        _canvas = canvas;
        canvas.MouseLeftButtonDown += OnMouseLeftButtonDown;
        canvas.MouseMove += OnMouseMove;
        canvas.MouseLeftButtonUp += OnMouseLeftButtonUp;
        canvas.MouseLeave += OnMouseLeave;
        canvas.PreviewKeyDown += OnPreviewKeyDown;
    }

    public void Dispose()
    {
        if (_canvas is null)
        {
            return;
        }

        _canvas.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        _canvas.MouseMove -= OnMouseMove;
        _canvas.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        _canvas.MouseLeave -= OnMouseLeave;
        _canvas.PreviewKeyDown -= OnPreviewKeyDown;
        _canvas = null;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        MouseLeftButtonDown?.Invoke(_canvas ?? sender, e);

    private void OnMouseMove(object sender, MouseEventArgs e) =>
        MouseMove?.Invoke(_canvas ?? sender, e);

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        MouseLeftButtonUp?.Invoke(_canvas ?? sender, e);

    private void OnMouseLeave(object sender, MouseEventArgs e) =>
        MouseLeave?.Invoke(_canvas ?? sender, e);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) =>
        PreviewKeyDown?.Invoke(_canvas ?? sender, e);
}
