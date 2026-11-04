// 本控件继续拥有组件数据与交互；独立宿主只借用组件视觉树，结束时归还原父容器。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

/// <summary>一个组件的独立呈现容器；原始控件与采样状态仍由媒体控件拥有。</summary>
public sealed class DetachedComponentVisual(TaskbarRestComponent component, Grid view)
{
    public TaskbarRestComponent Component { get; } = component;
    public Grid View { get; } = view;
    public Size SizeDip { get; internal set; }
    public bool HasContent { get; internal set; }
    public bool IsPlaced { get; set; }
    internal Action<PlayerForegroundDecision?>? ForegroundChanged { get; set; }
    internal TaskbarHoverPalette? HoverPalette { get; set; }
    internal List<(FrameworkElement Element, Panel Parent, int Index, Action RestoreLayout)> Roots { get; } = [];
}

public partial class TaskBarMediaControl
{
    private sealed class ComponentNameScope(TaskBarMediaControl owner) : INameScope
    {
        public object? FindName(string name) => owner.FindName(name);
        public void RegisterName(string name, object scopedElement) => throw new NotSupportedException();
        public void UnregisterName(string name) => throw new NotSupportedException();
    }
    private readonly Dictionary<TaskbarRestComponent, DetachedComponentVisual> _detachedVisuals = [];
    private bool _updatingDetachedVisuals;
    private readonly Dictionary<TaskbarRestComponent, PlayerForegroundDecision?> _componentForegroundDecisions = [];
    public BarConfigurationTarget? ComponentTarget { get; private set; }
    public string? ComponentMonitorDeviceId { get; private set; }
    public IReadOnlyCollection<DetachedComponentVisual> DetachedVisuals => _detachedVisuals.Values;
    public event EventHandler? DetachedVisualsChanged;
    public event EventHandler<ComponentMoveIntentEventArgs>? ComponentMoveIntent;
    public event Action<TaskbarRestComponent>? ComponentReturnRequested;
    public event EventHandler<ComponentContextMenuRequestEventArgs>? ComponentContextMenuRequested;
    public BarLayoutState ComponentLayoutState => RestLayoutState;
    public IReadOnlyList<ComponentLayoutEntry> ComponentEntries => EffectiveRestEntries();
    public Func<bool>? ComponentPlacementAllowed { get; set; }
    public bool HasDetachedContent => _detachedVisuals.Values.Any(visual => visual.HasContent);
    public bool HasVisibleDetachedContent => _detachedVisuals.Values.Any(visual => visual.HasContent && visual.IsPlaced);

    public void RefreshComponentSamplingVisibility()
    {
        var attached = EffectiveRestComponents();
        bool Visible(TaskbarRestComponent component) => attached.Contains(component) ||
            _detachedVisuals.TryGetValue(component, out var visual) && visual.HasContent && visual.IsPlaced;
        IsSpectrumComponentVisible = Visible(TaskbarRestComponent.Spectrum);
        IsPerformanceComponentVisible = Visible(TaskbarRestComponent.Performance) || ComponentLayoutPolicy.MetricComponents.Any(Visible);
    }

    public void SetComponentScope(BarConfigurationTarget target, string monitorDeviceId)
    {
        ComponentTarget = target;
        ComponentMonitorDeviceId = monitorDeviceId;
        ApplyPresentationSettings(MonitorBarProfilePolicy.Project(SettingsManager.Current, target, monitorDeviceId));
    }

    public void RefreshComponentScope()
    {
        if (ComponentTarget is { } target && ComponentMonitorDeviceId is { } monitor)
            ApplyPresentationSettings(MonitorBarProfilePolicy.Project(SettingsManager.Current, target, monitor));
    }
    public bool ComponentScopeMatchesCurrent => ComponentTarget is not { } target || MonitorBarProfilePolicy.Same(
        MonitorBarProfilePolicy.Find(PresentationSettings, target, ComponentMonitorDeviceId),
        MonitorBarProfilePolicy.Find(SettingsManager.Current, target, ComponentMonitorDeviceId));

    private IReadOnlyList<DetachedComponentPosition> DetachedPositions => ComponentTarget is { } target
        ? MonitorBarProfilePolicy.Find(PresentationSettings, target, ComponentMonitorDeviceId)?.Detached ?? [] : [];
    private bool IsDetached(TaskbarRestComponent component) => DetachedPositions.Any(position => position.Component == component);
    private bool IsDetachedElement(FrameworkElement element) => _detachedVisuals.Values.Any(visual =>
        visual.Roots.Any(root => ReferenceEquals(root.Element, element)));
    public bool IsDetachedComponentSource(DependencyObject? source)
    {
        for (var node = source; node is not null; node = ParentOf(node))
            if (_detachedVisuals.Values.Any(visual => ReferenceEquals(visual.View, node))) return true;
        return false;
    }

    private IEnumerable<FrameworkElement> ComponentRoots(TaskbarRestComponent component)
    {
        if (component == TaskbarRestComponent.Performance) return _metricSurfaces.Values;
        if (ComponentLayoutPolicy.IsMetric(component))
            return _metricSurfaces.TryGetValue(ComponentLayoutPolicy.Metric(component), out var metric) ? [metric] : [];
        return component switch
        {
            TaskbarRestComponent.Spectrum => [TaskbarSpectrumHoverSurface],
            TaskbarRestComponent.BluetoothBattery => [TaskbarBatterySurface],
            TaskbarRestComponent.OutputDevice => [TaskbarOutputDeviceHoverSurface],
            TaskbarRestComponent.InputDevice => [TaskbarInputDeviceHoverSurface],
            TaskbarRestComponent.Volume => [TaskbarVolumeHoverSurface],
            TaskbarRestComponent.PreviousTrack => [ResidentPreviousSurface],
            TaskbarRestComponent.PlayPause => [ResidentPlayPauseSurface],
            TaskbarRestComponent.NextTrack => [ResidentNextSurface],
            _ => []
        };
    }

    public Visual? GetComponentVisual(TaskbarRestComponent component) =>
        _detachedVisuals.TryGetValue(component, out var visual) ? visual.View :
            ComponentRoots(component).FirstOrDefault(element => element.Visibility == Visibility.Visible) ?? ComponentRoots(component).FirstOrDefault();

    public Size GetComponentSize(TaskbarRestComponent component)
    {
        var metrics = TaskbarDensityMetrics.From(PresentationSettings.TaskbarExperience.Density);
        var cross = Math.Max(1, (_isVertical ? MainBorder.Width : MainBorder.Height) - 4);
        return _isVertical ? new Size(cross, Math.Max(1, VerticalComponentLength(component, metrics)))
            : new Size(Math.Max(1, ResolveRestComponentWidth(component, metrics)), cross);
    }

    private static Action CaptureComponentLayout(FrameworkElement element)
    {
        var properties = new[] { WidthProperty, HeightProperty, MarginProperty, HorizontalAlignmentProperty,
            VerticalAlignmentProperty, RenderTransformProperty, Canvas.LeftProperty, Canvas.TopProperty };
        var values = properties.Select(property => (Property: property, Local: element.ReadLocalValue(property),
            Binding: BindingOperations.GetBindingBase(element, property))).ToArray();
        return () =>
        {
            foreach (var value in values)
                if (value.Binding is not null) BindingOperations.SetBinding(element, value.Property, value.Binding);
                else if (value.Local == DependencyProperty.UnsetValue) element.ClearValue(value.Property);
                else element.SetValue(value.Property, value.Local);
        };
    }

    private void RestoreAttachedComponents()
    {
        var entries = EffectiveRestEntries();
        var restored = false;
        foreach (var component in _detachedVisuals.Keys.Where(component => !IsDetached(component) ||
            !entries.Any(entry => entry.Component == component)).ToArray())
        { RestoreDetachedVisual(component); restored = true; }
        if (restored) DetachedVisualsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDetachedVisuals()
    {
        if (_updatingDetachedVisuals || ComponentTarget is null) return;
        _updatingDetachedVisuals = true;
        try
        {
            var changed = false;
            var entries = EffectiveRestEntries();
            var desired = DetachedPositions.Where(position => entries.Any(entry => entry.Component == position.Component))
                .Select(position => position.Component).ToHashSet();
            foreach (var component in _detachedVisuals.Keys.Where(component => !desired.Contains(component)).ToArray())
            { RestoreDetachedVisual(component); changed = true; }
            foreach (var component in desired)
            {
                if (!_detachedVisuals.TryGetValue(component, out var visual))
                {
                    var grid = new Grid();
                    // 分层窗口的零 alpha 像素会穿透；整块命中底与本体保持一致。
                    grid.SetBinding(Panel.BackgroundProperty, new Binding(nameof(Panel.Background)) { Source = InteractionSurface });
                    grid.Resources.MergedDictionaries.Add(Resources);
                    NameScope.SetNameScope(grid, new ComponentNameScope(this));
                    // 重新挂到窗口后不再继承媒体控件的数据上下文和前景，显式连接原所有者。
                    grid.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = this });
                    grid.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = SongTitle });
                    visual = new(component, grid);
                    visual.ForegroundChanged = decision =>
                    {
                        _componentForegroundDecisions[component] = decision;
                        ApplyDetachedForeground(component, decision);
                    };
                    foreach (var element in ComponentRoots(component))
                    {
                        if (element.Parent is not Panel parent) continue;
                        visual.Roots.Add((element, parent, parent.Children.IndexOf(element), CaptureComponentLayout(element)));
                        parent.Children.Remove(element);
                        grid.Children.Add(element);
                    }
                    if (visual.Roots.Count == 0) continue;
                    _detachedVisuals.Add(component, visual);
                    changed = true;
                    grid.PreviewMouseLeftButtonDown += (_, args) => DetachedPointerDown(component, args);
                    grid.PreviewMouseMove += ComponentPointerMove;
                    grid.PreviewMouseLeftButtonUp += ComponentPointerUp;
                    grid.PreviewKeyDown += ComponentKeyDown;
                    grid.LostMouseCapture += ComponentCaptureLost;
                    var menu = new ContextMenu();
                    var attach = new MenuItem();
                    attach.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc.ComponentPlacement.Attach");
                    attach.Click += (_, _) =>
                    {
                        if (ComponentPlacementAllowed?.Invoke() == true && !PresentationSettings.TaskbarBarPositionLocked &&
                            ComponentTarget is not null && ComponentMonitorDeviceId is not null)
                            ComponentReturnRequested?.Invoke(component);
                    };
                    menu.Items.Add(attach);
                    grid.ContextMenu = menu;
                    grid.ContextMenuOpening += (_, args) =>
                    {
                        if (ComponentContextMenuRequested is null) return;
                        args.Handled = true;
                        if (HasComponentGesture || !visual.HasContent || !visual.IsPlaced) return;
                        var point = grid.PointToScreen(new Point(grid.ActualWidth / 2, grid.ActualHeight / 2));
                        ComponentContextMenuRequested.Invoke(this, new(component, point));
                    };
                }
                var nextSize = GetComponentSize(component);
                var nextContent = entries.Any(entry => entry.Component == component && entry.IsVisible) &&
                    (component != TaskbarRestComponent.BluetoothBattery || _batteryCanShow);
                changed |= visual.SizeDip != nextSize || visual.HasContent != nextContent;
                visual.SizeDip = nextSize;
                visual.HasContent = nextContent;
                visual.View.Width = visual.SizeDip.Width;
                visual.View.Height = visual.SizeDip.Height;
                var cycle = component == TaskbarRestComponent.Performance;
                var current = _performanceMetrics.Count == 0 ? MetricKind.SystemMemory :
                    _performanceMetrics[Math.Clamp(_performanceCycleIndex, 0, _performanceMetrics.Count - 1)];
                foreach (var root in visual.Roots)
                {
                    if (component == TaskbarRestComponent.Performance || ComponentLayoutPolicy.IsMetric(component)) root.Element.Tag = component;
                    var visible = visual.HasContent && (!cycle || ReferenceEquals(root.Element, _metricSurfaces[current]));
                    SetRestComponentVisible(root.Element, visible);
                    root.Element.Width = visual.SizeDip.Width;
                    root.Element.Height = visual.SizeDip.Height;
                    root.Element.Margin = new Thickness(0);
                    root.Element.VerticalAlignment = VerticalAlignment.Center;
                    root.Element.HorizontalAlignment = HorizontalAlignment.Center;
                    root.Element.RenderTransform = Transform.Identity;
                }
            }
            if (changed) DetachedVisualsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _updatingDetachedVisuals = false; }
    }

    private void RestoreDetachedVisual(TaskbarRestComponent component)
    {
        if (!_detachedVisuals.Remove(component, out var visual)) return;
        visual.View.ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        foreach (var root in visual.Roots)
        {
            visual.View.Children.Remove(root.Element);
            root.RestoreLayout();
            root.Parent.Children.Insert(Math.Min(root.Index, root.Parent.Children.Count), root.Element);
        }
        _componentForegroundDecisions.Remove(component);
        ApplyAppearanceSettings();
    }

    private void ApplyDetachedForeground(TaskbarRestComponent component, PlayerForegroundDecision? decision)
    {
        if (!_detachedVisuals.TryGetValue(component, out var visual)) return;
        var fallbackLight = SongTitle.Foreground is not SolidColorBrush solid || solid.Color.R > 128;
        var presentation = PlayerForegroundPolicy.ResolvePresentation(PresentationSettings.Appearance.PlayerForegroundMode,
            SystemParameters.HighContrast, fallbackLight, decision);
        var brush = presentation.UsesSystemColors ? SystemColors.WindowTextBrush : presentation.UsesLightText ? Brushes.White : DarkPlayerForeground;
        var palette = TaskbarHoverPalettePolicy.Resolve(brush.Color);
        visual.HoverPalette = palette;
        visual.View.Resources["TaskbarHoverForegroundBrush"] = new SolidColorBrush(palette.Foreground);
        visual.View.Resources["TaskbarHoverButtonOverBrush"] = new SolidColorBrush(palette.ButtonHover);
        visual.View.Resources["TaskbarHoverButtonPressedBrush"] = new SolidColorBrush(palette.ButtonPressed);
        void Apply(DependencyObject element)
        {
            if (element is TextBlock text) text.SetCurrentValue(TextBlock.ForegroundProperty, brush);
            if (element is Control control) control.SetCurrentValue(Control.ForegroundProperty, brush);
            if (element is Wpf.Ui.Controls.SymbolIcon icon) icon.SetCurrentValue(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, brush);
            if (element is MetricPresenter) TextElement.SetForeground(element, brush);
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) Apply(VisualTreeHelper.GetChild(element, index));
        }
        Apply(visual.View);
        foreach (var root in visual.Roots)
            if (root.Element is Border surface) AnimateComponentHover(surface, surface.IsMouseOver);
        if (component == TaskbarRestComponent.Spectrum) ApplySpectrumForeground(brush);
    }

    private void ReapplyDetachedForeground()
    {
        foreach (var component in _detachedVisuals.Keys)
            ApplyDetachedForeground(component, _componentForegroundDecisions.GetValueOrDefault(component));
    }

    public void ReleaseDetachedVisuals()
    {
        CancelComponentGesture();
        foreach (var component in _detachedVisuals.Keys.ToArray()) RestoreDetachedVisual(component);
    }
}
