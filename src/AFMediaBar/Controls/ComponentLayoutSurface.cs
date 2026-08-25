using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Controls;

internal sealed class LayoutCommandEventArgs(
    MediaCommandKind command,
    FrameworkElement? placementTarget) : EventArgs
{
    internal MediaCommandKind Command { get; } = command;
    internal FrameworkElement? PlacementTarget { get; } = placementTarget;
}

internal sealed class LayoutMetricsEventArgs(bool openTaskManager) : EventArgs
{
    internal bool OpenTaskManager { get; } = openTaskManager;
}

/// <summary>
/// 组件将设备/音量滚轮连同自身锚点转发给窗口，以便弹窗定位不依赖旧静态控件。
/// Forwards device/volume wheel input with the originating anchor so popups do not depend on legacy static controls.
/// </summary>
internal sealed class LayoutWheelEventArgs(
    MediaCommandKind command,
    int delta,
    FrameworkElement placementTarget) : EventArgs
{
    internal MediaCommandKind Command { get; } = command;
    internal int Delta { get; } = delta;
    internal FrameworkElement PlacementTarget { get; } = placementTarget;
}

/// <summary>
/// 设计模式下把真实组件的选择与拖放回传给设置编辑器；组件本身不修改布局档案。
/// In design mode, returns selection and drag gestures from real widgets; the surface never mutates layout profiles.
/// </summary>
internal sealed class LayoutDesignElementEventArgs(
    string instanceId,
    DependencyObject source,
    bool isContainer = false) : EventArgs
{
    internal string InstanceId { get; } = instanceId;
    internal DependencyObject Source { get; } = source;
    internal bool IsContainer { get; } = isContainer;
}

/// <summary>
/// 设计模式把真实容器的拖放目标回传给设置编辑器；编辑器负责校验并提交不可变档案。
/// Reports a real container drop target to the settings editor; the editor validates and commits the immutable profile.
/// </summary>
internal sealed class LayoutDesignDropEventArgs(
    string containerId,
    LayoutSlotKind slotKind,
    FrameworkElement target,
    DragEventArgs dragEventArgs) : EventArgs
{
    internal string ContainerId { get; } = containerId;
    internal LayoutSlotKind SlotKind { get; } = slotKind;
    internal FrameworkElement Target { get; } = target;
    internal DragEventArgs DragEventArgs { get; } = dragEventArgs;
}

internal sealed class LayoutDesignPreviewStateEventArgs(
    string containerId,
    bool pointerNear) : EventArgs
{
    internal string ContainerId { get; } = containerId;
    internal bool PointerNear { get; } = pointerNear;
}

/// <summary>
/// 设计模式四边 Thumb 的缩放请求；DeltaDip 是该边累计的 DIP 增量，编辑器负责换算为整数格。
/// Design-mode four-edge resize request; DeltaDip is the cumulative DIP delta, converted to integer cells by the editor.
/// </summary>
internal sealed class LayoutDesignResizeEventArgs(
    string instanceId,
    LayoutEdge edge,
    double deltaDip) : EventArgs
{
    internal string InstanceId { get; } = instanceId;
    internal LayoutEdge Edge { get; } = edge;
    internal double DeltaDip { get; } = deltaDip;
}

/// <summary>
/// 设计模式右键删除请求；编辑器用命中坐标弹出名称+删除小菜单。
/// Design-mode right-click delete request; the editor pops the name-plus-delete menu at the hit position.
/// </summary>
internal sealed class LayoutDesignDeleteEventArgs(
    string instanceId,
    FrameworkElement source,
    Point position) : EventArgs
{
    internal string InstanceId { get; } = instanceId;
    internal FrameworkElement Source { get; } = source;
    internal Point Position { get; } = position;
}

/// <summary>
/// 根据不可变布局档案生成运行时与设置预览共用的 WPF 组件树；不读取注册表、不创建系统会话，业务动作通过事件交给窗口协调器。
/// Builds the shared runtime/settings-preview WPF tree from an immutable layout profile without registry or system-session access; actions return to the window coordinator through events.
/// </summary>
internal sealed class ComponentLayoutSurface : Grid, IDisposable
{
    private const int MaximumMediaTextLines = 2;
    internal static readonly DependencyProperty IsInteractiveElementProperty =
        DependencyProperty.RegisterAttached(
            "IsInteractiveElement",
            typeof(bool),
            typeof(ComponentLayoutSurface),
            new FrameworkPropertyMetadata(false));
    private static readonly DependencyProperty TransitionKeyProperty =
        DependencyProperty.RegisterAttached(
            "TransitionKey",
            typeof(string),
            typeof(ComponentLayoutSurface),
            new FrameworkPropertyMetadata(null));
    private static readonly DependencyProperty IsTransitionBoundaryProperty =
        DependencyProperty.RegisterAttached(
            "IsTransitionBoundary",
            typeof(bool),
            typeof(ComponentLayoutSurface),
            new FrameworkPropertyMetadata(false));
    private static readonly DependencyProperty TransitionProgressProperty =
        DependencyProperty.RegisterAttached(
            "TransitionProgress",
            typeof(double),
            typeof(ComponentLayoutSurface),
            new FrameworkPropertyMetadata(0d));

    private readonly Dictionary<string, FrameworkElement> _widgetViews =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _designElements =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Adorner> _designAdorners =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Adorner> _designBoundaryAdorners =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContainerVisual> _containerViews =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaTextKind> _mediaTextKinds =
        new(StringComparer.Ordinal);
    private readonly ComponentSkinService _componentSkinService = new();
    private readonly Dictionary<string, MarqueeState> _marqueeStates =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricViewState> _metricStates =
        new(StringComparer.Ordinal);
    private readonly float[] _spectrum = new float[AudioMonitorService.BandCount];
    private readonly DispatcherTimer _marqueeTimer;
    private readonly DispatcherTimer _pointerStateTimer;
    private readonly DispatcherTimer _hoverButtonTimer;
    private LayoutProfile? _profile;
    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Disconnected;
    private string _metricsText = string.Empty;
    private bool _pointerNear;
    private bool _designMode;
    private bool _useMenuThemeForContent;
    private string? _designSelectedInstanceId;
    private string? _designPressInstanceId;
    private Point _designPressPoint;
    private DependencyObject? _designPressSource;
    private int _gapDip;
    private bool _disposed;

    internal ComponentLayoutSurface()
    {
        // 透明背景让整块条带都参与 WPF 命中测试；靠近距离可能落在组件空白区，不能只依赖子控件收到 MouseMove。
        // A transparent background keeps the whole strip hit-testable; proximity can fall in empty space and must not depend on child widgets.
        Background = Brushes.Transparent;
        _marqueeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(260),
            DispatcherPriority.Render,
            OnMarqueeTimerTick,
            Dispatcher);
        _marqueeTimer.Stop();
        _pointerStateTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(40),
            DispatcherPriority.Input,
            OnPointerStateTimerTick,
            Dispatcher);
        _pointerStateTimer.Stop();
        _hoverButtonTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(140),
            DispatcherPriority.Input,
            OnHoverButtonTimerTick,
            Dispatcher);
        _hoverButtonTimer.Stop();
        MouseEnter += Surface_OnMouseEnter;
        MouseMove += Surface_OnMouseMove;
        MouseLeave += Surface_OnMouseLeave;
    }

    internal event EventHandler<LayoutCommandEventArgs>? CommandRequested;
    internal event EventHandler<LayoutMetricsEventArgs>? MetricsRequested;
    internal event EventHandler<LayoutWheelEventArgs>? WheelRequested;
    internal event EventHandler? SourceRequested;
    internal event EventHandler<LayoutDesignElementEventArgs>? DesignElementSelected;
    internal event EventHandler<LayoutDesignElementEventArgs>? DesignElementDragRequested;
    internal event EventHandler<LayoutDesignDropEventArgs>? DesignDropTargetDragOver;
    internal event EventHandler<LayoutDesignDropEventArgs>? DesignDropRequested;
    internal event EventHandler<LayoutDesignPreviewStateEventArgs>? DesignPreviewStateChanged;
    internal event EventHandler<LayoutDesignResizeEventArgs>? DesignResizeRequested;
    internal event EventHandler<LayoutDesignDeleteEventArgs>? DesignDeleteRequested;

    internal void SetUseMenuThemeForContent(bool useMenuTheme) =>
        _useMenuThemeForContent = useMenuTheme;

    internal static bool GetIsInteractiveElement(DependencyObject element) =>
        (bool)element.GetValue(IsInteractiveElementProperty);

    private static void SetIsInteractiveElement(DependencyObject element, bool value) =>
        element.SetValue(IsInteractiveElementProperty, value);

    internal void Apply(LayoutProfile profile, bool pointerNear)
    {
        _profile = profile;
        _pointerNear = pointerNear;
        _gapDip = Math.Clamp(profile.Surface.GapDip, 0, 32);
        _widgetViews.Clear();
        ClearDesignAdorners();
        _designElements.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
        _marqueeTimer.Stop();
        _pointerStateTimer.Stop();
        Children.Clear();

        var root = BuildAbsoluteLayout(profile);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        var vertical = profile.LayoutMode == PlayerLayoutMode.Vertical;
        var lengthScale = Math.Clamp(profile.Surface.LengthScalePercent, 70, 125) / 100d;
        var thicknessScale = Math.Clamp(profile.Surface.ThicknessScalePercent, 70, 125) / 100d;
        root.LayoutTransform = new ScaleTransform(
            vertical ? thicknessScale : lengthScale,
            vertical ? lengthScale : thicknessScale);
        // schema 4 外框尺寸由网格联合边界决定；Surface.WidthDip/HeightDip 已被迁移清空，不再作为事实来源。
        // Schema-4 frame size comes from the grid union; Surface DIP overrides were cleared by migration.
        Width = double.NaN;
        Height = double.NaN;
        ClipToBounds = false;
        Children.Add(root);
        RefreshAllData();
        if (_marqueeStates.Count > 0)
        {
            _marqueeTimer.Start();
        }
    }

    /// <summary>
    /// 切换设置预览的设计模式；该模式只改变输入处理，不改变运行时布局和视觉。
    /// Enables editor input handling without changing the runtime layout or visuals.
    /// </summary>
    internal void SetDesignMode(bool enabled)
    {
        _designMode = enabled;
        if (enabled)
        {
            _pointerStateTimer.Stop();
        }
    }

    /// <summary>
    /// 设计模式下为元素挂右键删除；命中后把坐标回传编辑器，由编辑器弹出名称+删除菜单。
    /// In design mode, wires right-click delete; the hit is reported to the editor to pop the name-plus-delete menu.
    /// </summary>
    private void AttachDesignDeleteHandler(FrameworkElement view, string instanceId)
    {
        if (!_designMode)
        {
            return;
        }

        view.PreviewMouseRightButtonUp += (_, args) =>
        {
            if (_disposed)
            {
                return;
            }

            args.Handled = true;
            DesignDeleteRequested?.Invoke(
                this,
                new LayoutDesignDeleteEventArgs(instanceId, view, args.GetPosition(view)));
        };
    }

    /// <summary>
    /// 在 AdornerLayer 上叠加选择框，不把编辑手柄计入组件测量或运行时命中区域。
    /// Adds a selection frame through AdornerLayer so editor handles never affect measurement or runtime hit testing.
    /// </summary>
    internal void SetDesignSelection(string? instanceId)
    {
        _designSelectedInstanceId = instanceId;
        foreach (var adorner in _designAdorners.Values)
        {
            AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        }
        _designAdorners.Clear();
        if (!_designMode || string.IsNullOrWhiteSpace(instanceId) ||
            !_designElements.TryGetValue(instanceId, out var view))
        {
            return;
        }

        void Attach(object? sender, RoutedEventArgs args)
        {
            view.Loaded -= Attach;
            if (AdornerLayer.GetAdornerLayer(view) is not { } layer)
            {
                return;
            }

            var adorner = new DesignSelectionAdorner(
                view,
                (edge, deltaDip) =>
                {
                    if (_designMode && !_disposed)
                    {
                        DesignResizeRequested?.Invoke(
                            this,
                            new LayoutDesignResizeEventArgs(instanceId, edge, deltaDip));
                    }
                });
            layer.Add(adorner);
            _designAdorners[instanceId] = adorner;
        }

        if (view.IsLoaded)
        {
            Attach(view, new RoutedEventArgs());
        }
        else
        {
            view.Loaded += Attach;
        }
    }

    internal void ApplyEdge(LayoutProfile profile, LayoutCollapseContainer collapse)
    {
        _profile = profile;
        _pointerNear = true;
        _gapDip = Math.Clamp(profile.Surface.GapDip, 0, 32);
        _widgetViews.Clear();
        ClearDesignAdorners();
        _designElements.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
        _marqueeTimer.Stop();
        _pointerStateTimer.Stop();
        Children.Clear();

        var root = BuildSlot(collapse.ExpandedSlot, collapse.GridBounds);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        if (!collapse.ExpandedSlot.Children.Any(child => child.Enabled))
        {
            EnsureContainerMinimum(root);
        }
        if (_designMode)
        {
            if (root is Panel rootPanel)
            {
                rootPanel.Background = Brushes.Transparent;
            }
            AttachDesignContainerHandlers(root, collapse.InstanceId);
            AttachDesignDropHandlers(root, collapse.InstanceId, LayoutSlotKind.Expanded);
            // 编辑器中的折叠触发区没有展开内容时仍应可见，避免空容器无法选中。
            // Keep an editor-only footprint for an empty collapse container so it remains selectable.
            root.MinWidth = Math.Max(root.MinWidth, 74);
            root.MinHeight = Math.Max(root.MinHeight, 30);
        }
        _designElements[collapse.InstanceId] = root;
        RegisterDesignBoundary(collapse.InstanceId, root, LayoutContainerKind.AutoCollapse);
        AttachDesignDeleteHandler(root, collapse.InstanceId);
        Children.Add(root);
        RefreshAllData();
        if (_marqueeStates.Count > 0)
        {
            _marqueeTimer.Start();
        }
    }

    /// <summary>
    /// 构建绝对网格根：容器按档案全局 GridBounds 转为 DIP 后绝对定位，左上角以占用联合矩形为局部原点。
    /// Builds the absolute-grid root: containers are positioned from profile-global GridBounds scaled to DIPs,
    /// using the occupied union's top-left corner as the local origin so leading empty space is not rendered.
    /// </summary>
    private FrameworkElement BuildAbsoluteLayout(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var root = new Canvas();
        var origin = LayoutRuntimeService.CalculateBodyGridBounds(profile);
        if (origin is null)
        {
            return root;
        }

        foreach (var container in profile.Containers)
        {
            if (!container.Enabled || container.GridBounds is not { } bounds)
            {
                continue;
            }

            var view = BuildContainer(container);
            Canvas.SetLeft(view, (bounds.X - origin.X) * cell);
            Canvas.SetTop(view, (bounds.Y - origin.Y) * cell);
            view.Width = bounds.Width * cell;
            view.Height = bounds.Height * cell;
            root.Children.Add(view);
        }

        return root;
    }

    internal void SetPointerNear(bool pointerNear)
    {
        _pointerNear = pointerNear;
        foreach (var visual in _containerViews.Values)
        {
            visual.PointerNear = pointerNear;
            ApplyContainerState(visual, animate: true);
        }
        UpdatePointerStateTimer();
    }

    internal void RefreshPointerNearFromMouse()
    {
        if (_designMode || _disposed)
        {
            return;
        }

        foreach (var visual in _containerViews.Values)
        {
            if (visual.Model.ContainerKind != LayoutContainerKind.HoverSwitch)
            {
                continue;
            }

            UpdateContainerPointerState(visual, IsPointerNear(visual));
        }
        UpdatePointerStateTimer();
    }

    private void Surface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        RefreshPointerNearFromMouse();
    }

    private void Surface_OnMouseMove(object sender, MouseEventArgs e)
    {
        RefreshPointerNearFromMouse();
    }

    /// <summary>
    /// 根据当前鼠标相对容器的 DIP 坐标判断“靠近”；使用膨胀矩形覆盖容器外的空白区域，并在视觉树重建后保持一致。
    /// Resolves proximity from the current pointer in DIP coordinates; the inflated rectangle covers empty space outside the container and stays consistent after tree rebuilds.
    /// </summary>
    private bool IsPointerNear(ContainerVisual visual)
    {
        if (visual.Host.ActualWidth <= 0 || visual.Host.ActualHeight <= 0)
        {
            return false;
        }

        var proximity = Math.Clamp(visual.Model.ProximityDip, 0, 256);
        var point = Mouse.GetPosition(visual.Host);
        return point.X >= -proximity &&
            point.Y >= -proximity &&
            point.X <= visual.Host.ActualWidth + proximity &&
            point.Y <= visual.Host.ActualHeight + proximity;
    }

    private void Surface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_designMode || _disposed)
        {
            return;
        }

        // MouseLeave 可能发生在 ProximityDip 内；计时器继续读取实际坐标，直到指针真正离开靠近范围。
        // MouseLeave can occur inside ProximityDip; the timer keeps reading real coordinates until the pointer actually clears it.
        RefreshPointerNearFromMouse();
    }

    private void OnPointerStateTimerTick(object? sender, EventArgs e)
    {
        RefreshPointerNearFromMouse();
    }

    private void UpdatePointerStateTimer()
    {
        if (_designMode || _disposed ||
            !_containerViews.Values.Any(visual =>
                visual.Model.ContainerKind == LayoutContainerKind.HoverSwitch &&
                visual.PointerNear))
        {
            _pointerStateTimer.Stop();
            return;
        }

        if (!_pointerStateTimer.IsEnabled)
        {
            _pointerStateTimer.Start();
        }
    }

    internal void SetMediaSnapshot(MediaSnapshot snapshot)
    {
        _mediaSnapshot = snapshot;
        RefreshMediaViews();
    }

    internal void SetMetricsText(string text)
    {
        _metricsText = text;
        foreach (var view in _widgetViews.Values)
        {
            if (view is Border { Child: TextBlock textBlock } &&
                textBlock.Tag is string tag &&
                tag == BuiltInWidgetTypeIds.Metrics)
            {
                textBlock.Text = text;
            }
        }
    }

    internal void SetMetricsSnapshot(SystemMetricsSnapshot snapshot)
    {
        var now = Environment.TickCount64;
        foreach (var state in _metricStates.Values)
        {
            var interval = Math.Clamp(
                state.Settings.RefreshIntervalMilliseconds,
                250,
                30_000);
            if (state.LastUpdateTick != 0 && now - state.LastUpdateTick < interval)
            {
                continue;
            }

            state.LastUpdateTick = now;
            var cycle = state.Settings.CycleMetrics is { Count: > 0 }
                ? state.Settings.CycleMetrics
                : [state.Settings.Metric];
            state.CycleIndex = Math.Clamp(state.CycleIndex, 0, cycle.Count - 1);
            state.Text.Text = MetricTextFormatter.Format(snapshot, cycle[state.CycleIndex]);
            state.CycleIndex = (state.CycleIndex + 1) % cycle.Count;
        }
    }

    internal void SetSpectrum(IReadOnlyList<float> values)
    {
        var count = Math.Min(values.Count, _spectrum.Length);
        for (var index = 0; index < _spectrum.Length; index++)
        {
            _spectrum[index] = index < count
                ? Math.Clamp(values[index], 0, 1)
                : 0;
        }

        foreach (var view in _widgetViews.Values)
        {
            if (view is SpectrumView spectrum)
            {
                spectrum.SetValues(_spectrum);
            }
        }
    }

    private FrameworkElement BuildContainer(LayoutContainerElement container)
    {
        var bounds = container.GridBounds ?? new LayoutGridRect(0, 0, 1, 1);
        if (container.ContainerKind == LayoutContainerKind.Static)
        {
            var staticSlot = BuildSlot(container.PrimarySlot, bounds);
            if (_designMode && staticSlot is Panel staticPanel)
            {
                staticPanel.Background = Brushes.Transparent;
            }
            ApplyGeometry(staticSlot, container.Geometry);
            if (!container.PrimarySlot.Children.Any(child => child.Enabled))
            {
                EnsureContainerMinimum(staticSlot);
            }
            if (_designMode)
            {
                AttachDesignContainerHandlers(staticSlot, container.InstanceId);
                AttachDesignDropHandlers(staticSlot, container.InstanceId, LayoutSlotKind.Primary);
            }
            staticSlot.SetValue(TransitionKeyProperty, $"container:{container.InstanceId}");
            staticSlot.SetValue(IsTransitionBoundaryProperty, true);
            _designElements[container.InstanceId] = staticSlot;
            RegisterDesignBoundary(container.InstanceId, staticSlot, container.ContainerKind);
            AttachDesignDeleteHandler(staticSlot, container.InstanceId);
            return staticSlot;
        }

        // HoverSwitch：离开/靠近两状态层共用同一外框，切换只替换可见层。
        var visual = new ContainerVisual(container, bounds);
        visual.Host.SetValue(TransitionKeyProperty, $"container:{container.InstanceId}");
        visual.Host.SetValue(IsTransitionBoundaryProperty, true);
        visual.PointerNear = _pointerNear;
        _containerViews[container.InstanceId] = visual;
        visual.Host.Background = Brushes.Transparent;
        visual.Slots[0].Children.Add(BuildSlot(container.PrimarySlot, bounds));
        visual.Slots[1].Children.Add(BuildSlot(container.SecondarySlot, bounds));
        ApplyContainerState(visual, animate: false);
        ApplyGeometry(visual.Host, container.Geometry);
        if (!container.PrimarySlot.Children.Any(child => child.Enabled) &&
            !container.SecondarySlot.Children.Any(child => child.Enabled))
        {
            EnsureContainerMinimum(visual.Host);
        }
        if (_designMode)
        {
            AttachDesignContainerHandlers(visual.Host, container.InstanceId);
            AttachDesignDropHandlers(visual.Slots[0], container.InstanceId, LayoutSlotKind.Primary);
            AttachDesignDropHandlers(visual.Slots[1], container.InstanceId, LayoutSlotKind.Secondary);
        }
        _designElements[container.InstanceId] = visual.Host;
        RegisterDesignBoundary(container.InstanceId, visual.Host, container.ContainerKind);
        AttachDesignDeleteHandler(visual.Host, container.InstanceId);
        return _designMode && container.ContainerKind == LayoutContainerKind.HoverSwitch
            ? BuildDesignHoverPreview(container, visual)
            : visual.Host;
    }

    private FrameworkElement BuildDesignHoverPreview(
        LayoutContainerElement container,
        ContainerVisual visual)
    {
        var buttons = new UniformGrid
        {
            Columns = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var leaveButton = CreateDesignStateButton(Loc.Get("Settings.Layout.EditorLeaveContent"));
        var nearButton = CreateDesignStateButton(Loc.Get("Settings.Layout.EditorNearContent"));
        buttons.Children.Add(leaveButton);
        buttons.Children.Add(nearButton);

        void RefreshButtons()
        {
            ApplyDesignStateButton(leaveButton, selected: !visual.PointerNear);
            ApplyDesignStateButton(nearButton, selected: visual.PointerNear);
        }

        void SelectState(bool pointerNear)
        {
            visual.PointerNear = pointerNear;
            ApplyContainerState(visual, animate: false);
            RefreshButtons();
            DesignPreviewStateChanged?.Invoke(
                this,
                new LayoutDesignPreviewStateEventArgs(container.InstanceId, pointerNear));
        }

        leaveButton.Click += (_, _) => SelectState(pointerNear: false);
        nearButton.Click += (_, _) => SelectState(pointerNear: true);
        RefreshButtons();

        // 标签悬浮在容器上边界之外，不遮挡容器内容；仅当该容器未被选中且鼠标靠近时显示切换按钮。
        // The label floats outside the container's top edge so it never blocks container widgets; it shows only
        // when the pointer is near and this container is not currently selected.
        var buttonBar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -30, 0, 0),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = buttons,
            Visibility = Visibility.Collapsed
        };
        SetDynamicResource(buttonBar, Border.BackgroundProperty, "MenuHoverBrush");
        SetDynamicResource(buttonBar, Border.BorderBrushProperty, "LayoutEditorAccentBrush");
        var overlay = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        overlay.Children.Add(visual.Host);
        overlay.Children.Add(buttonBar);
        overlay.MouseEnter += (_, _) =>
        {
            // 已选中的容器不显示切换按钮，避免遮挡其中的组件交互。
            if (_designSelectedInstanceId == container.InstanceId)
            {
                _hoverButtonTimer.Stop();
                buttonBar.Visibility = Visibility.Collapsed;
                return;
            }

            _hoverButtonTimer.Stop();
            buttonBar.Visibility = Visibility.Visible;
        };
        overlay.MouseLeave += (_, _) => _hoverButtonTimer.Start();
        return overlay;
    }

    private void OnHoverButtonTimerTick(object? sender, EventArgs e)
    {
        _hoverButtonTimer.Stop();
        // 去抖延迟结束后才隐藏；期间若鼠标重新进入 overlay，MouseEnter 已停止计时器。
        foreach (var element in _designElements.Values)
        {
            if (element is not Grid { Children.Count: 2 } grid)
            {
                continue;
            }

            foreach (var child in grid.Children)
            {
                if (child is Border { Child: UniformGrid } border)
                {
                    border.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private Button CreateDesignStateButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 72,
            MinHeight = 24,
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(1, 0, 1, 0),
            FontSize = 10.5,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Style = GetResource<Style>("LayoutEditorButtonStyle")
        };
        SetDynamicResource(button, Control.ForegroundProperty, "MenuPrimaryTextBrush");
        SetDynamicResource(button, Control.BorderBrushProperty, "MenuBorderBrush");
        return button;
    }

    private void ApplyDesignStateButton(Button button, bool selected)
    {
        if (selected)
        {
            SetDynamicResource(button, Control.BackgroundProperty, "MenuSelectionBrush");
            SetDynamicResource(button, Control.BorderBrushProperty, "LayoutEditorAccentBrush");
        }
        else
        {
            button.Background = Brushes.Transparent;
            SetDynamicResource(button, Control.BorderBrushProperty, "MenuBorderBrush");
        }
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static void EnsureContainerMinimum(FrameworkElement view)
    {
        view.MinWidth = Math.Max(view.MinWidth, LayoutRuntimeService.EmptyContainerMinWidthDip);
        view.MinHeight = Math.Max(view.MinHeight, LayoutRuntimeService.EmptyContainerMinHeightDip);
    }

    private void UpdateContainerPointerState(ContainerVisual visual, bool pointerNear)
    {
        if (visual.PointerNear == pointerNear)
        {
            return;
        }

        visual.PointerNear = pointerNear;
        ApplyContainerState(visual, animate: true);
        UpdatePointerStateTimer();
    }

    private void AttachDesignContainerHandlers(FrameworkElement view, string instanceId)
    {
        view.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (IsInsideWidget(args.OriginalSource as DependencyObject))
            {
                return;
            }

            _designPressInstanceId = instanceId;
            _designPressPoint = args.GetPosition(this);
            _designPressSource = view;
            DesignElementSelected?.Invoke(
                this,
                new LayoutDesignElementEventArgs(instanceId, view, isContainer: true));
        };
        view.PreviewMouseMove += (_, args) =>
        {
            if (IsInsideWidget(args.OriginalSource as DependencyObject))
            {
                return;
            }

            if (_designPressInstanceId != instanceId ||
                args.LeftButton != MouseButtonState.Pressed ||
                _designPressSource is null)
            {
                return;
            }

            var current = args.GetPosition(this);
            if (Math.Abs(current.X - _designPressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _designPressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var source = _designPressSource;
            _designPressInstanceId = null;
            _designPressSource = null;
            DesignElementDragRequested?.Invoke(
                this,
                new LayoutDesignElementEventArgs(instanceId, source, isContainer: true));
        };
    }

    private bool IsInsideWidget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (_widgetViews.Values.Any(view => ReferenceEquals(view, source)))
            {
                return true;
            }
            source = source is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }

        return false;
    }

    private void AttachDesignDropHandlers(
        FrameworkElement view,
        string containerId,
        LayoutSlotKind slotKind)
    {
        // 仅在编辑器树中让槽位接收拖放；运行时表面不绑定这些事件，避免改变真实命中区域。
        // Only the editor tree accepts drops; runtime surfaces stay free of these handlers and keep their hit area unchanged.
        if (view is Panel panel)
        {
            panel.Background = Brushes.Transparent;
        }
        view.AllowDrop = true;
        view.DragOver += (_, args) =>
        {
            DesignDropTargetDragOver?.Invoke(
                this,
                new LayoutDesignDropEventArgs(containerId, slotKind, view, args));
            args.Handled = true;
        };
        view.Drop += (_, args) =>
        {
            DesignDropRequested?.Invoke(
                this,
                new LayoutDesignDropEventArgs(containerId, slotKind, view, args));
            args.Handled = true;
        };
    }

    /// <summary>
    /// 槽位组件按容器局部网格绝对定位；不再使用 StackPanel 自动排列。
    /// Positions slot widgets absolutely on the container-local grid instead of auto-arranging with a StackPanel.
    /// </summary>
    private FrameworkElement BuildSlot(LayoutSlot slot, LayoutGridRect ownerBounds)
    {
        var canvas = new Canvas();
        foreach (var child in slot.Children)
        {
            if (!child.Enabled || child is not LayoutWidgetElement widget ||
                widget.GridBounds is not { } widgetBounds)
            {
                continue;
            }

            var view = BuildWidgetAt(widget, widgetBounds);
            Canvas.SetLeft(view, widgetBounds.X * CellSize);
            Canvas.SetTop(view, widgetBounds.Y * CellSize);
            canvas.Children.Add(view);
        }

        return canvas;
    }

    /// <summary>
    /// 组件外框由局部网格矩形决定；内部视觉保持原尺寸并居中，要求等比的图标内容由各自 Stretch 策略保持比例。
    /// The widget frame is its local grid rectangle; the inner visual keeps its intrinsic size centered,
    /// and aspect-requiring visuals keep their ratio via their own Stretch policy.
    /// </summary>
    private FrameworkElement BuildWidgetAt(LayoutWidgetElement widget, LayoutGridRect bounds)
    {
        var view = BuildWidget(widget);
        var host = new Grid
        {
            Width = Math.Max(0, bounds.Width * CellSize),
            Height = Math.Max(0, bounds.Height * CellSize),
            ClipToBounds = true
        };
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(view);
        // 设计模式：为组件叠加可见边框与浅色底，使边界在画布上可辨，尤其无背景的指标组件。
        // In design mode, add a visible frame and translucent fill so widget bounds are recognizable, especially metrics.
        if (_designMode)
        {
            var frame = new Border
            {
                Width = host.Width,
                Height = host.Height,
                Child = host,
                ClipToBounds = true
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "LayoutEditorAccentBrush");
            frame.BorderThickness = new Thickness(1);
            frame.Background = new SolidColorBrush(Color.FromArgb(18, 86, 156, 255));
            _designElements[widget.InstanceId] = frame;
            AttachDesignDeleteHandler(frame, widget.InstanceId);
            return frame;
        }

        _designElements[widget.InstanceId] = host;
        AttachDesignDeleteHandler(host, widget.InstanceId);
        return host;
    }

    private int CellSize
    {
        get
        {
            var grid = _profile is null
                ? LayoutGridSettings.Default
                : LayoutGridSettings.Normalize(_profile.Grid);
            return Math.Max(grid.CellSizeDip, 1);
        }
    }

    private FrameworkElement BuildWidget(LayoutWidgetElement widget)
    {
        FrameworkElement view = widget.TypeId switch
        {
            BuiltInWidgetTypeIds.Artwork => BuildArtwork(widget),
            BuiltInWidgetTypeIds.MediaText => BuildMediaText(widget),
            BuiltInWidgetTypeIds.MediaSource => BuildMediaSource(widget),
            BuiltInWidgetTypeIds.Command => BuildCommand(widget),
            BuiltInWidgetTypeIds.Metrics => BuildMetrics(widget),
            BuiltInWidgetTypeIds.Spectrum => BuildSpectrum(widget),
            BuiltInWidgetTypeIds.Separator => BuildSeparator(widget),
            _ => BuildUnknown(widget)
        };

        ApplyGeometry(view, widget.Geometry);
        AssignTransitionKeys(view, widget);
        if (_designMode)
        {
            view.PreviewMouseLeftButtonDown += (_, args) =>
            {
                _designPressInstanceId = widget.InstanceId;
                _designPressPoint = args.GetPosition(this);
                _designPressSource = view;
                DesignElementSelected?.Invoke(
                    this,
                    new LayoutDesignElementEventArgs(widget.InstanceId, view));
            };
            view.PreviewMouseMove += (_, args) =>
            {
                if (_designPressInstanceId != widget.InstanceId ||
                    args.LeftButton != MouseButtonState.Pressed ||
                    _designPressSource is null)
                {
                    return;
                }

                var current = args.GetPosition(this);
                if (Math.Abs(current.X - _designPressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - _designPressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                var source = _designPressSource;
                _designPressInstanceId = null;
                _designPressSource = null;
                DesignElementDragRequested?.Invoke(
                    this,
                    new LayoutDesignElementEventArgs(widget.InstanceId, source));
            };
        }
        _widgetViews[widget.InstanceId] = view;
        _designElements[widget.InstanceId] = view;
        return view;
    }

    private static string GetTransitionKey(LayoutWidgetElement widget)
    {
        return widget.Settings switch
        {
            MediaTextWidgetSettings text => text.TextKind switch
            {
                MediaTextKind.Title => "media-text:title",
                MediaTextKind.Artist => "media-text:artist",
                MediaTextKind.Source => "media-text:source",
                MediaTextKind.TitleAndArtist => "media-text:combined",
                _ => "media-text"
            },
            CommandWidgetSettings command => $"{widget.TypeId}:{command.Command}",
            MetricsWidgetSettings metrics => $"{widget.TypeId}:{metrics.Metric}",
            _ => widget.TypeId
        };
    }

    private static void AssignTransitionKeys(FrameworkElement view, LayoutWidgetElement widget)
    {
        if (widget.Settings is MediaTextWidgetSettings { TextKind: MediaTextKind.TitleAndArtist } &&
            view is StackPanel { Tag: ValueTuple<TextBlock, TextBlock> combined })
        {
            combined.Item1.SetValue(TransitionKeyProperty, "media-text:title");
            combined.Item2.SetValue(TransitionKeyProperty, "media-text:artist");
            return;
        }

        view.SetValue(TransitionKeyProperty, GetTransitionKey(widget));
    }

    private FrameworkElement BuildArtwork(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as ArtworkWidgetSettings ??
            new ArtworkWidgetSettings(6, false, true);
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = _mediaSnapshot.Artwork.AsImageSource(),
            IsHitTestVisible = false
        };
        var placeholder = new TextBlock
        {
            Text = "\uE8D6",
            FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        SetDynamicResource(
            placeholder,
            TextBlock.ForegroundProperty,
            ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
        var grid = new Grid();
        grid.Children.Add(placeholder);
        grid.Children.Add(image);
        var useArtworkColor = settings.UseMediaPrimaryColor && _mediaSnapshot.Artwork is not null;
        var border = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(Math.Clamp(settings.CornerRadiusDip, 0, 32)),
            Background = useArtworkColor ? ResolveArtworkBackground(settings) : Brushes.Transparent,
            Child = grid,
            Cursor = settings.OpenSourceOnClick ? Cursors.Hand : Cursors.Arrow,
            ToolTip = settings.OpenSourceOnClick ? Loc.Get("Main.Menu.ShowSource") : null
        };
        if (!useArtworkColor)
        {
            SetDynamicResource(
                border,
                Border.BackgroundProperty,
                ResolveContentResourceKey("TaskbarSurfaceBrush"));
        }
        if (settings.OpenSourceOnClick)
        {
            SetIsInteractiveElement(border, true);
            border.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                if (_designMode)
                {
                    return;
                }
                SourceRequested?.Invoke(this, EventArgs.Empty);
            };
        }
        border.Tag = (image, placeholder, settings);
        return border;
    }

    private FrameworkElement BuildMediaText(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ??
            new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1);
        if (settings.TextKind == MediaTextKind.TitleAndArtist)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = widget.Geometry?.WidthDip ?? (IsVertical ? 68 : 150),
                Height = widget.Geometry?.HeightDip ?? 40,
                ClipToBounds = true
            };
            var title = new TextBlock
            {
                FontSize = Math.Clamp(settings.FontSizeDip, 6, 72),
                Height = 22,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var artist = new TextBlock
            {
                FontSize = Math.Clamp(settings.FontSizeDip - 3, 6, 72),
                Height = 18,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            SetDynamicResource(title, TextBlock.FontFamilyProperty, "AppDisplayFontFamily");
            SetDynamicResource(title, TextBlock.FontWeightProperty, "PlayerTitleFontWeight");
            SetDynamicResource(
                title,
                TextBlock.ForegroundProperty,
                ResolveContentResourceKey("TaskbarPrimaryTextBrush"));
            SetDynamicResource(artist, TextBlock.FontFamilyProperty, "AppTextFontFamily");
            SetDynamicResource(artist, TextBlock.FontWeightProperty, "PlayerTextFontWeight");
            SetDynamicResource(
                artist,
                TextBlock.ForegroundProperty,
                ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
            stack.Children.Add(title);
            stack.Children.Add(artist);
            _mediaTextKinds[widget.InstanceId] = MediaTextKind.TitleAndArtist;
            stack.Tag = (title, artist);
            return stack;
        }
        var text = new TextBlock
        {
            FontSize = Math.Clamp(settings.FontSizeDip, 6, 72),
            TextWrapping = settings.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = IsVertical ? 68 : 210,
            // 文本组件保持稳定的槽位高度；最多行数只控制内部换行，不能把同槽控件顶出显示区域。
            // Keep a stable slot height; MaxLines controls wrapping inside the widget and must not push siblings out of view.
            Height = 40,
            TextAlignment = TextAlignment.Center,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(text, TextBlock.FontFamilyProperty, "AppDisplayFontFamily");
        SetDynamicResource(text, TextBlock.FontWeightProperty, "PlayerTitleFontWeight");
        SetDynamicResource(
            text,
            TextBlock.ForegroundProperty,
            ResolveContentResourceKey("TaskbarPrimaryTextBrush"));
        if (settings.MaxLines > 1)
        {
            // 多行文本放入固定高度槽位，内部高度按最大行数裁切，避免换行改变同级组件的排列位置。
            // Multi-line text stays inside a fixed-height slot; its inner height is capped by MaxLines so wrapping cannot move siblings.
            var lineHeight = Math.Max(12, Math.Ceiling(Math.Clamp(settings.FontSizeDip, 6, 72) * 1.25));
            text.Height = double.NaN;
            text.Width = double.NaN;
            text.LineHeight = lineHeight;
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            // 长条文本槽位固定为两行高度；超过两行不会产生额外可见内容，只会制造无效设置。
            // The strip text slot is fixed to two lines; values above two cannot render more content and would only create a no-op setting.
            text.MaxHeight = Math.Min(40, lineHeight * Math.Clamp(settings.MaxLines, 1, MaximumMediaTextLines));
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            var host = new Grid
            {
                Width = IsVertical ? 68 : 210,
                Height = 40,
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = text
            };
            host.Children.Add(text);
            _mediaTextKinds[widget.InstanceId] = settings.TextKind;
            return host;
        }
        _mediaTextKinds[widget.InstanceId] = settings.TextKind;
        // 多行文本需要保留 WPF 的换行布局；跑马灯只对单行标题启用，避免设置看似成功却仍被改回单行。
        // Multi-line text must keep WPF wrapping; marquee is limited to single-line titles so the MaxLines setting remains effective.
        if (settings.EnableMarquee && settings.MaxLines <= 1)
        {
            _marqueeStates[widget.InstanceId] = new(text, string.Empty, 0);
        }
        return text;
    }

    private FrameworkElement BuildMediaSource(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ??
            new MediaTextWidgetSettings(MediaTextKind.Source, false, 11, 1);
        var text = BuildMediaText(widget with
        {
            Settings = settings with { TextKind = MediaTextKind.Source }
        });
        if (GetTextBlock(text) is { } textBlock)
        {
            SetDynamicResource(
                textBlock,
                TextBlock.ForegroundProperty,
                ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
            textBlock.Cursor = Cursors.Hand;
            textBlock.ToolTip = Loc.Get("Main.Menu.ShowSource");
            SetIsInteractiveElement(textBlock, true);
            if (text is FrameworkElement host)
            {
                SetIsInteractiveElement(host, true);
                host.MouseLeftButtonUp += (_, args) =>
                {
                    args.Handled = true;
                    if (_designMode)
                    {
                        return;
                    }
                    SourceRequested?.Invoke(this, EventArgs.Empty);
                };
            }
            else
            {
                textBlock.MouseLeftButtonUp += (_, args) =>
                {
                    args.Handled = true;
                    if (_designMode)
                    {
                        return;
                    }
                    SourceRequested?.Invoke(this, EventArgs.Empty);
                };
            }
        }

        return text;
    }

    private FrameworkElement BuildCommand(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as CommandWidgetSettings ??
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36);
        var button = new Button
        {
            Width = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Height = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Cursor = Cursors.Hand,
            Style = GetResource<Style>(_componentSkinService.ResolveResourceKey(widget, _useMenuThemeForContent)),
            Tag = settings.Command,
            ToolTip = GetCommandTooltip(settings.Command),
            Content = new TextBlock
            {
                Text = GetCommandGlyph(settings.Command),
                FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, args) =>
        {
            args.Handled = true;
            if (_designMode)
            {
                return;
            }

            CommandRequested?.Invoke(
                this,
                new LayoutCommandEventArgs(settings.Command, button));
        };
        if (settings.Command is MediaCommandKind.SelectOutputDevice or MediaCommandKind.AdjustVolume)
        {
            button.PreviewMouseWheel += (_, args) =>
            {
                args.Handled = true;
                if (_designMode)
                {
                    return;
                }

                WheelRequested?.Invoke(
                    this,
                    new LayoutWheelEventArgs(settings.Command, args.Delta, button));
            };
        }
        SetIsInteractiveElement(button, true);
        return button;
    }

    private FrameworkElement BuildMetrics(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MetricsWidgetSettings ??
            new MetricsWidgetSettings(MetricKind.SystemMemory, false, 2500, [MetricKind.SystemMemory]);
        var text = new TextBlock
        {
            Tag = BuiltInWidgetTypeIds.Metrics,
            Text = _metricsText,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(text, TextBlock.FontFamilyProperty, "AppTextFontFamily");
        SetDynamicResource(text, TextBlock.FontWeightProperty, "PlayerTextFontWeight");
        SetDynamicResource(
            text,
            TextBlock.ForegroundProperty,
            ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
        var border = new Border
        {
            Width = 74,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Cursor = settings.OpenTaskManagerOnClick ? Cursors.Hand : Cursors.Arrow,
            Child = text
        };
        SetDynamicResource(
            border,
            Border.BackgroundProperty,
            ResolveContentResourceKey("TaskbarHoverBrush"));
        SetIsInteractiveElement(border, settings.OpenTaskManagerOnClick);
        border.MouseLeftButtonUp += (_, args) =>
        {
            if (!settings.OpenTaskManagerOnClick)
            {
                return;
            }

            args.Handled = true;
            if (_designMode)
            {
                return;
            }
            MetricsRequested?.Invoke(
                this,
                new LayoutMetricsEventArgs(settings.OpenTaskManagerOnClick));
        };
        _metricStates[widget.InstanceId] = new(text, settings);
        return border;
    }

    private FrameworkElement BuildSpectrum(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SpectrumWidgetSettings ??
            new SpectrumWidgetSettings(9, 20, 100);
        return new SpectrumView(
            Math.Clamp(settings.BandCount, 1, AudioMonitorService.BandCount),
            Math.Clamp(settings.RefreshRateHz, 5, 30),
            Math.Clamp(settings.SensitivityPercent, 1, 400),
            ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
    }

    private FrameworkElement BuildSeparator(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SeparatorWidgetSettings ??
            new SeparatorWidgetSettings(1, 22);
        var separator = new Border
        {
            Width = Math.Clamp(settings.ThicknessDip, 1, 8),
            Height = Math.Clamp(settings.LengthDip, 4, 256),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        SetDynamicResource(
            separator,
            Border.BackgroundProperty,
            ResolveContentResourceKey("TaskbarDividerBrush"));
        return separator;
    }

    private static FrameworkElement BuildUnknown(LayoutWidgetElement widget)
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            Opacity = 0.4,
            Child = new TextBlock
            {
                Text = "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = Loc.Get("Settings.Layout.UnknownWidget", widget.TypeId)
        };
    }

    private void RefreshAllData()
    {
        RefreshMediaViews();
        SetMetricsText(_metricsText);
        SetSpectrum(_spectrum);
    }

    private void RefreshMediaViews()
    {
        foreach (var pair in _mediaTextKinds)
        {
            if (!_widgetViews.TryGetValue(pair.Key, out var view))
            {
                continue;
            }

            var value = pair.Value switch
            {
                MediaTextKind.Title => GetDisplayText(_mediaSnapshot.Title, "Main.Placeholder.Title"),
                MediaTextKind.Artist => GetDisplayText(_mediaSnapshot.Artist, "Main.Placeholder.Subtitle"),
                MediaTextKind.Source => GetDisplayText(_mediaSnapshot.SourceName, "Main.TitleIdle"),
                _ => string.Empty
            };
            if (pair.Value == MediaTextKind.TitleAndArtist && view is StackPanel { Tag: ValueTuple<TextBlock, TextBlock> combined })
            {
                combined.Item1.Text = GetDisplayText(_mediaSnapshot.Title, "Main.Placeholder.Title");
                combined.Item2.Text = GetDisplayText(_mediaSnapshot.Artist, "Main.Placeholder.Subtitle");
                combined.Item1.ToolTip = combined.Item1.Text;
                combined.Item2.ToolTip = combined.Item2.Text;
                continue;
            }

            var text = GetTextBlock(view);
            if (text is null)
            {
                continue;
            }
            text.Text = IsVertical ? FormatVerticalText(value) : value;
            text.ToolTip = value;
            if (_marqueeStates.TryGetValue(pair.Key, out var marquee))
            {
                marquee.Content = value;
                marquee.Offset = 0;
            }
        }

        foreach (var view in _widgetViews.Values)
        {
            if (view is not Border
                {
                    Child: Grid grid,
                    Tag: ValueTuple<Image, TextBlock, ArtworkWidgetSettings> artwork
                } border)
            {
                continue;
            }

            artwork.Item1.Source = _mediaSnapshot.Artwork.AsImageSource();
            artwork.Item2.Visibility = _mediaSnapshot.Artwork is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            border.Background = ResolveArtworkBackground(artwork.Item3);
        }

        RefreshCommandViews();
    }

    private void RefreshCommandViews()
    {
        foreach (var view in _widgetViews.Values.OfType<Button>())
        {
            if (view.Tag is not MediaCommandKind command)
            {
                continue;
            }

            view.IsEnabled = command switch
            {
                MediaCommandKind.Previous => _mediaSnapshot.IsConnected && _mediaSnapshot.CanSkipPrevious,
                MediaCommandKind.PlayPause => _mediaSnapshot.IsConnected && _mediaSnapshot.CanPlayPause,
                MediaCommandKind.Next => _mediaSnapshot.IsConnected && _mediaSnapshot.CanSkipNext,
                MediaCommandKind.SelectSource or MediaCommandKind.AdjustVolume => _mediaSnapshot.IsConnected,
                MediaCommandKind.SelectOutputDevice => true,
                _ => true
            };
            if (view.Content is TextBlock glyph)
            {
                glyph.Text = command == MediaCommandKind.PlayPause
                    ? GetCommandGlyph(command, _mediaSnapshot.IsPlaying)
                    : GetCommandGlyph(command);
            }
            view.ToolTip = command == MediaCommandKind.PlayPause
                ? GetCommandTooltip(command, _mediaSnapshot.IsPlaying)
                : GetCommandTooltip(command);
        }
    }

    private void ApplyContainerState(ContainerVisual visual, bool animate)
    {
        var container = visual.Model;
        // 悬停容器只有“离开/靠近”两个状态层，由实际指针状态唯一决定。
        // A HoverSwitch container owns exactly two state layers driven solely by the real pointer state.
        var activeSlot = container.ContainerKind == LayoutContainerKind.HoverSwitch
            ? (visual.PointerNear ? 1 : 0)
            : 0;
        if (visual.ActiveSlot == activeSlot)
        {
            return;
        }

        var previousSlot = visual.ActiveSlot;
        visual.ActiveSlot = activeSlot;
        if (!animate || previousSlot < 0 ||
            !container.Animation.Enabled || container.Animation.DurationMilliseconds <= 0)
        {
            CommitContainerState(visual);
            return;
        }

        AnimateContainerState(visual, previousSlot, activeSlot);
    }

    private void CommitContainerState(ContainerVisual visual)
    {
        visual.TransitionVersion++;
        visual.Host.BeginAnimation(TransitionProgressProperty, null);
        visual.Host.SetValue(TransitionProgressProperty, 0d);
        for (var index = 0; index < visual.Slots.Count; index++)
        {
            var slot = visual.Slots[index];
            var active = index == visual.ActiveSlot;
            slot.BeginAnimation(UIElement.OpacityProperty, null);
            slot.Opacity = 1;
            slot.IsHitTestVisible = active;
            slot.Visibility = active
                ? Visibility.Visible
                : visual.Model.ContainerKind == LayoutContainerKind.HoverSwitch
                    ? Visibility.Hidden
                    : Visibility.Collapsed;
            foreach (var element in EnumerateTransitionElements(slot))
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                element.ClearValue(UIElement.RenderTransformProperty);
                element.ClearValue(UIElement.RenderTransformOriginProperty);
            }
        }
    }

    private void AnimateContainerState(
        ContainerVisual visual,
        int previousSlot,
        int activeSlot)
    {
        var version = ++visual.TransitionVersion;
        var durationMilliseconds = Math.Clamp(
            visual.Model.Animation.DurationMilliseconds,
            1,
            2_000);
        var delayMilliseconds = Math.Clamp(
            visual.Model.Animation.DelayMilliseconds,
            0,
            2_000);
        var easing = ResolveEasing(visual.Model.Animation.Easing);
        var outgoingSlot = visual.Slots[previousSlot];
        var incomingSlot = visual.Slots[activeSlot];
        var allElements = EnumerateTransitionElements(outgoingSlot)
            .Concat(EnumerateTransitionElements(incomingSlot))
            .Distinct()
            .ToArray();
        var presentations = allElements.ToDictionary(
            element => element,
            element => CaptureElementPresentation(element, visual.Host));

        outgoingSlot.Visibility = Visibility.Visible;
        incomingSlot.Visibility = Visibility.Visible;
        outgoingSlot.IsHitTestVisible = false;
        incomingSlot.IsHitTestVisible = true;
        outgoingSlot.BeginAnimation(UIElement.OpacityProperty, null);
        incomingSlot.BeginAnimation(UIElement.OpacityProperty, null);
        outgoingSlot.Opacity = 1;
        incomingSlot.Opacity = 1;

        var outgoingByKey = EnumerateTransitionElements(outgoingSlot)
            .GroupBy(GetTransitionKey)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var incomingByKey = EnumerateTransitionElements(incomingSlot)
            .GroupBy(GetTransitionKey)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var matchedOutgoing = new HashSet<FrameworkElement>();
        var matchedIncoming = new HashSet<FrameworkElement>();

        foreach (var key in outgoingByKey.Keys.Intersect(incomingByKey.Keys, StringComparer.Ordinal))
        {
            var outgoing = outgoingByKey[key];
            var incoming = incomingByKey[key];
            var count = Math.Min(outgoing.Length, incoming.Length);
            for (var index = 0; index < count; index++)
            {
                var oldElement = outgoing[index];
                var newElement = incoming[index];
                matchedOutgoing.Add(oldElement);
                matchedIncoming.Add(newElement);
                oldElement.BeginAnimation(UIElement.OpacityProperty, null);
                oldElement.Opacity = 0;
                newElement.BeginAnimation(UIElement.OpacityProperty, null);
                newElement.Opacity = 1;

                var delta = presentations[oldElement].VisualPosition -
                    presentations[newElement].BasePosition;
                var transform = new TranslateTransform(delta.X, delta.Y);
                newElement.RenderTransform = transform;
                AnimateTo(
                    transform,
                    TranslateTransform.XProperty,
                    0,
                    durationMilliseconds,
                    delayMilliseconds,
                    easing);
                AnimateTo(
                    transform,
                    TranslateTransform.YProperty,
                    0,
                    durationMilliseconds,
                    delayMilliseconds,
                    easing);
            }
        }

        var outgoingElements = EnumerateTransitionElements(outgoingSlot).ToArray();
        var incomingElements = EnumerateTransitionElements(incomingSlot).ToArray();
        foreach (var element in outgoingElements.Where(element => !matchedOutgoing.Contains(element)))
        {
            element.Opacity = presentations[element].Opacity;
            AnimateOpacity(element, 0, durationMilliseconds, delayMilliseconds, easing);
        }
        foreach (var element in incomingElements.Where(element => !matchedIncoming.Contains(element)))
        {
            element.Opacity = presentations[element].WasVisible
                ? presentations[element].Opacity
                : 0;
            AnimateOpacity(element, 1, durationMilliseconds, delayMilliseconds, easing);
            var currentOffset = presentations[element].VisualPosition -
                presentations[element].BasePosition;
            if (Math.Abs(currentOffset.X) > 0.01 || Math.Abs(currentOffset.Y) > 0.01)
            {
                var transform = new TranslateTransform(currentOffset.X, currentOffset.Y);
                element.RenderTransform = transform;
                AnimateTo(transform, TranslateTransform.XProperty, 0, durationMilliseconds, delayMilliseconds, easing);
                AnimateTo(transform, TranslateTransform.YProperty, 0, durationMilliseconds, delayMilliseconds, easing);
            }
        }

        if (outgoingElements.Length == 0 && incomingElements.Length == 0)
        {
            outgoingSlot.Opacity = 1;
            incomingSlot.Opacity = 0;
            AnimateOpacity(outgoingSlot, 0, durationMilliseconds, delayMilliseconds, easing);
            AnimateOpacity(incomingSlot, 1, durationMilliseconds, delayMilliseconds, easing);
        }

        var completion = new DoubleAnimation
        {
            From = 1,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            FillBehavior = FillBehavior.Stop
        };
        completion.Completed += (_, _) =>
        {
            if (!_disposed && visual.TransitionVersion == version)
            {
                CommitContainerState(visual);
            }
        };
        visual.Host.BeginAnimation(
            TransitionProgressProperty,
            completion,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static ElementPresentation CaptureElementPresentation(
        FrameworkElement element,
        UIElement relativeTo)
    {
        var wasVisible = element.IsVisible;
        var opacity = element.Opacity;
        var visualPosition = element.TranslatePoint(new Point(), relativeTo);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
        element.ClearValue(UIElement.RenderTransformProperty);
        element.ClearValue(UIElement.RenderTransformOriginProperty);
        var basePosition = element.TranslatePoint(new Point(), relativeTo);
        return new ElementPresentation(visualPosition, basePosition, opacity, wasVisible);
    }

    private static IEnumerable<FrameworkElement> EnumerateTransitionElements(DependencyObject root)
    {
        if (root is FrameworkElement element &&
            element.GetValue(TransitionKeyProperty) is string)
        {
            yield return element;
            if ((bool)element.GetValue(IsTransitionBoundaryProperty))
            {
                yield break;
            }
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in EnumerateTransitionElements(VisualTreeHelper.GetChild(root, index)))
            {
                yield return child;
            }
        }
    }

    private static string GetTransitionKey(FrameworkElement element) =>
        (string)element.GetValue(TransitionKeyProperty);

    private static IEasingFunction? ResolveEasing(LayoutEasingKind easing) => easing switch
    {
        LayoutEasingKind.Linear => null,
        LayoutEasingKind.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private static void AnimateOpacity(
        UIElement element,
        double target,
        int durationMilliseconds,
        int delayMilliseconds,
        IEasingFunction? easing)
    {
        var current = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = current;
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = current,
                To = target,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
                EasingFunction = easing
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateTo(
        Animatable target,
        DependencyProperty property,
        double value,
        int durationMilliseconds,
        int delayMilliseconds,
        IEasingFunction? easing)
    {
        target.BeginAnimation(
            property,
            new DoubleAnimation
            {
                To = value,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
                EasingFunction = easing
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsVertical => _profile?.LayoutMode == PlayerLayoutMode.Vertical;

    private Orientation ResolveOrientation(LayoutFlowOrientation orientation)
    {
        return orientation switch
        {
            LayoutFlowOrientation.Vertical => Orientation.Vertical,
            LayoutFlowOrientation.Horizontal => Orientation.Horizontal,
            _ => IsVertical ? Orientation.Vertical : Orientation.Horizontal
        };
    }

    private static HorizontalAlignment ResolveHorizontalAlignment(LayoutContentAlignment alignment) =>
        alignment switch
        {
            LayoutContentAlignment.Start => HorizontalAlignment.Left,
            LayoutContentAlignment.End => HorizontalAlignment.Right,
            LayoutContentAlignment.Stretch => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Center
        };

    private static VerticalAlignment ResolveVerticalAlignment(LayoutContentAlignment alignment) =>
        alignment switch
        {
            LayoutContentAlignment.Start => VerticalAlignment.Top,
            LayoutContentAlignment.End => VerticalAlignment.Bottom,
            LayoutContentAlignment.Stretch => VerticalAlignment.Stretch,
            _ => VerticalAlignment.Center
        };

    private static void ApplyGeometry(FrameworkElement view, LayoutGeometry geometry)
    {
        // schema 4 中组件/容器外框由网格矩形决定；DIP 尺寸覆盖已被迁移清空，不得再次成为事实来源。
        // Schema-4 frames are owned by grid rectangles; DIP size overrides were cleared by migration.
        if (geometry.MinWidthDip.HasValue)
        {
            view.MinWidth = geometry.MinWidthDip.Value;
        }
        if (geometry.MaxWidthDip.HasValue)
        {
            view.MaxWidth = geometry.MaxWidthDip.Value;
        }
        if (geometry.MinHeightDip.HasValue)
        {
            view.MinHeight = geometry.MinHeightDip.Value;
        }
        if (geometry.MaxHeightDip.HasValue)
        {
            view.MaxHeight = geometry.MaxHeightDip.Value;
        }
        var margin = geometry.Margin ?? LayoutThickness.Zero;
        var existingMargin = view.Margin;
        view.Margin = new Thickness(
            existingMargin.Left + margin.Left,
            existingMargin.Top + margin.Top,
            existingMargin.Right + margin.Right,
            existingMargin.Bottom + margin.Bottom);
    }

    private static string GetDisplayText(string value, string fallbackKey)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Loc.Get(fallbackKey)
            : value;
    }

    private static TextBlock? GetTextBlock(FrameworkElement view)
    {
        return view switch
        {
            TextBlock text => text,
            Grid { Tag: TextBlock text } => text,
            _ => null
        };
    }

    private Brush ResolveArtworkBackground(ArtworkWidgetSettings settings)
    {
        if (!settings.UseMediaPrimaryColor || _mediaSnapshot.Artwork is null)
        {
            return GetContentBrush("TaskbarSurfaceBrush");
        }

        try
        {
            var source = _mediaSnapshot.Artwork.AsImageSource() as BitmapSource;
            if (source is null)
            {
                return GetContentBrush("TaskbarSurfaceBrush");
            }
            var width = Math.Max(1, source.PixelWidth);
            var height = Math.Max(1, source.PixelHeight);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = 0;
            var sampleStep = Math.Max(1, Math.Max(width, height) / 32);
            for (var y = 0; y < height; y += sampleStep)
            {
                for (var x = 0; x < width; x += sampleStep)
                {
                    var index = y * stride + x * 4;
                    blue += pixels[index];
                    green += pixels[index + 1];
                    red += pixels[index + 2];
                    count++;
                }
            }

            if (count > 0)
            {
                return new SolidColorBrush(Color.FromRgb(
                    (byte)(red / count),
                    (byte)(green / count),
                    (byte)(blue / count)));
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("layout-artwork-accent", exception);
        }

        return GetContentBrush("TaskbarSurfaceBrush");
    }

    private void OnMarqueeTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || IsVertical)
        {
            return;
        }

        foreach (var state in _marqueeStates.Values)
        {
            var content = state.Content;
            if (content.Length <= 18)
            {
                state.Text.Text = content;
                continue;
            }

            var text = content + "   ";
            var offset = state.Offset % text.Length;
            state.Text.Text = text[offset..] + text[..offset];
            state.Offset++;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _marqueeTimer.Stop();
        _marqueeTimer.Tick -= OnMarqueeTimerTick;
        _pointerStateTimer.Stop();
        _pointerStateTimer.Tick -= OnPointerStateTimerTick;
        _hoverButtonTimer.Stop();
        _hoverButtonTimer.Tick -= OnHoverButtonTimerTick;
        CommandRequested = null;
        MetricsRequested = null;
        WheelRequested = null;
        SourceRequested = null;
        DesignElementSelected = null;
        DesignElementDragRequested = null;
        DesignDropTargetDragOver = null;
        DesignDropRequested = null;
        DesignPreviewStateChanged = null;
        DesignResizeRequested = null;
        MouseEnter -= Surface_OnMouseEnter;
        MouseMove -= Surface_OnMouseMove;
        MouseLeave -= Surface_OnMouseLeave;
        ClearDesignAdorners();
        _designElements.Clear();
        _widgetViews.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
    }

    private void ClearDesignAdorners()
    {
        foreach (var adorner in _designAdorners.Values)
        {
            AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        }
        _designAdorners.Clear();
        foreach (var adorner in _designBoundaryAdorners.Values)
        {
            AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        }
        _designBoundaryAdorners.Clear();
    }

    private void RegisterDesignBoundary(
        string instanceId,
        FrameworkElement view,
        LayoutContainerKind containerKind)
    {
        if (!_designMode)
        {
            return;
        }

        void Attach(object? sender, RoutedEventArgs args)
        {
            view.Loaded -= Attach;
            if (AdornerLayer.GetAdornerLayer(view) is not { } layer ||
                _designBoundaryAdorners.ContainsKey(instanceId))
            {
                return;
            }

            var adorner = new DesignBoundaryAdorner(view, containerKind);
            adorner.IsHitTestVisible = false;
            layer.Add(adorner);
            _designBoundaryAdorners[instanceId] = adorner;
        }

        if (view.IsLoaded)
        {
            Attach(view, new RoutedEventArgs());
        }
        else
        {
            view.Loaded += Attach;
        }
    }

    private static string FormatVerticalText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var starts = StringInfo.ParseCombiningCharacters(value);
        return string.Join(
            Environment.NewLine,
            starts.Select((start, index) =>
            {
                var end = index + 1 < starts.Length
                    ? starts[index + 1]
                    : value.Length;
                return value[start..end];
            }));
    }

    private static string GetCommandGlyph(MediaCommandKind command, bool isPlaying = false) => command switch
    {
        MediaCommandKind.Previous => "\uE892",
        MediaCommandKind.PlayPause => isPlaying ? "\uE769" : "\uE768",
        MediaCommandKind.Next => "\uE893",
        MediaCommandKind.SelectSource => "\uE8D6",
        MediaCommandKind.AdjustVolume => "\uE767",
        MediaCommandKind.SelectOutputDevice => "\uE7F5",
        _ => "\uE710"
    };

    private static string GetCommandTooltip(MediaCommandKind command, bool isPlaying = false) => command switch
    {
        MediaCommandKind.Previous => Loc.Get("Main.Control.Previous"),
        MediaCommandKind.PlayPause => isPlaying
            ? Loc.Get("Main.Control.Pause")
            : Loc.Get("Main.Control.Play"),
        MediaCommandKind.Next => Loc.Get("Main.Control.Next"),
        MediaCommandKind.SelectSource => Loc.Get("Main.Menu.ShowSource"),
        MediaCommandKind.AdjustVolume => Loc.Get("Main.Volume.Current"),
        MediaCommandKind.SelectOutputDevice => Loc.Get("Main.Device.Output"),
        _ => string.Empty
    };

    private static T? GetResource<T>(string key)
        where T : class
    {
        return Application.Current?.TryFindResource(key) as T;
    }

    private static Brush GetBrush(string key)
    {
        return GetResource<Brush>(key) ?? Brushes.Transparent;
    }

    private Brush GetContentBrush(string taskbarResourceKey)
    {
        return TryFindResource(ResolveContentResourceKey(taskbarResourceKey)) as Brush ??
            Brushes.Transparent;
    }

    private string ResolveContentResourceKey(string taskbarResourceKey)
    {
        if (!_useMenuThemeForContent)
        {
            return taskbarResourceKey;
        }

        return taskbarResourceKey switch
        {
            "TaskbarPrimaryTextBrush" or "TaskbarHighlightTextBrush" => "MenuPrimaryTextBrush",
            "TaskbarSecondaryTextBrush" => "MenuSecondaryTextBrush",
            "TaskbarDisabledTextBrush" => "MenuDisabledBrush",
            "TaskbarPressedBrush" => "MenuPressedBrush",
            "TaskbarDividerBrush" => "MenuSeparatorBrush",
            "TaskbarSurfaceBrush" or "TaskbarHoverBrush" or "TaskbarReadabilityBrush" => "MenuHoverBrush",
            _ => taskbarResourceKey
        };
    }

    private static void SetDynamicResource(
        FrameworkElement element,
        DependencyProperty property,
        string resourceKey) =>
        element.SetResourceReference(property, resourceKey);

    private sealed class ContainerVisual
    {
        internal ContainerVisual(LayoutContainerElement model, LayoutGridRect bounds)
        {
            Model = model;
            // 外框尺寸由调用方按网格矩形设置（BuildAbsoluteLayout 统一赋 Width/Height）。
            for (var index = 0; index < 2; index++)
            {
                var slot = new Grid();
                Slots.Add(slot);
                Host.Children.Add(slot);
            }
        }

        internal LayoutContainerElement Model { get; }
        internal Grid Host { get; } = new();
        internal List<Grid> Slots { get; } = [];
        internal bool PointerNear { get; set; }
        internal int ActiveSlot { get; set; } = -1;
        internal int TransitionVersion { get; set; }
    }

    private sealed record ElementPresentation(
        Point VisualPosition,
        Point BasePosition,
        double Opacity,
        bool WasVisible);

    private sealed class MarqueeState(TextBlock text, string content, int offset)
    {
        internal TextBlock Text { get; } = text;
        internal string Content { get; set; } = content;
        internal int Offset { get; set; } = offset;
    }

    private sealed class MetricViewState(TextBlock text, MetricsWidgetSettings settings)
    {
        internal TextBlock Text { get; } = text;
        internal MetricsWidgetSettings Settings { get; } = settings;
        internal long LastUpdateTick { get; set; }
        internal int CycleIndex { get; set; }
    }

    /// <summary>
    /// 选择框叠加在 AdornerLayer：四边 Thumb 只命中手柄区域，不改变真实组件测量。
    /// The editor-only selection frame lives on the AdornerLayer; the four edge Thumbs hit only their handles and never change runtime measurement.
    /// </summary>
    private sealed class DesignSelectionAdorner : Adorner
    {
        private const double HandleSize = 7;
        private readonly Pen _pen = new(
            new SolidColorBrush(Color.FromRgb(86, 156, 255)),
            1.5);
        private readonly Thumb _leftThumb;
        private readonly Thumb _topThumb;
        private readonly Thumb _rightThumb;
        private readonly Thumb _bottomThumb;

        internal DesignSelectionAdorner(
            UIElement adornedElement,
            Action<LayoutEdge, double> resizeRequested) : base(adornedElement)
        {
            _leftThumb = CreateThumb(Cursors.SizeWE, resizeRequested, LayoutEdge.Left);
            _topThumb = CreateThumb(Cursors.SizeNS, resizeRequested, LayoutEdge.Top);
            _rightThumb = CreateThumb(Cursors.SizeWE, resizeRequested, LayoutEdge.Right);
            _bottomThumb = CreateThumb(Cursors.SizeNS, resizeRequested, LayoutEdge.Bottom);
            AddVisualChild(_leftThumb);
            AddVisualChild(_topThumb);
            AddVisualChild(_rightThumb);
            AddVisualChild(_bottomThumb);
        }

        private static Thumb CreateThumb(
            Cursor cursor,
            Action<LayoutEdge, double> resizeRequested,
            LayoutEdge edge)
        {
            var thumb = new Thumb
            {
                Width = HandleSize,
                Height = HandleSize,
                Cursor = cursor,
                Background = new SolidColorBrush(Color.FromRgb(86, 156, 255)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Opacity = 0.95,
                IsTabStop = false
            };
            thumb.DragDelta += (_, args) =>
            {
                var delta = edge is LayoutEdge.Left or LayoutEdge.Right
                    ? args.HorizontalChange
                    : args.VerticalChange;
                if (Math.Abs(delta) > 0.01)
                {
                    resizeRequested(edge, delta);
                }
            };
            return thumb;
        }

        protected override int VisualChildrenCount => 4;

        protected override Visual GetVisualChild(int index) => index switch
        {
            0 => _leftThumb,
            1 => _topThumb,
            2 => _rightThumb,
            3 => _bottomThumb,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        protected override Size ArrangeOverride(Size finalSize)
        {
            var half = HandleSize / 2;
            var midX = finalSize.Width / 2 - half;
            var midY = finalSize.Height / 2 - half;
            _leftThumb.Arrange(new Rect(-half, midY, HandleSize, HandleSize));
            _rightThumb.Arrange(new Rect(finalSize.Width - half, midY, HandleSize, HandleSize));
            _topThumb.Arrange(new Rect(midX, -half, HandleSize, HandleSize));
            _bottomThumb.Arrange(new Rect(midX, finalSize.Height - half, HandleSize, HandleSize));
            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var size = AdornedElement.RenderSize;
            var rect = new Rect(0.75, 0.75, Math.Max(0, size.Width - 1.5), Math.Max(0, size.Height - 1.5));
            drawingContext.DrawRectangle(null, _pen, rect);
        }
    }

    /// <summary>
    /// 编辑器持续显示容器轮廓；折叠容器使用虚线和最小触发区，避免只能点中展开内容。
    /// Keeps container outlines visible in the editor; dashed lines and a minimum trigger footprint make edge containers selectable even when empty.
    /// </summary>
    private sealed class DesignBoundaryAdorner(
        UIElement adornedElement,
        LayoutContainerKind containerKind) : Adorner(adornedElement)
    {
        private readonly Pen _pen = CreatePen(containerKind);

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var size = AdornedElement.RenderSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            var rect = new Rect(0.75, 0.75, Math.Max(0, size.Width - 1.5), Math.Max(0, size.Height - 1.5));
            drawingContext.DrawRectangle(null, _pen, rect);
        }

        private static Pen CreatePen(LayoutContainerKind kind)
        {
            var color = kind switch
            {
                LayoutContainerKind.HoverSwitch => Color.FromRgb(255, 190, 76),
                LayoutContainerKind.AutoCollapse => Color.FromRgb(255, 115, 115),
                _ => Color.FromRgb(120, 205, 255)
            };
            var pen = new Pen(new SolidColorBrush(color), 1.25);
            if (kind != LayoutContainerKind.Static)
            {
                pen.DashStyle = DashStyles.Dash;
            }
            return pen;
        }
    }

    private sealed class SpectrumView(
        int bandCount,
        int refreshRateHz,
        int sensitivityPercent,
        string brushResourceKey) : FrameworkElement
    {
        private readonly float[] _values = new float[AudioMonitorService.BandCount];
        private long _lastRenderTick;

        internal void SetValues(IReadOnlyList<float> values)
        {
            var now = Environment.TickCount64;
            if (now - _lastRenderTick < 1_000 / refreshRateHz)
            {
                return;
            }

            _lastRenderTick = now;
            var count = Math.Min(values.Count, _values.Length);
            for (var index = 0; index < count; index++)
            {
                _values[index] = Math.Clamp(
                    values[index] * sensitivityPercent / 100f,
                    0,
                    1);
            }
            for (var index = count; index < _values.Length; index++)
            {
                _values[index] = 0;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var width = ActualWidth > 0 ? ActualWidth : 68;
            var height = ActualHeight > 0 ? ActualHeight : 24;
            var gap = 3d;
            var barWidth = Math.Max(2, (width - gap * (bandCount - 1)) / bandCount);
            for (var index = 0; index < bandCount; index++)
            {
                var barHeight = Math.Clamp(3 + Math.Sqrt(_values[index]) * (height - 3), 3, height);
                var x = index * (barWidth + gap);
                drawingContext.DrawRoundedRectangle(
                    TryFindResource(brushResourceKey) as Brush ?? Brushes.Transparent,
                    null,
                    new Rect(x, (height - barHeight) / 2, barWidth, barHeight),
                    2,
                    2);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(Math.Min(88, availableSize.Width), 24);
        }
    }
}
