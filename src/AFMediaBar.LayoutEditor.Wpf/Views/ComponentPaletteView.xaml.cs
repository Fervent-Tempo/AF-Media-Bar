using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.LayoutEditor.Wpf.ViewModels;

namespace AFMediaBar.LayoutEditor.Wpf.Views;

public partial class ComponentPaletteView : UserControl
{
    private Point _dragStart;

    public ComponentPaletteView() => InitializeComponent();

    public event EventHandler<ComponentPaletteItemViewModel>? ItemInvoked;
    public event EventHandler<ComponentPaletteDragEventArgs>? ItemDragRequested;
    public Func<ComponentPaletteItemViewModel, FrameworkElement?>? PreviewFactory { get; set; }

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ComponentPaletteItemViewModel item })
        {
            ItemInvoked?.Invoke(this, item);
        }
    }

    private void Preview_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ContentControl { DataContext: ComponentPaletteItemViewModel item } host)
        {
            host.Content = PreviewFactory?.Invoke(item);
        }
    }

    private void PaletteButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(this);

    private void PaletteButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button { DataContext: ComponentPaletteItemViewModel item } button)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        ItemDragRequested?.Invoke(this, new ComponentPaletteDragEventArgs(item, button));
    }
}

public sealed class ComponentPaletteDragEventArgs(
    ComponentPaletteItemViewModel item,
    FrameworkElement source) : EventArgs
{
    public ComponentPaletteItemViewModel Item { get; } = item;
    public FrameworkElement Source { get; } = source;
}
