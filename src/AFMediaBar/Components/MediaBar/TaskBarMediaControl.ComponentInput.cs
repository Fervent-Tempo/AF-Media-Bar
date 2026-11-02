// 组件手势只预览视图，松手时由命令提交；整栏移动仍由宿主管理。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private double _barHandleOpacityTarget;
    private bool _componentGestureActive;
    private bool _suppressComponentRelease;
    private bool _finishingComponentGesture;
    private ComponentReorderRequest? _componentGesture;
    private bool _componentVertical;
    private DpiScale _componentDpi;
    private readonly Dictionary<TaskbarRestComponent, Rect> _componentStartBounds = new();
    private IReadOnlyList<ComponentLayoutEntry>? _componentPreview;
    private IInputElement? _componentPreviousFocus;
    private UIElement? _componentCaptureElement;
    private Point _componentScreenPress;
    private Rect _componentScreenBounds;
    private Rect _componentBarBounds;
    private bool _componentDetachedOrigin;
    private bool _componentDetachPreview;
    private bool _componentDockCandidate;
    private ComponentMoveIntentEventArgs? _componentMovePreview;
    private DispatcherTimer? _componentDragTimer;
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
            [TaskbarRestComponent.Volume] = TaskbarVolumeHoverSurface,
            [TaskbarRestComponent.PreviousTrack] = ResidentPreviousSurface,
            [TaskbarRestComponent.PlayPause] = ResidentPlayPauseSurface,
            [TaskbarRestComponent.NextTrack] = ResidentNextSurface
        };
        foreach (var surface in _metricSurfaces.Values)
            if (surface.Tag is TaskbarRestComponent component && surface.Visibility == Visibility.Visible) elements[component] = surface;
        return elements;
    }

    private void InitializeComponentLayoutInput()
    {
        var handleStyle = new Style(typeof(Border));
        handleStyle.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("TaskbarHoverButtonOverBrush")));
        handleStyle.Triggers.Add(hover);
        _barMoveHandle = new Border
        {
            Style = handleStyle,
            CornerRadius = new CornerRadius(4),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        // 与标题共享同一支前景画刷，包含自动背景判断、强制深浅色和高对比度。
        ((TextBlock)_barMoveHandle.Child).SetBinding(TextBlock.ForegroundProperty,
            new Binding(nameof(TextBlock.Foreground)) { Source = SongTitle, Mode = BindingMode.OneWay });
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
        _componentDragTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        _componentDragTimer.Tick += ComponentDragTick;
    }

    private void DisposeComponentLayoutInput()
    {
        ReleaseDetachedVisuals();
        if (_componentDragTimer is not null) { _componentDragTimer.Stop(); _componentDragTimer.Tick -= ComponentDragTick; }
        SettingsManager.SettingsChanged -= ComponentSettingsChanged;
        ComponentReorderCommand = null;
        ResetBarMoveHandle();
    }

    private void ComponentSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ComponentSettingsChanged(sender, args)); return; }
        if (args.PropertyName == nameof(AppSettings.MonitorBarProfiles) && ComponentScopeMatchesCurrent) return;
        // Any changed setting can affect the source layer, its geometry or available commands.
        CancelComponentGesture();
        RefreshBarMoveHandle();
    }
    private void ComponentBarEntered(object sender, MouseEventArgs args) => RefreshBarMoveHandle();
    private void ComponentBarLeft(object sender, MouseEventArgs args)
    {
        RefreshBarMoveHandle();
        if (_componentGesture?.State == BarLayoutState.Hover && !IsValidComponentDrop(args.GetPosition(this))) CancelComponentGesture();
    }
    private void ComponentVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!IsVisible) { CancelComponentGesture(); ResetBarMoveHandle(); }
        else RefreshBarMoveHandle();
    }
    private void ComponentSizeChanged(object sender, SizeChangedEventArgs args) { if (HasComponentGesture) CancelComponentGesture(); }
    private void ComponentUnloaded(object sender, RoutedEventArgs args) { CancelComponentGesture(); ResetBarMoveHandle(); }
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
        if (!_finishingComponentGesture && HasComponentGesture && Mouse.Captured != _componentCaptureElement) CancelComponentGesture();
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
        var targetOpacity = IsMouseOver || _barDragActive ? 1d : 0d;
        var motion = CurrentMotion;
        var immediate = !IsLoaded || !motion.UseTransitions || SystemParameters.HighContrast;
        if (immediate || _barHandleOpacityTarget != targetOpacity)
        {
            // 从正在显示的值接续，终态使用基础值，不依赖动画完成回调。
            var currentOpacity = _barMoveHandle.Opacity;
            _barHandleOpacityTarget = targetOpacity;
            _barMoveHandle.BeginAnimation(OpacityProperty, null);
            _barMoveHandle.Opacity = targetOpacity;
            if (!immediate && Math.Abs(currentOpacity - targetOpacity) > 0.001)
                _barMoveHandle.BeginAnimation(OpacityProperty, new DoubleAnimation
                {
                    From = currentOpacity,
                    To = targetOpacity,
                    Duration = motion.FastDuration,
                    EasingFunction = CreateEaseOut(),
                    FillBehavior = FillBehavior.Stop
                }, HandoffBehavior.SnapshotAndReplace);
        }
        var locked = PresentationSettings.TaskbarBarPositionLocked;
        if (_barDragActive) _barMoveHandle.SetResourceReference(Border.BackgroundProperty, "TaskbarHoverButtonPressedBrush");
        else _barMoveHandle.ClearValue(Border.BackgroundProperty);
        _barMoveHandle.Cursor = locked ? Cursors.No : Cursors.SizeAll;
        _barMoveHandle.ToolTip = Translations.Get(locked ? "ComponentLayout.Locked" : "ComponentLayout.Move");
        System.Windows.Automation.AutomationProperties.SetName(_barMoveHandle, _barMoveHandle.ToolTip.ToString());
    }
    private void ResetBarMoveHandle()
    {
        _barDragActive = false;
        _barHandleOpacityTarget = 0;
        if (_barMoveHandle is null) return;
        _barMoveHandle.BeginAnimation(OpacityProperty, null);
        _barMoveHandle.Opacity = 0;
        _barMoveHandle.ClearValue(Border.BackgroundProperty);
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
        BeginComponentGesture(source.Component, source.State, this, args);
    }
    private void DetachedPointerDown(TaskbarRestComponent component, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || PresentationSettings.TaskbarBarPositionLocked) return;
        if (_detachedVisuals.TryGetValue(component, out var visual)) BeginComponentGesture(component, RestLayoutState, visual.View, args);
    }
    private void BeginComponentGesture(TaskbarRestComponent component, BarLayoutState state, UIElement capture, MouseButtonEventArgs args)
    {
        CancelComponentGesture();
        _suppressComponentRelease = false;
        var source = (Component: component, State: state);
        var entries = ComponentLayoutPolicy.Resolve(PresentationSettings, source.State).Select(entry => entry with { }).ToArray();
        if (!entries.Any(entry => entry.Component == source.Component && entry.IsVisible)) return;
        _componentGesture = new(source.State, source.Component, Array.FindIndex(entries, entry => entry.Component == source.Component), entries)
        { Target = ComponentTarget, MonitorDeviceId = ComponentMonitorDeviceId };
        _componentCaptureElement = capture;
        if (!Classes.Interop.NativeMethods.GetCursorPos(out var cursor)) { _componentGesture = null; return; }
        _componentScreenPress = new Point(cursor.X, cursor.Y);
        _componentDetachedOrigin = capture != this;
        _componentDetachPreview = _componentDetachedOrigin;
        _componentDockCandidate = false;
        _componentBarBounds = IsVisible && !_isRestLayerEmpty && Opacity > 0 ? new Rect(PointToScreen(new Point()), new Size(ActualWidth * VisualTreeHelper.GetDpi(this).DpiScaleX,
            ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY)) : Rect.Empty;
        var componentElement = state == BarLayoutState.Hover ? HoverElements().GetValueOrDefault(component) : GetComponentVisual(component) as FrameworkElement;
        if (componentElement is null) { _componentGesture = null; return; }
        var componentDpi = VisualTreeHelper.GetDpi(componentElement);
        _componentScreenBounds = new Rect(componentElement.PointToScreen(new Point()),
            new Size(componentElement.ActualWidth * componentDpi.DpiScaleX, componentElement.ActualHeight * componentDpi.DpiScaleY));
        if (component == TaskbarRestComponent.Performance)
        {
            var size = GetComponentSize(component);
            _componentScreenBounds.Size = new Size(size.Width * componentDpi.DpiScaleX, size.Height * componentDpi.DpiScaleY);
        }
        _componentVertical = _isVertical;
        _componentDpi = VisualTreeHelper.GetDpi(this);
        _componentDragTimer?.Start();
        var elements = source.State == BarLayoutState.Hover ? HoverElements() : RestElements();
        _componentStartBounds.Clear();
        foreach (var pair in elements.Where(pair => !IsDetachedElement(pair.Value) && pair.Value.Visibility == Visibility.Visible && pair.Value.ActualWidth > 0 && pair.Value.ActualHeight > 0))
            _componentStartBounds[pair.Key] = pair.Value.TransformToAncestor(this).TransformBounds(new Rect(pair.Value.RenderSize));
    }
    private void ComponentPointerMove(object sender, MouseEventArgs args)
    {
        if (_componentGesture is not { } gesture) return;
        if (args.LeftButton != MouseButtonState.Pressed || !GestureStillValid()) { CancelComponentGesture(); return; }
        if (!Classes.Interop.NativeMethods.GetCursorPos(out var cursor)) return;
        var screen = new Point(cursor.X, cursor.Y);
        var position = PointFromScreen(screen);
        var delta = screen - _componentScreenPress;
        if (!_componentGestureActive)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * _componentDpi.DpiScaleX &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * _componentDpi.DpiScaleY) return;
            _componentGestureActive = true;
            _suppressComponentRelease = true;
            _hoverOpenTimer.Stop(); _hoverCloseTimer.Stop(); _hoverHideFallbackTimer.Stop();
            _componentPreviousFocus = Keyboard.FocusedElement;
            // 先接管按钮捕获再转移焦点，避免 ButtonBase 失焦释放捕获时取消排序。
            // 保留内部容器的真实命中；Element 捕获会让其误收 MouseLeave 并清理当前排序。
            var capture = _componentCaptureElement!;
            if (!Mouse.Capture(capture, CaptureMode.SubTree)) { CancelComponentGesture(); return; }
            if (_componentGesture is not null) { capture.Focusable = true; capture.Focus(); }
            if (_componentGesture is null)
            {
                if (Mouse.Captured == capture) Mouse.Capture(null);
                return;
            }
        }
        if (gesture.State != BarLayoutState.Hover && !PresentationSettings.TaskbarBarPositionLocked)
        {
            var outer = _componentBarBounds;
            if (!outer.IsEmpty) outer.Inflate(12 * _componentDpi.DpiScaleX, 12 * _componentDpi.DpiScaleY);
            if (_componentDetachedOrigin)
            {
                var dockBounds = _componentBarBounds;
                if (!dockBounds.IsEmpty) dockBounds.Inflate((_componentDockCandidate ? 20 : 12) * _componentDpi.DpiScaleX,
                    (_componentDockCandidate ? 20 : 12) * _componentDpi.DpiScaleY);
                _componentDockCandidate = !dockBounds.IsEmpty && dockBounds.Contains(screen);
            }
            else if (outer.IsEmpty || !outer.Contains(screen)) _componentDetachPreview = true;
            else if (_componentBarBounds.Contains(screen))
            {
                _componentDetachPreview = false;
                if (_componentMovePreview is { } previousMove)
                {
                    _componentMovePreview = null;
                    ComponentMoveIntent?.Invoke(this, new(previousMove.Component, previousMove.ScreenBounds, previousMove.State,
                        previousMove.OriginalEntries, previousMove.TargetIndex, false, true, true));
                }
            }
            if (_componentDetachPreview)
            {
                if (_componentDockCandidate)
                {
                    var localPointer = PointFromScreen(screen);
                    var localPrimary = _isVertical ? localPointer.Y : localPointer.X;
                    var dockTarget = gesture.OriginalEntries.Where(entry => entry.IsVisible && ComponentLayoutPolicy.CanMove(entry.Component) &&
                        entry.Component != gesture.Component && _componentStartBounds.ContainsKey(entry.Component))
                        .OrderBy(entry =>
                        {
                            var rectangle = _componentStartBounds[entry.Component];
                            return Math.Abs(localPrimary - (_isVertical ? rectangle.Top + rectangle.Height / 2 : rectangle.Left + rectangle.Width / 2));
                        })
                        .FirstOrDefault();
                    if (dockTarget is not null)
                    {
                        gesture = gesture with { TargetIndex = gesture.OriginalEntries.ToList().FindIndex(entry => entry.Component == dockTarget.Component) };
                        _componentGesture = gesture;
                    }
                }
                var screenBounds = _componentScreenBounds;
                screenBounds.Offset(screen - _componentScreenPress);
                _componentMovePreview = new(gesture.Component, screenBounds, gesture.State, gesture.OriginalEntries,
                    gesture.TargetIndex, _componentDockCandidate, false);
                ComponentMoveIntent?.Invoke(this, _componentMovePreview);
                _componentDockCandidate = _componentMovePreview.CanDock;
                if (_componentInsertionMarker is { } insertion)
                {
                    insertion.Visibility = _componentDockCandidate ? Visibility.Visible : Visibility.Collapsed;
                    insertion.Width = _isVertical ? ActualWidth : 2;
                    insertion.Height = _isVertical ? 2 : ActualHeight;
                    var selected = gesture.OriginalEntries.ElementAtOrDefault(gesture.TargetIndex);
                    var targetBounds = selected is not null && _componentStartBounds.TryGetValue(selected.Component, out var rectangle) ? rectangle : new Rect(RenderSize);
                    var originalIndex = gesture.OriginalEntries.ToList().FindIndex(entry => entry.Component == gesture.Component);
                    var isBefore = gesture.TargetIndex <= originalIndex;
                    var isReversed = !_isVertical && ResolvedTaskbarArrangement == TaskbarContentArrangement.Right;
                    insertion.Margin = _isVertical ? new Thickness(targetBounds.Left, isBefore ? targetBounds.Top : targetBounds.Bottom, 0, 0)
                        : new Thickness(isBefore != isReversed ? targetBounds.Left : targetBounds.Right, targetBounds.Top, 0, 0);
                }
                args.Handled = true;
                return;
            }
        }
        if (!IsValidComponentDrop(position)) { if (gesture.State == BarLayoutState.Hover) CancelComponentGesture(); args.Handled = true; return; }
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
        if (active && _componentDetachPreview && _componentMovePreview is { } move)
        {
            var finalBounds = move.ScreenBounds;
            var finalDock = move.CanDock;
            if (Classes.Interop.NativeMethods.GetCursorPos(out var released))
            {
                finalBounds = _componentScreenBounds;
                finalBounds.Offset(new Vector(released.X - _componentScreenPress.X, released.Y - _componentScreenPress.Y));
                var dockBounds = _componentBarBounds;
                if (!dockBounds.IsEmpty) dockBounds.Inflate(20 * _componentDpi.DpiScaleX, 20 * _componentDpi.DpiScaleY);
                finalDock &= !dockBounds.IsEmpty && dockBounds.Contains(new Point(released.X, released.Y));
            }
            _componentMovePreview = null;
            _finishingComponentGesture = true;
            try
            {
                ComponentMoveIntent?.Invoke(this, new(move.Component, finalBounds, move.State, move.OriginalEntries,
                move.TargetIndex, finalDock, true));
            }
            finally { _finishingComponentGesture = false; CancelComponentGesture(); _suppressComponentRelease = false; }
            args.Handled = true;
            return;
        }
        var hit = HitComponent(args.OriginalSource as DependencyObject);
        var valid = GestureStillValid() && (_componentDetachedOrigin
            ? hit?.Component == gesture.Component : IsValidComponentDrop(args.GetPosition(this)));
        if (active)
        {
            args.Handled = true;
            // 先提交再撤去预览；设置通知可能同步重入，不能在提交中途恢复旧布局。
            _finishingComponentGesture = true;
            try
            {
                if (valid && ComponentReorderCommand?.CanExecute(gesture) == true) ComponentReorderCommand.Execute(gesture);
            }
            finally
            {
                _finishingComponentGesture = false;
                CancelComponentGesture();
                _suppressComponentRelease = false;
            }
            return;
        }
        CancelComponentGesture();
        _suppressComponentRelease = false;
        if (valid && hit?.Component == gesture.Component && (ComponentLayoutPolicy.IsMetric(gesture.Component) || gesture.Component == TaskbarRestComponent.Performance) &&
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
        _componentDragTimer?.Stop();
        if (_componentMovePreview is { } move)
        {
            _componentMovePreview = null;
            ComponentMoveIntent?.Invoke(this, new(move.Component, move.ScreenBounds, move.State, move.OriginalEntries,
                move.TargetIndex, move.Dock, true, true));
        }
        _componentStartBounds.Clear();
        if (_componentInsertionMarker is not null) _componentInsertionMarker.Visibility = Visibility.Collapsed;
        if (wasActive)
        {
            if (Mouse.Captured == _componentCaptureElement) Mouse.Capture(null);
            if (wasHover) { ApplyHoverComponentLayout(); if (_isVertical) ConfigureVerticalHoverGeometry(); }
            else if (_isVertical) ApplyVerticalSectionGeometry(MainBorder.Height);
            else ApplyTaskbarSectionGeometry(MainBorder.Width);
            if (_componentCaptureElement is { } capture && _componentPreviousFocus is { } focused && Window.GetWindow(capture)?.IsActive == true &&
                ReferenceEquals(Keyboard.FocusedElement, capture)) Keyboard.Focus(focused);
            _componentPreviousFocus = null;
            if (!IsMouseOver) HideTaskbarHoverLayer();
        }
        _finishingComponentGesture = false;
    }
    public void CancelComponentPlacement() => CancelComponentGesture();
    public void CompleteComponentPlacement()
    {
        if (_componentGestureActive && Mouse.LeftButton == MouseButtonState.Released) ComponentPointerUp(this,
            new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
    }
    private void ComponentDragTick(object? sender, EventArgs args)
    {
        if (_componentGesture is null) { _componentDragTimer?.Stop(); return; }
        if ((Classes.Interop.NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0) { CancelComponentGesture(); return; }
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            if (_componentGestureActive) CompleteComponentPlacement(); else CancelComponentGesture();
            return;
        }
        // 处理器会设置 Handled；即使直接调用，也必须为合成参数指定路由事件。
        ComponentPointerMove(this, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
    }
}
