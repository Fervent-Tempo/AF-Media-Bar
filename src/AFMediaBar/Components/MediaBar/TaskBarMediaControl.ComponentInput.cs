// 组件手势只预览视图，松手时由命令提交；整栏移动仍由宿主管理。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private Border? _barMoveHandle;
    private Border? _componentInsertionMarker;
    private bool _barDragActive;
    private bool _componentGestureActive;
    private bool _suppressComponentRelease;
    private bool _finishingComponentGesture;
    private ComponentReorderRequest? _componentGesture;
    private Point _componentPress;
    private bool _componentVertical;
    private DpiScale _componentDpi;
    private readonly Dictionary<TaskbarRestComponent, Rect> _componentStartBounds = new();
    private IReadOnlyList<ComponentLayoutEntry>? _componentPreview;
    private IInputElement? _componentPreviousFocus;
    public ICommand? ComponentReorderCommand { get; set; }
    public bool HasComponentGesture => _componentGesture is not null;

    private Dictionary<TaskbarRestComponent, FrameworkElement> HoverElements() => new()
    {
        [TaskbarRestComponent.PreviousTrack] = TaskbarPreviousButton,
        [TaskbarRestComponent.PlayPause] = TaskbarPlayPauseButton,
        [TaskbarRestComponent.NextTrack] = TaskbarNextButton,
        [TaskbarRestComponent.OutputDevice] = TaskbarDeviceButton,
        [TaskbarRestComponent.InputDevice] = TaskbarInputDeviceButton,
        [TaskbarRestComponent.Volume] = TaskbarVolumeButton
    };
    private Dictionary<TaskbarRestComponent, FrameworkElement> RestElements()
    {
        var elements = new Dictionary<TaskbarRestComponent, FrameworkElement>
        {
            [TaskbarRestComponent.Spectrum] = TaskbarSpectrumHoverSurface,
            [TaskbarRestComponent.BluetoothBattery] = TaskbarBatterySurface,
            [TaskbarRestComponent.OutputDevice] = TaskbarOutputDeviceHoverSurface,
            [TaskbarRestComponent.InputDevice] = TaskbarInputDeviceHoverSurface,
            [TaskbarRestComponent.Volume] = TaskbarVolumeHoverSurface
        };
        foreach (var surface in _metricSurfaces.Values)
            if (surface.Tag is TaskbarRestComponent component && surface.Visibility == Visibility.Visible) elements[component] = surface;
        return elements;
    }

    private void InitializeComponentLayoutInput()
    {
        _barMoveHandle = new Border
        {
            Background = Brushes.Transparent,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        Panel.SetZIndex(_barMoveHandle, 10);
        InteractionSurface.Children.Add(_barMoveHandle);
        _componentInsertionMarker = new Border
        {
            Background = SystemColors.HighlightBrush,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Panel.SetZIndex(_componentInsertionMarker, 20);
        InteractionSurface.Children.Add(_componentInsertionMarker);
        PreviewMouseLeftButtonDown += ComponentPointerDown;
        PreviewMouseMove += ComponentPointerMove;
        PreviewMouseLeftButtonUp += ComponentPointerUp;
        PreviewKeyDown += ComponentKeyDown;
        LostMouseCapture += ComponentCaptureLost;
        MouseEnter += ComponentBarEntered;
        MouseLeave += ComponentBarLeft;
        IsVisibleChanged += ComponentVisibilityChanged;
        SizeChanged += ComponentSizeChanged;
        Unloaded += ComponentUnloaded;
        SettingsManager.SettingsChanged += ComponentSettingsChanged;
    }

    private void DisposeComponentLayoutInput()
    {
        CancelComponentGesture();
        SettingsManager.SettingsChanged -= ComponentSettingsChanged;
        ComponentReorderCommand = null;
        SetBarDragActive(false);
    }

    private void ComponentSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ComponentSettingsChanged(sender, args)); return; }
        // Any changed setting can affect the source layer, its geometry or available commands.
        CancelComponentGesture();
        RefreshBarMoveHandle();
    }
    private void ComponentBarEntered(object sender, MouseEventArgs args) => RefreshBarMoveHandle();
    private void ComponentBarLeft(object sender, MouseEventArgs args)
    {
        RefreshBarMoveHandle();
        if (HasComponentGesture) CancelComponentGesture();
    }
    private void ComponentVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!IsVisible) CancelComponentGesture(); }
    private void ComponentSizeChanged(object sender, SizeChangedEventArgs args) { if (HasComponentGesture) CancelComponentGesture(); }
    private void ComponentUnloaded(object sender, RoutedEventArgs args) { CancelComponentGesture(); SetBarDragActive(false); }
    private void ComponentKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && HasComponentGesture) { CancelComponentGesture(); args.Handled = true; }
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        CancelComponentGesture();
        base.OnDpiChanged(oldDpi, newDpi);
    }
    private void ComponentCaptureLost(object sender, MouseEventArgs args)
    {
        if (!_finishingComponentGesture && HasComponentGesture && Mouse.Captured != this) CancelComponentGesture();
    }

    public bool IsBarDragHandleHit(DependencyObject? source)
    {
        for (var node = source; node is not null && node != this; node = ParentOf(node))
            if (ReferenceEquals(node, _barMoveHandle)) return true;
        return false;
    }
    public void SetBarDragActive(bool active) { _barDragActive = active; RefreshBarMoveHandle(); }
    private void RefreshBarMoveHandle()
    {
        if (_barMoveHandle is null) return;
        _barMoveHandle.Opacity = IsMouseOver || _barDragActive ? 1 : 0;
        var locked = PresentationSettings.TaskbarBarPositionLocked;
        _barMoveHandle.Cursor = locked ? Cursors.No : Cursors.SizeAll;
        _barMoveHandle.ToolTip = Translations.Get(locked ? "ComponentLayout.Locked" : "ComponentLayout.Move");
        System.Windows.Automation.AutomationProperties.SetName(_barMoveHandle, _barMoveHandle.ToolTip.ToString());
    }
    private void PlaceBarMoveHandle(TaskbarRestLayout layout, double crossSize)
    {
        if (_barMoveHandle is null) return;
        var placement = layout.Find(TaskbarRestComponent.DragHandle);
        _barMoveHandle.Visibility = placement is null ? Visibility.Collapsed : Visibility.Visible;
        if (placement is not { } slot) return;
        _barMoveHandle.Width = _isVertical ? crossSize : ComponentLayoutPolicy.HandleLength;
        _barMoveHandle.Height = _isVertical ? ComponentLayoutPolicy.HandleLength : crossSize;
        _barMoveHandle.VerticalAlignment = _isVertical ? VerticalAlignment.Top : VerticalAlignment.Center;
        _barMoveHandle.Margin = _isVertical ? new Thickness(2, slot.Left, 0, 0) : new Thickness(slot.Left, 0, 0, 0);
        RefreshBarMoveHandle();
    }

    private IReadOnlyList<ComponentLayoutEntry> PreviewRestEntries() =>
        _componentPreview is not null && _componentGesture?.State == RestLayoutState ? _componentPreview : EffectiveRestEntries();

    private void ApplyHoverComponentLayout()
    {
        var entries = _componentPreview is not null && _componentGesture?.State == BarLayoutState.Hover
            ? _componentPreview : ComponentLayoutPolicy.Resolve(PresentationSettings, BarLayoutState.Hover);
        var elements = HoverElements();
        var desired = entries.Select(entry => elements[entry.Component]).ToArray();
        if (!TaskbarHoverActions.Children.Cast<FrameworkElement>().SequenceEqual(desired))
        {
            TaskbarTransportButtons.Children.Clear();
            TaskbarHoverActions.Children.Clear();
            foreach (var element in desired) TaskbarHoverActions.Children.Add(element);
        }
        var visibleIndex = 0;
        foreach (var entry in entries)
        {
            var element = elements[entry.Component];
            element.Visibility = entry.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (entry.IsVisible)
            {
                var gap = visibleIndex++ == 0 ? 0 : PresentationSettings.TaskbarExperience.HoverButtonSpacingDip;
                element.Margin = _isVertical ? new Thickness(0, gap, 0, 0) : new Thickness(gap, 0, 0, 0);
            }
        }
    }
    private double HoverMinimumWidth()
    {
        var experience = PresentationSettings.TaskbarExperience;
        var count = ComponentLayoutPolicy.Resolve(PresentationSettings, BarLayoutState.Hover).Count(entry => entry.IsVisible);
        var button = TaskbarDensityMetrics.From(experience.Density).ButtonSize;
        return 11 + count * button + Math.Max(0, count - 1) * experience.HoverButtonSpacingDip;
    }
    private static DependencyObject? ParentOf(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    private (TaskbarRestComponent Component, BarLayoutState State)? HitComponent(DependencyObject? source)
    {
        var hover = HoverElements();
        var rest = RestElements();
        for (var node = source; node is not null && node != this; node = ParentOf(node))
        {
            foreach (var pair in hover) if (ReferenceEquals(node, pair.Value)) return (pair.Key, BarLayoutState.Hover);
            foreach (var pair in rest) if (ReferenceEquals(node, pair.Value)) return (pair.Key, RestLayoutState);
        }
        return null;
    }
    private void ComponentPointerDown(object sender, MouseButtonEventArgs args)
    {
        if (!IsBarPresentation || args.Handled || args.ChangedButton != MouseButton.Left) return;
        CancelComponentGesture();
        _suppressComponentRelease = false;
        var hit = HitComponent(args.OriginalSource as DependencyObject);
        if (hit is not { } source || !ComponentLayoutPolicy.CanMove(source.Component)) return;
        var entries = ComponentLayoutPolicy.Resolve(PresentationSettings, source.State).Select(entry => entry with { }).ToArray();
        if (!entries.Any(entry => entry.Component == source.Component && entry.IsVisible)) return;
        _componentGesture = new(source.State, source.Component, Array.FindIndex(entries, entry => entry.Component == source.Component), entries);
        _componentPress = args.GetPosition(this);
        _componentVertical = _isVertical;
        _componentDpi = VisualTreeHelper.GetDpi(this);
        var elements = source.State == BarLayoutState.Hover ? HoverElements() : RestElements();
        _componentStartBounds.Clear();
        foreach (var pair in elements.Where(pair => pair.Value.Visibility == Visibility.Visible && pair.Value.ActualWidth > 0 && pair.Value.ActualHeight > 0))
            _componentStartBounds[pair.Key] = pair.Value.TransformToAncestor(this).TransformBounds(new Rect(pair.Value.RenderSize));
    }
    private void ComponentPointerMove(object sender, MouseEventArgs args)
    {
        if (_componentGesture is not { } gesture) return;
        if (args.LeftButton != MouseButtonState.Pressed || !GestureStillValid()) { CancelComponentGesture(); return; }
        var position = args.GetPosition(this);
        var delta = position - _componentPress;
        if (!_componentGestureActive)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _componentGestureActive = true;
            _suppressComponentRelease = true;
            _hoverOpenTimer.Stop(); _hoverCloseTimer.Stop(); _hoverHideFallbackTimer.Stop();
            _componentPreviousFocus = Keyboard.FocusedElement;
            Focusable = true; Focus();
            if (!Mouse.Capture(this, CaptureMode.Element)) { CancelComponentGesture(); return; }
        }
        if (!IsValidComponentDrop(position)) { CancelComponentGesture(); args.Handled = true; return; }
        var primary = _isVertical ? position.Y : position.X;
        var reversed = !_isVertical && gesture.State != BarLayoutState.Hover && ResolvedTaskbarArrangement == TaskbarContentArrangement.Right;
        var candidates = gesture.OriginalEntries.Where(entry => entry.IsVisible && ComponentLayoutPolicy.CanMove(entry.Component) && _componentStartBounds.ContainsKey(entry.Component)).ToArray();
        if (candidates.Length == 0) { CancelComponentGesture(); return; }
        var target = candidates.OrderBy(entry =>
        {
            var rect = _componentStartBounds[entry.Component];
            return Math.Abs(primary - (_isVertical ? rect.Top + rect.Height / 2 : rect.Left + rect.Width / 2));
        }).First().Component;
        var targetIndex = gesture.OriginalEntries.ToList().FindIndex(entry => entry.Component == target);
        _componentGesture = gesture with { TargetIndex = targetIndex };
        var preview = gesture.OriginalEntries.ToList();
        var sourceIndex = preview.FindIndex(entry => entry.Component == gesture.Component);
        var source = preview[sourceIndex]; preview.RemoveAt(sourceIndex); preview.Insert(targetIndex, source);
        _componentPreview = preview;
        ApplyComponentPreview();
        var bounds = _componentStartBounds[target];
        var before = targetIndex <= sourceIndex;
        var marker = _componentInsertionMarker!;
        marker.Width = _isVertical ? bounds.Width : 2;
        marker.Height = _isVertical ? 2 : bounds.Height;
        marker.Margin = _isVertical ? new Thickness(bounds.Left, before ? bounds.Top : bounds.Bottom, 0, 0)
            : new Thickness(before != reversed ? bounds.Left : bounds.Right, bounds.Top, 0, 0);
        marker.Visibility = Visibility.Visible;
        args.Handled = true;
    }
    private bool GestureStillValid() => _componentGesture is { } gesture && _componentVertical == _isVertical &&
        _componentDpi.Equals(VisualTreeHelper.GetDpi(this)) && (gesture.State == BarLayoutState.Hover || gesture.State == RestLayoutState) &&
        ComponentLayoutPolicy.Resolve(PresentationSettings, gesture.State).SequenceEqual(gesture.OriginalEntries);
    private bool IsValidComponentDrop(Point point)
    {
        if (!new Rect(RenderSize).Contains(point)) return false;
        if (_componentGesture?.State != BarLayoutState.Hover) return true;
        return HoverRevealHost.TransformToAncestor(this).TransformBounds(new Rect(HoverRevealHost.RenderSize)).Contains(point);
    }
    private void ComponentPointerUp(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        if (_componentGesture is not { } gesture)
        {
            if (_suppressComponentRelease) { args.Handled = true; _suppressComponentRelease = false; }
            return;
        }
        var active = _componentGestureActive;
        var valid = GestureStillValid() && IsValidComponentDrop(args.GetPosition(this));
        var hit = HitComponent(args.OriginalSource as DependencyObject);
        CancelComponentGesture();
        _suppressComponentRelease = false;
        if (active)
        {
            args.Handled = true;
            if (valid && ComponentReorderCommand?.CanExecute(gesture) == true) ComponentReorderCommand.Execute(gesture);
        }
        else if (valid && hit?.Component == gesture.Component && (ComponentLayoutPolicy.IsMetric(gesture.Component) || gesture.Component == TaskbarRestComponent.Performance) &&
            PresentationSettings.PerformanceComponent.OpenTaskManagerOnClick)
        { args.Handled = true; RequestOpenTaskManager(); }
    }
    private void ApplyComponentPreview()
    {
        if (_componentGesture?.State == BarLayoutState.Hover)
        {
            ApplyHoverComponentLayout();
            if (_isVertical) ConfigureVerticalHoverGeometry();
        }
        else if (_isVertical) ApplyVerticalSectionGeometry(MainBorder.Height);
        else ApplyTaskbarSectionGeometry(MainBorder.Width);
    }
    private void CancelComponentGesture()
    {
        if (_finishingComponentGesture || _componentGesture is null) return;
        _finishingComponentGesture = true;
        var wasActive = _componentGestureActive;
        var wasHover = _componentGesture.State == BarLayoutState.Hover;
        _componentGesture = null; _componentPreview = null; _componentGestureActive = false;
        _componentStartBounds.Clear();
        if (_componentInsertionMarker is not null) _componentInsertionMarker.Visibility = Visibility.Collapsed;
        if (wasActive)
        {
            if (Mouse.Captured == this) Mouse.Capture(null);
            if (wasHover) { ApplyHoverComponentLayout(); if (_isVertical) ConfigureVerticalHoverGeometry(); }
            else if (_isVertical) ApplyVerticalSectionGeometry(MainBorder.Height);
            else ApplyTaskbarSectionGeometry(MainBorder.Width);
            if (_componentPreviousFocus is { } focused && Window.GetWindow(this)?.IsActive == true && ReferenceEquals(Keyboard.FocusedElement, this)) Keyboard.Focus(focused);
            _componentPreviousFocus = null;
            if (!IsMouseOver) HideTaskbarHoverLayer();
        }
        _finishingComponentGesture = false;
    }
}
