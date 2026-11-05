// 组件手势只预览视图，松手时由命令提交；整栏移动仍由宿主管理。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private Border? _barMoveHandle;
    // 控件拥有浮层、取色会话与离开计时器；注入的共享采样器只借用。
    // 显示期间订阅布局和宿主位移，关闭时解除订阅并取消采样。
    private Popup? _barMovePopup;
    private DispatcherTimer? _barMoveTimer;
    private Window? _barMoveOwner;
    private Point? _barMoveScreenOrigin;
    private Size _barMoveTargetSize;
    private AdaptiveForegroundSamplingSession? _barMoveContrastSampling;
    private readonly ColorTransitionBrush _barMoveForeground = new();
    private PlayerForegroundDecision? _barMoveForegroundDecision;
    private long _barMoveNextContrastTick;
    private long? _barMoveOutsideSince;
    private bool _barMovePressed;
    private bool _barMoveDisposed;
    private Border? _componentInsertionMarker;
    private bool _barDragActive;
    private double _barHandleOpacityTarget;
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
    public event MouseButtonEventHandler? BarDragHandlePressed;
    internal event MouseEventHandler? BarDragHandleMoved;
    internal event MouseButtonEventHandler? BarDragHandleReleased;

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
        handleStyle.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("BarMoveHandleHoverBrush")));
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
        ApplyBarMoveContrast(null);
        _barMoveHandle.MouseEnter += (_, _) => RefreshBarMoveHandle();
        _barMoveHandle.PreviewMouseLeftButtonDown += (_, args) =>
        {
            _barMovePressed = !PresentationSettings.TaskbarBarPositionLocked;
            BarDragHandlePressed?.Invoke(this, args);
        };
        _barMoveHandle.PreviewMouseMove += (_, args) => BarDragHandleMoved?.Invoke(this, args);
        _barMoveHandle.PreviewMouseLeftButtonUp += (_, args) =>
        {
            _barMovePressed = false;
            BarDragHandleReleased?.Invoke(this, args);
        };
        _barMovePopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = true,
            PlacementTarget = MainBorder,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceBarMovePopup,
            Child = _barMoveHandle,
            UseLayoutRounding = true
        };
        _barMoveTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _barMoveTimer.Tick += BarMoveTimer_Tick;
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
        if (_barMoveDisposed) return;
        _barMoveDisposed = true;
        CancelComponentGesture();
        SettingsManager.SettingsChanged -= ComponentSettingsChanged;
        ComponentReorderCommand = null;
        ResetBarMoveHandle();
        _barMoveContrastSampling?.Dispose();
        _barMoveContrastSampling = null;
        if (_barMoveTimer is not null) _barMoveTimer.Tick -= BarMoveTimer_Tick;
        if (_barMovePopup is not null) _barMovePopup.Child = null;
        BarDragHandlePressed = null;
        BarDragHandleMoved = null;
        BarDragHandleReleased = null;
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
        if (HasComponentGesture && !IsValidComponentDrop(args.GetPosition(this))) CancelComponentGesture();
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
        if (!_finishingComponentGesture && HasComponentGesture && Mouse.Captured != this) CancelComponentGesture();
    }

    public bool IsBarDragHandleHit(DependencyObject? source)
    {
        for (var node = source; node is not null && node != this; node = ParentOf(node))
            if (ReferenceEquals(node, _barMoveHandle)) return true;
        return false;
    }
    public void SetBarDragActive(bool active)
    {
        _barDragActive = active;
        RefreshBarMoveHandle();
    }
    private void RefreshBarMoveHandle()
    {
        if (_barMoveHandle is null) return;
        var canShow = !_barMoveDisposed && IsLoaded && IsVisible && IsHitTestVisible && IsBarPresentation &&
            !_isHostVisibilitySuspended && (!_isPlacementMotionActive || _barDragActive) &&
            _backgroundPruneLevel < MemoryPruneLevel.DisplayOff && _barMoveHandle.Visibility == Visibility.Visible;
        var pointerActive = IsMouseOver || _barMoveHandle.IsMouseOver || _barDragActive ||
            _barMovePressed && Mouse.LeftButton == MouseButtonState.Pressed;
        if (!canShow || !pointerActive && _barMoveOutsideSince is { } since && Environment.TickCount64 - since >= 150)
        {
            ResetBarMoveHandle();
            return;
        }
        if (pointerActive) _barMoveOutsideSince = null;
        else _barMoveOutsideSince ??= Environment.TickCount64;
        if (!canShow || _barMovePopup is null || !pointerActive && !_barMovePopup.IsOpen) return;
        if (!_barMovePopup.IsOpen)
        {
            _barMoveScreenOrigin = MainBorder.PointToScreen(new Point());
            _barMoveTargetSize = MainBorder.RenderSize;
            _barMoveOwner = Window.GetWindow(this);
            if (_barMoveOwner is not null) _barMoveOwner.LocationChanged += BarMovePlacementChanged;
            LayoutUpdated += BarMovePlacementChanged;
            _barMovePopup.IsOpen = true;
            _barMoveTimer?.Start();
        }
        var targetOpacity = 1d;
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
        if (_barDragActive) _barMoveHandle.SetResourceReference(Border.BackgroundProperty, "BarMoveHandlePressedBrush");
        else _barMoveHandle.ClearValue(Border.BackgroundProperty);
        _barMoveHandle.Cursor = locked ? Cursors.No : Cursors.SizeAll;
        _barMoveHandle.ToolTip = Translations.Get(locked ? "ComponentLayout.Locked" : "ComponentLayout.Move");
        System.Windows.Automation.AutomationProperties.SetName(_barMoveHandle, _barMoveHandle.ToolTip.ToString());
        RefreshBarMoveContrast();
    }
    private void ResetBarMoveHandle()
    {
        _barDragActive = false;
        _barMovePressed = false;
        _barMoveOutsideSince = null;
        _barMoveScreenOrigin = null;
        LayoutUpdated -= BarMovePlacementChanged;
        if (_barMoveOwner is not null) _barMoveOwner.LocationChanged -= BarMovePlacementChanged;
        _barMoveOwner = null;
        _barMoveContrastSampling?.Invalidate(clearDecision: false);
        _barMoveNextContrastTick = 0;
        _barMoveForeground.Finish();
        _barMoveTimer?.Stop();
        if (_barMovePopup is not null) _barMovePopup.IsOpen = false;
        _barHandleOpacityTarget = 0;
        if (_barMoveHandle is null) return;
        _barMoveHandle.BeginAnimation(OpacityProperty, null);
        _barMoveHandle.Opacity = 0;
        _barMoveHandle.ClearValue(Border.BackgroundProperty);
    }
    private void PlaceBarMoveHandle(TaskbarRestLayout layout, double crossSize)
    {
        if (_barMoveHandle is null) return;
        _barMoveHandle.Visibility = layout.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        _barMoveHandle.Width = _isVertical ? crossSize : ComponentLayoutPolicy.HandleLength;
        _barMoveHandle.Height = _isVertical ? ComponentLayoutPolicy.HandleLength : crossSize;
        RefreshBarMovePopupPosition();
        RefreshBarMoveHandle();
    }

    private CustomPopupPlacement[] PlaceBarMovePopup(Size popupSize, Size targetSize, Point offset)
    {
        if (_isVertical)
            return [new(new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height), PopupPrimaryAxis.Horizontal),
                new(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height), PopupPrimaryAxis.Horizontal),
                new(new Point(targetSize.Width, Math.Max(0, targetSize.Height - popupSize.Height)), PopupPrimaryAxis.Vertical),
                new(new Point(-popupSize.Width, Math.Max(0, targetSize.Height - popupSize.Height)), PopupPrimaryAxis.Vertical)];
        var right = new CustomPopupPlacement(new Point(targetSize.Width, (targetSize.Height - popupSize.Height) / 2), PopupPrimaryAxis.Vertical);
        var left = new CustomPopupPlacement(new Point(-popupSize.Width, (targetSize.Height - popupSize.Height) / 2), PopupPrimaryAxis.Vertical);
        var fromRight = ResolvedTaskbarArrangement == TaskbarContentArrangement.Right;
        var end = fromRight ? 0 : Math.Max(0, targetSize.Width - popupSize.Width);
        return [fromRight ? left : right, fromRight ? right : left,
            new(new Point(end, -popupSize.Height), PopupPrimaryAxis.Horizontal),
            new(new Point(end, targetSize.Height), PopupPrimaryAxis.Horizontal)];
    }

    private void RefreshBarMovePopupPosition()
    {
        if (_barMovePopup is not { IsOpen: true }) return;
        // WPF Popup 不随父窗口移动；只在位置或布局变化时触发重新定位。
        _barMovePopup.HorizontalOffset = _barMovePopup.HorizontalOffset == 0 ? 0.01 : 0;
    }

    private void BarMoveTimer_Tick(object? sender, EventArgs args) => RefreshBarMoveHandle();

    private void BarMovePlacementChanged(object? sender, EventArgs args)
    {
        if (_barMovePopup is not { IsOpen: true }) return;
        var origin = MainBorder.PointToScreen(new Point());
        var targetSize = MainBorder.RenderSize;
        if (_barMoveScreenOrigin == origin && _barMoveTargetSize == targetSize) return;
        _barMoveScreenOrigin = origin;
        _barMoveTargetSize = targetSize;
        _barMoveContrastSampling?.Invalidate(clearDecision: false);
        _barMoveNextContrastTick = 0;
        // 与主体的布局或 HWND 位移同步，不等待另一个计时器帧。
        RefreshBarMovePopupPosition();
        RefreshBarMoveContrast();
    }

    internal void BindBarMoveContrastSampler(ScreenBackgroundSampler sampler)
    {
        if (_barMoveDisposed) return;
        _barMoveContrastSampling?.Dispose();
        _barMoveContrastSampling = new AdaptiveForegroundSamplingSession(sampler, Dispatcher,
            GetBarMoveContrastRegions, ApplyBarMoveContrast);
        _barMoveNextContrastTick = 0;
        RefreshBarMoveContrast();
    }

    private IReadOnlyList<Int32Rect> GetBarMoveContrastRegions()
    {
        if (_barMoveDisposed || _barMovePopup is not { IsOpen: true } || _barMoveHandle is null ||
            !TryGetPhysicalBounds(_barMoveHandle, out var bounds)) return [];
        var dpi = VisualTreeHelper.GetDpi(_barMoveHandle);
        var scale = _isVertical ? dpi.DpiScaleX : dpi.DpiScaleY;
        // 只取浮层外的两条留白，避开点阵和整块悬停/按下底色。
        var padding = Math.Max(1, (int)Math.Ceiling(2 * scale)) * 2;
        var area = new Int32Rect(bounds.X - padding, bounds.Y - padding,
            bounds.Width + padding * 2, bounds.Height + padding * 2);
        return ForegroundSampleRegionPolicy.Create(bounds, area, _isVertical, scale);
    }

    private void RefreshBarMoveContrast()
    {
        if (_barMoveDisposed || _barMovePopup is not { IsOpen: true } ||
            Environment.TickCount64 < _barMoveNextContrastTick) return;
        if (SystemParameters.HighContrast)
        {
            _barMoveContrastSampling?.Invalidate(clearDecision: true);
            ApplyBarMoveContrast(null);
        }
        else _barMoveContrastSampling?.RequestRefresh();
        _barMoveNextContrastTick = Environment.TickCount64 + 250;
    }

    private void ApplyBarMoveContrast(PlayerForegroundDecision? decision)
    {
        if (_barMoveDisposed || _barMoveHandle is not { Child: TextBlock glyph } handle) return;
        var animate = _barMoveForegroundDecision is not null && decision is not null && _barMovePopup is { IsOpen: true };
        if (decision is not null) _barMoveForegroundDecision = decision;
        var foreground = HandleIndicatorAppearance.Resolve(_barMoveForegroundDecision);
        glyph.Foreground = _barMoveForeground.Apply(foreground, ColorTransitionBrush.ResolveDuration(animate));
        // 点阵和交互底色都使用栏外背景的决定，不借用媒体栏材质或文字的配色。
        var palette = TaskbarHoverPalettePolicy.Resolve(((SolidColorBrush)foreground).Color);
        handle.Resources["BarMoveHandleHoverBrush"] = new SolidColorBrush(palette.ButtonHover);
        handle.Resources["BarMoveHandlePressedBrush"] = new SolidColorBrush(palette.ButtonPressed);
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
            // 先接管按钮捕获再转移焦点，避免 ButtonBase 失焦释放捕获时取消排序。
            // 保留内部容器的真实命中；Element 捕获会让其误收 MouseLeave 并清理当前排序。
            if (!Mouse.Capture(this, CaptureMode.SubTree)) { CancelComponentGesture(); return; }
            if (_componentGesture is not null) { Focusable = true; Focus(); }
            if (_componentGesture is null)
            {
                if (IsMouseCaptured) ReleaseMouseCapture();
                return;
            }
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
