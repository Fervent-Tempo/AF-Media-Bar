// 每个悬浮宿主拥有自己的组件窗口组；手势只在所属显示器活动，正常位置与预览分离。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private readonly Dictionary<TaskbarRestComponent, (DetachedComponentVisual Visual, ComponentFloatingWindow Window)> _componentWindows = [];
    private Func<DetachedComponentVisual, ComponentFloatingWindow>? _componentWindowFactory;
    private ComponentFloatingWindow? _componentGhostWindow;
    private TaskbarRestComponent? _movingComponent;
    private bool _componentRefreshQueued;
    private bool _componentsActivated;
    private bool _bodyDragCancelled;
    private FloatingBarDockEdge _bodyDragDockEdge;
    private TaskbarRestComponent? _componentMenuTarget;
    internal ContextMenu? ActiveComponentMenu { get; set; }
    private bool CanSaveFloatingComponent => !_isClosing && !_suspended && !IsBackgroundPruned &&
        string.Equals(TargetMonitorDeviceId, Monitor.DeviceId, StringComparison.OrdinalIgnoreCase);

    private void InitializeFloatingComponents(Func<DetachedComponentVisual, ComponentFloatingWindow>? factory)
    {
        _componentWindowFactory = factory;
        _mediaControl.ComponentPlacementAllowed = () => CanSaveFloatingComponent;
        _mediaControl.ComponentMoveIntent += FloatingComponentMove;
        _mediaControl.DetachedVisualsChanged += FloatingComponentsChanged;
        _mediaControl.ComponentReturnRequested += ReturnFloatingComponent;
        _mediaControl.ComponentContextMenuRequested += FloatingComponentMenuRequested;
    }

    private void FloatingComponentsChanged(object? sender, EventArgs args)
    {
        if (_componentRefreshQueued || _isClosing) return;
        _componentRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _componentRefreshQueued = false;
            if (!_isClosing && _componentsActivated) RefreshFloatingComponents();
        });
    }

    private Rect ClampComponent(Rect physical)
    {
        var area = PlacementArea;
        var width = Math.Min(area.Width, Math.Max(1, physical.Width));
        var height = Math.Min(area.Height, Math.Max(1, physical.Height));
        return new Rect(Math.Clamp(physical.Left, area.Left, area.Right - width),
            Math.Clamp(physical.Top, area.Top, area.Bottom - height), width, height);
    }

    private void RefreshFloatingComponents()
    {
        var visuals = _mediaControl.DetachedVisuals.ToDictionary(visual => visual.Component);
        foreach (var obsolete in _componentWindows.Keys.Where(key => !visuals.TryGetValue(key, out var visual) ||
            !ReferenceEquals(_componentWindows[key].Visual, visual)).ToArray())
        { _componentWindows[obsolete].Window.Dispose(); _componentWindows.Remove(obsolete); }
        var positions = MonitorBarProfilePolicy.Find(SettingsManager.Current, BarConfigurationTarget.FloatingBar, TargetMonitorDeviceId)?.Detached ?? [];
        foreach (var visual in visuals.Values)
        {
            if (_movingComponent == visual.Component) continue;
            if (!_componentWindows.TryGetValue(visual.Component, out var entry))
            {
                var window = (_componentWindowFactory ?? throw new InvalidOperationException("Component window factory is unavailable."))(visual);
                entry = (visual, window);
                _componentWindows.Add(visual.Component, entry);
            }
            var saved = positions.FirstOrDefault(position => position.Component == visual.Component);
            var center = new Point(Monitor.WorkArea.Left + (saved?.PrimaryDip ?? Monitor.WorkArea.Width / Dpi / 2) * Dpi,
                Monitor.WorkArea.Top + (saved?.CrossDip ?? Monitor.WorkArea.Height / Dpi / 2) * Dpi);
            var size = new Size(visual.SizeDip.Width * Dpi, visual.SizeDip.Height * Dpi);
            var bounds = ClampComponent(new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height));
            entry.Window.Place(bounds, _layer, _overlayTaskbar, _componentsActivated && !_suspended && !IsBackgroundPruned &&
                !_foregroundFullscreen && SettingsManager.Current.TaskbarBarEnabled && visual.HasContent);
        }
        _mediaControl.RefreshComponentSamplingVisibility();
        SynchronizeResources();
    }

    private void FloatingComponentMove(object? sender, ComponentMoveIntentEventArgs intent)
    {
        if (intent.Cancelled || !CanSaveFloatingComponent)
        {
            _componentGhostWindow?.Dispose(); _componentGhostWindow = null;
            var restore = _movingComponent.HasValue;
            _movingComponent = null;
            if (restore && CanSaveFloatingComponent && _componentsActivated) RefreshFloatingComponents();
            return;
        }
        var bounds = ClampComponent(intent.ScreenBounds);
        if (!intent.Finished)
        {
            if (_componentWindows.TryGetValue(intent.Component, out var moving))
            {
                _movingComponent = intent.Component;
                moving.Window.Place(bounds, _layer, _overlayTaskbar, true);
                return;
            }
            if (_componentGhostWindow is null)
            {
                var grid = new Grid
                {
                    IsHitTestVisible = false,
                    Opacity = .8,
                    Background = new VisualBrush(_mediaControl.GetComponentVisual(intent.Component)) { Stretch = Stretch.Fill }
                };
                _componentGhostWindow = (_componentWindowFactory ?? throw new InvalidOperationException("Component window factory is unavailable."))
                    (new DetachedComponentVisual(intent.Component, grid));
                _componentGhostWindow.IsHitTestVisible = false;
            }
            _componentGhostWindow.Place(bounds, _layer, _overlayTaskbar, true);
            return;
        }
        _componentGhostWindow?.Dispose(); _componentGhostWindow = null;
        _movingComponent = null;
        var position = intent.Dock ? null : new DetachedComponentPosition(intent.Component,
            (bounds.Left + bounds.Width / 2 - Monitor.WorkArea.Left) / Dpi,
            (bounds.Top + bounds.Height / 2 - Monitor.WorkArea.Top) / Dpi);
        var request = new ComponentPlacementRequest(BarConfigurationTarget.FloatingBar, TargetMonitorDeviceId, intent.Component,
            position, intent.State, intent.OriginalEntries, intent.TargetIndex);
        if (_commands.CommitComponentPlacementCommand.CanExecute(request)) _commands.CommitComponentPlacementCommand.Execute(request);
        RefreshFloatingComponents();
    }

    private void SuspendFloatingComponents()
    {
        if (ActiveComponentMenu is { } menu) menu.IsOpen = false;
        ActiveComponentMenu = null;
        _mediaControl.CancelComponentPlacement();
        _movingComponent = null;
        _componentGhostWindow?.Dispose(); _componentGhostWindow = null;
        foreach (var entry in _componentWindows.Values) entry.Window.Suspend();
    }

    private void DisposeFloatingComponents()
    {
        SuspendFloatingComponents();
        _mediaControl.ComponentMoveIntent -= FloatingComponentMove;
        _mediaControl.DetachedVisualsChanged -= FloatingComponentsChanged;
        _mediaControl.ComponentReturnRequested -= ReturnFloatingComponent;
        _mediaControl.ComponentContextMenuRequested -= FloatingComponentMenuRequested;
        foreach (var entry in _componentWindows.Values) entry.Window.Dispose();
        _componentWindows.Clear();
        _mediaControl.ReleaseDetachedVisuals();
        _mediaControl.ComponentPlacementAllowed = null;
    }

    private void ReturnFloatingComponent(TaskbarRestComponent component)
    {
        if (!CanSaveFloatingComponent) return;
        var entries = _mediaControl.ComponentEntries;
        var request = new ComponentPlacementRequest(BarConfigurationTarget.FloatingBar, TargetMonitorDeviceId, component, null,
            _mediaControl.ComponentLayoutState, entries, entries.ToList().FindIndex(entry => entry.Component == component));
        if (_commands.CommitComponentPlacementCommand.CanExecute(request)) _commands.CommitComponentPlacementCommand.Execute(request);
    }

    private void FloatingComponentMenuRequested(object? sender, ComponentContextMenuRequestEventArgs request)
    {
        if (!IsOperational) return;
        _componentMenuTarget = request.Component;
        IsPerformanceMenuRequested = SettingsManager.Current.Experiments.MemoryTools &&
            (request.Component == TaskbarRestComponent.Performance || Classes.Services.ComponentLayoutPolicy.IsMetric(request.Component));
        IsBatteryMenuRequested = request.Component == TaskbarRestComponent.BluetoothBattery;
        PlayerMenuOpenRequested?.Invoke(this, request.Position);
    }

    internal MenuItem? CreateComponentReturnMenuItem()
    {
        if (_componentMenuTarget is not { } component) return null;
        var item = new MenuItem { IsEnabled = CanSaveFloatingComponent && !PresentationSettings.TaskbarBarPositionLocked };
        item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc.ComponentPlacement.Attach");
        item.Click += (_, _) => ReturnFloatingComponent(component);
        return item;
    }
}
