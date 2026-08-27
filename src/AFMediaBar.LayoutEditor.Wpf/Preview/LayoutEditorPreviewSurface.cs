using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.LayoutEditor.Wpf.Preview;

namespace AFMediaBar.LayoutEditor.Wpf.Preview;

/// <summary>
/// Editor-only block preview. It deliberately does not render media, run
/// timers, or handle runtime commands; the settings host receives only design
/// gestures and applies them through Layout services.
/// </summary>
public sealed class LayoutEditorPreviewSurface : Canvas, IDisposable
{
    private readonly Dictionary<string, Border> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, LayoutEdge Edge), double> _resizeDeltas = [];
    private readonly Dictionary<string, bool> _pointerNearByContainer = new(StringComparer.Ordinal);
    private readonly Dictionary<Border, Point> _dragStarts = [];
    private LayoutProfile? _profile;
    private string? _collapseInstanceId;
    private LayoutGridRect _origin = LayoutGridRect.Unit(0, 0);
    private double _cell = 1;
    private string? _selectedId;
    private bool _disposed;

    public event EventHandler<LayoutDesignElementEventArgs>? DesignElementSelected;
    public event EventHandler<LayoutDesignPreviewStateEventArgs>? DesignPreviewStateChanged;
    public event EventHandler<LayoutDesignResizeEventArgs>? DesignResizeRequested;
    public event EventHandler? DesignResizeCompleted;
    public event EventHandler<LayoutDesignDeleteEventArgs>? DesignDeleteRequested;

    public LayoutEditorPreviewSurface()
    {
        Background = Brushes.Transparent;
        ClipToBounds = false;
    }

    public void SetDesignMode(bool enabled)
    {
        IsHitTestVisible = enabled;
    }

    public void SetDesignPlacementArmed(bool armed)
    {
        Cursor = armed ? Cursors.Cross : Cursors.Arrow;
    }

    public void SetMediaSnapshot(object? snapshot)
    {
        // Runtime media data is intentionally not part of the editor surface.
    }

    public void Apply(LayoutProfile profile, bool pointerNear)
    {
        _profile = profile;
        _collapseInstanceId = null;
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        _cell = Math.Max(1, grid.CellSizeDip);
        _origin = LayoutBoundsService.CalculateBodyGridBounds(profile) ?? LayoutGridRect.Unit(0, 0);
        Width = Math.Max(1, _origin.Width * _cell);
        Height = Math.Max(1, _origin.Height * _cell);
        Rebuild(profile.Containers.SelectMany(EnumerateContainers), pointerNear);
    }

    public void ApplyEdge(LayoutProfile profile, LayoutCollapseContainer collapse)
    {
        _profile = profile;
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        _cell = Math.Max(1, grid.CellSizeDip);
        _origin = collapse.GridBounds;
        Width = Math.Max(1, collapse.GridBounds.Width * _cell);
        Height = Math.Max(1, collapse.GridBounds.Height * _cell);
        _collapseInstanceId = collapse.InstanceId;
        Rebuild([], pointerNear: true);
        AddCollapseElement(collapse, pointerNear: true);
    }

    public void SetDesignSelection(string? instanceId)
    {
        _selectedId = instanceId;
        foreach (var (id, border) in _elements)
        {
            border.BorderThickness = new Thickness(id == instanceId ? 2 : 1);
            border.SetResourceReference(
                Border.BorderBrushProperty,
                id == instanceId ? "LayoutEditorAccentBrush" : "MenuBorderBrush");
        }
    }

    public void SetPointerNear(bool pointerNear)
    {
        // Proximity is emitted only from container hit testing. This setter
        // synchronizes host state without creating a selection event.
    }

    public void RefreshDesignGeometry(LayoutProfile profile)
    {
        if (_collapseInstanceId is { } collapseId &&
            profile.CollapseContainers.FirstOrDefault(item => item.InstanceId == collapseId) is { } collapse)
        {
            ApplyEdge(profile, collapse);
        }
        else
        {
            Apply(profile, pointerNear: false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Children.Clear();
        _elements.Clear();
        _resizeDeltas.Clear();
        _pointerNearByContainer.Clear();
        _dragStarts.Clear();
        _profile = null;
        _collapseInstanceId = null;
    }

    private void Rebuild(IEnumerable<LayoutElement> elements, bool pointerNear)
    {
        Children.Clear();
        _elements.Clear();
        _pointerNearByContainer.Clear();
        _dragStarts.Clear();
        foreach (var element in elements)
        {
            AddElement(element, pointerNear);
        }

        SetDesignSelection(_selectedId);
    }

    private void AddElement(LayoutElement element, bool pointerNear)
    {
        if (element.GridBounds is not { } bounds || !element.Enabled)
        {
            return;
        }

        var border = new Border
        {
            Width = Math.Max(1, bounds.Width * _cell),
            Height = Math.Max(1, bounds.Height * _cell),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = element is LayoutContainerElement
                ? new SolidColorBrush(Color.FromArgb(45, 90, 145, 205))
                : new SolidColorBrush(Color.FromArgb(55, 90, 180, 135)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 120, 140, 165)),
            ToolTip = element is LayoutWidgetElement widget ? widget.TypeId : "container",
            Tag = element.InstanceId
        };
        var isContainer = element is LayoutContainerElement;
        var label = new TextBlock
        {
            Text = element is LayoutWidgetElement widgetLabel
                ? widgetLabel.TypeId
                : element is LayoutContainerElement containerLabel
                    ? containerLabel.ContainerKind.ToString()
                    : "collapse",
            FontSize = 10,
            Margin = new Thickness(4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White,
            Visibility = isContainer ? Visibility.Collapsed : Visibility.Visible,
            IsHitTestVisible = false
        };
        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            isContainer ? "MenuHighlightTextBrush" : "MenuPrimaryTextBrush");
        border.Child = label;
        border.MouseLeftButtonUp += Element_OnMouseLeftButtonUp;
        border.MouseRightButtonUp += Element_OnMouseRightButtonUp;
        border.PreviewMouseLeftButtonDown += Element_OnPreviewMouseLeftButtonDown;
        border.PreviewMouseMove += Element_OnPreviewMouseMove;
        border.MouseMove += (_, _) =>
        {
            if (element is LayoutContainerElement hoverContainer)
            {
                label.Visibility = Visibility.Visible;
                var point = Mouse.GetPosition(border);
                var pointerNear = point.X >= border.ActualWidth / 2;
                if (!_pointerNearByContainer.TryGetValue(hoverContainer.InstanceId, out var previous) ||
                    previous != pointerNear)
                {
                    _pointerNearByContainer[hoverContainer.InstanceId] = pointerNear;
                    DesignPreviewStateChanged?.Invoke(
                        this,
                        new LayoutDesignPreviewStateEventArgs(
                            hoverContainer.InstanceId,
                            pointerNear));
                }
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (isContainer)
            {
                label.Visibility = Visibility.Collapsed;
            }
        };

        var x = (bounds.X - _origin.X) * _cell;
        var y = (bounds.Y - _origin.Y) * _cell;
        SetLeft(border, x);
        SetTop(border, y);
        Children.Add(border);
        _elements[element.InstanceId] = border;
        AddResizeHandles(element.InstanceId, border, bounds);

        if (element is LayoutContainerElement parentContainer)
        {
            foreach (var child in parentContainer.PrimarySlot.Children.Concat(parentContainer.SecondarySlot.Children))
            {
                if (child is LayoutWidgetElement childWidget && childWidget.GridBounds is { } local)
                {
                    var absolute = childWidget with
                    {
                        GridBounds = new LayoutGridRect(
                            bounds.X + local.X,
                            bounds.Y + local.Y,
                            local.Width,
                            local.Height)
                    };
                    AddElement(absolute, pointerNear);
                }
            }
        }
    }

    private void AddCollapseElement(LayoutCollapseContainer collapse, bool pointerNear)
    {
        var label = new TextBlock
        {
            Text = "collapse",
            FontSize = 10,
            Margin = new Thickness(4),
            Foreground = Brushes.White,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MenuHighlightTextBrush");
        var border = new Border
        {
            Width = Math.Max(1, collapse.GridBounds.Width * _cell),
            Height = Math.Max(1, collapse.GridBounds.Height * _cell),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(45, 180, 145, 90)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 180, 145, 90)),
            ToolTip = "collapse",
            Tag = collapse.InstanceId,
            Child = label
        };
        border.MouseLeftButtonUp += Element_OnMouseLeftButtonUp;
        border.MouseRightButtonUp += Element_OnMouseRightButtonUp;
        border.PreviewMouseLeftButtonDown += Element_OnPreviewMouseLeftButtonDown;
        border.PreviewMouseMove += Element_OnPreviewMouseMove;
        border.MouseEnter += (_, _) => label.Visibility = Visibility.Visible;
        border.MouseLeave += (_, _) => label.Visibility = Visibility.Collapsed;
        SetLeft(border, 0);
        SetTop(border, 0);
        Children.Add(border);
        _elements[collapse.InstanceId] = border;
        AddResizeHandles(collapse.InstanceId, border, collapse.GridBounds);
        foreach (var child in collapse.ExpandedSlot.Children)
        {
            if (child is LayoutWidgetElement widget && widget.GridBounds is { } local)
            {
                AddElement(
                    widget with
                    {
                        GridBounds = new LayoutGridRect(
                            collapse.GridBounds.X + local.X,
                            collapse.GridBounds.Y + local.Y,
                            local.Width,
                            local.Height)
                    },
                    pointerNear);
            }
        }

        SetDesignSelection(_selectedId);
    }

    private void AddResizeHandles(string instanceId, Border target, LayoutGridRect bounds)
    {
        foreach (var edge in Enum.GetValues<LayoutEdge>())
        {
            var thumb = new Thumb
            {
                Width = edge is LayoutEdge.Left or LayoutEdge.Right ? 8 : target.Width,
                Height = edge is LayoutEdge.Top or LayoutEdge.Bottom ? 8 : target.Height,
                Background = Brushes.Transparent,
                Cursor = edge switch
                {
                    LayoutEdge.Left or LayoutEdge.Right => Cursors.SizeWE,
                    _ => Cursors.SizeNS
                },
                Tag = (instanceId, edge, bounds)
            };
            thumb.DragDelta += ResizeThumb_OnDragDelta;
            thumb.DragCompleted += ResizeThumb_OnDragCompleted;
            SetLeft(thumb, GetLeft(target) + (edge is LayoutEdge.Right ? target.Width - 8 : 0));
            SetTop(thumb, GetTop(target) + (edge is LayoutEdge.Bottom ? target.Height - 8 : 0));
            Children.Add(thumb);
        }
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: ValueTuple<string, LayoutEdge, LayoutGridRect> data })
        {
            return;
        }

        var key = (data.Item1, data.Item2);
        var delta = data.Item2 is LayoutEdge.Left or LayoutEdge.Right ? e.HorizontalChange : e.VerticalChange;
        _resizeDeltas[key] = _resizeDeltas.GetValueOrDefault(key) + delta;
        DesignResizeRequested?.Invoke(
            this,
            new LayoutDesignResizeEventArgs(data.Item1, data.Item2, _resizeDeltas[key]));
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is Thumb { Tag: ValueTuple<string, LayoutEdge, LayoutGridRect> data })
        {
            _resizeDeltas.Remove((data.Item1, data.Item2));
        }

        DesignResizeCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Element_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string id } border)
        {
            DesignElementSelected?.Invoke(this, new LayoutDesignElementEventArgs(id, border));
            e.Handled = true;
        }
    }

    private void Element_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            _dragStarts[border] = e.GetPosition(this);
        }
    }

    private void Element_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: string id } border ||
            e.LeftButton != MouseButtonState.Pressed ||
            !_dragStarts.TryGetValue(border, out var start))
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStarts.Remove(border);
        var format = _profile?.Containers.Any(container => container.InstanceId == id) == true ||
                     _profile?.CollapseContainers.Any(collapse => collapse.InstanceId == id) == true
            ? LayoutEditorDragFormats.ExistingContainer
            : LayoutEditorDragFormats.ExistingWidget;
        var data = new DataObject(format, id);
        DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
        e.Handled = true;
    }

    private void Element_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string id } border)
        {
            DesignDeleteRequested?.Invoke(
                this,
                new LayoutDesignDeleteEventArgs(id, border, e.GetPosition(this)));
            e.Handled = true;
        }
    }

    private static IEnumerable<LayoutElement> EnumerateContainers(LayoutContainerElement container)
    {
        yield return container;
        foreach (var child in container.PrimarySlot.Children.Concat(container.SecondarySlot.Children))
        {
            if (child is LayoutContainerElement nested)
            {
                foreach (var item in EnumerateContainers(nested))
                {
                    yield return item;
                }
            }
        }
    }
}
