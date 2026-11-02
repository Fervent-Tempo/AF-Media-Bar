// 同一任务栏 HWND 承载所有独立组件；窗口拥有预览与区域，组件数据继续由 MediaControl 拥有。
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Windows;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarWindow
{
    private readonly Dictionary<TaskbarRestComponent, RECT> _detachedRects = [];
    private readonly HashSet<Grid> _detachedViews = [];
    private ICommand? _componentPlacementCommand;
    private Rectangle? _componentGhost;
    private RECT? _componentGhostRect;
    private int? _bodyDragPrimary;
    private int _bodyDragLength;
    private bool _updatingDetachedLayout;
    private readonly Dictionary<TaskbarRestComponent, DetachedComponentPosition> _appliedComponentPreferences = [];
    private IReadOnlyList<TaskbarPrimaryRange> _componentSafeRanges = [];
    private TaskbarRestComponent? _componentMenuTarget;
    internal ContextMenu? ActiveComponentMenu { get; set; }
    private ScreenBackgroundSampler? _componentSampler;
    private readonly Dictionary<TaskbarRestComponent, (DetachedComponentVisual Visual, AdaptiveForegroundSamplingSession Session)> _componentColors = [];
    private bool HasAnyTaskbarContent => !MediaControl.IsRestLayerEmpty || MediaControl.HasDetachedContent;
    private AppSettings TaskbarPresentation => MonitorBarProfilePolicy.Project(SettingsManager.Current, BarConfigurationTarget.Taskbar, _targetMonitorDeviceId);
    private bool CanSaveComponentPlacement => !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned && !IsTaskbarPresentationSuspended &&
        _lastTaskbarHandle != IntPtr.Zero && string.Equals(MonitorUtil.GetMonitor(_lastTaskbarHandle).deviceId,
            _targetMonitorDeviceId, StringComparison.OrdinalIgnoreCase);

    private void InitializeDetachedComponents(TaskbarWindowViewModel? commands, ScreenBackgroundSampler sampler)
    {
        _componentSampler = sampler;
        _componentPlacementCommand = commands?.CommitComponentPlacementCommand;
        MediaControl.ComponentMoveIntent += OnComponentMoveIntent;
        MediaControl.DetachedVisualsChanged += OnDetachedVisualsChanged;
        MediaControl.ComponentPlacementAllowed = () => CanSaveComponentPlacement;
        MediaControl.ComponentReturnRequested += ReturnComponent;
        MediaControl.ComponentContextMenuRequested += ComponentContextMenuRequested;
    }

    private void OnDetachedVisualsChanged(object? sender, EventArgs args)
    {
        if (_isClosing || _updatingDetachedLayout) return;
        // 控件布局可能正在请求尺寸；合并到已存在的位置队列，避免重入本次布局。
        Dispatcher.BeginInvoke(() => { if (!_isClosing && !IsTaskbarPresentationSuspended) UpdatePosition(); });
    }

    private IReadOnlyList<TaskbarPrimaryRange> CurrentComponentRanges(RECT taskbar, double dpi, bool duringGesture = false)
    {
        var length = IsVerticalPresentation ? taskbar.Bottom - taskbar.Top : taskbar.Right - taskbar.Left;
        if (!SettingsManager.Current.TaskbarBarAvoidIcons)
            return [new(Math.Min(EdgePadding, length), Math.Max(Math.Min(EdgePadding, length), length - EdgePadding))];
        if (_hostActions.IsEnvironmentRecovering || IsTaskbarPresentationSuspended || DateTime.UtcNow < _skipOccupiedAreaProbeUntilUtc) return [];
        if (duringGesture && (_isDragging || MediaControl.HasComponentGesture)) return _componentSafeRanges;
        var snapshot = _occupiedAreaService.GetSnapshot(_lastTaskbarHandle, taskbar,
            _appliedOrientation ?? LayoutOrientation.Horizontal, dpi, EdgePadding);
        // 未知结果不得让释放后的对象继续压住图标；等待同环境成功发布再恢复。
        _componentSafeRanges = snapshot.Status == TaskbarProbeStatus.Success ? snapshot.Ranges : [];
        return _componentSafeRanges;
    }

    private TaskbarPrimaryRange PrimaryRange(RECT rect) => IsVerticalPresentation
        ? new(rect.Top, rect.Bottom) : new(rect.Left, rect.Right);

    private void LayoutDetachedComponents(RECT taskbar, double dpi, RECT body)
    {
        if (_updatingDetachedLayout || _isDragging) return;
        _updatingDetachedLayout = true;
        try
        {
            var visuals = MediaControl.DetachedVisuals;
            var activeViews = visuals.Select(visual => visual.View).ToHashSet();
            foreach (var obsolete in _detachedViews.Where(view => !activeViews.Contains(view)).ToArray())
            { WidgetCanvas.Children.Remove(obsolete); _detachedViews.Remove(obsolete); }
            var profiles = MonitorBarProfilePolicy.Find(SettingsManager.Current, BarConfigurationTarget.Taskbar, _targetMonitorDeviceId)?.Detached ?? [];
            var free = CurrentComponentRanges(taskbar, dpi, duringGesture: true);
            if (HasArea(body)) free = ComponentPlacementPolicy.Subtract(free,
                [PrimaryRange(TaskbarArrangementPolicy.ExtendZoomOverflow(body, _artworkZoomExtensionPx,
                    IsVerticalPresentation, MediaControl.ResolvedTaskbarArrangement))], (int)Math.Ceiling(4 * dpi));
            var previous = new Dictionary<TaskbarRestComponent, RECT>(_detachedRects);
            _detachedRects.Clear();
            // 先保留仍合法的位置，只有失效项才重新分配。
            var ordered = visuals.OrderBy(visual => previous.ContainsKey(visual.Component) ? 0 : 1).ThenBy(visual => visual.Component);
            foreach (var visual in ordered)
            {
                if (_detachedViews.Add(visual.View)) WidgetCanvas.Children.Add(visual.View);
                var primarySize = (int)Math.Ceiling((IsVerticalPresentation ? visual.SizeDip.Height : visual.SizeDip.Width) * dpi);
                var crossLength = IsVerticalPresentation ? taskbar.Right - taskbar.Left : taskbar.Bottom - taskbar.Top;
                var crossSize = Math.Min(crossLength, (int)Math.Ceiling((IsVerticalPresentation ? visual.SizeDip.Width : visual.SizeDip.Height) * dpi));
                var preference = profiles.FirstOrDefault(position => position.Component == visual.Component);
                var desired = (int)Math.Round((preference?.PrimaryDip ?? 0) * dpi);
                if (preference is not null && _appliedComponentPreferences.GetValueOrDefault(visual.Component) == preference &&
                    previous.TryGetValue(visual.Component, out var stable))
                {
                    var prior = PrimaryRange(stable);
                    if (prior.Length == primarySize && free.Any(range => range.Start <= prior.Start && range.End >= prior.End)) desired = prior.Start;
                }
                var primary = visual.HasContent ? ComponentPlacementPolicy.Nearest(free, desired, primarySize) : null;
                visual.IsPlaced = primary.HasValue;
                visual.View.Visibility = primary.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (primary is not { } start) continue;
                var preferredCross = preference is null ? (crossLength - crossSize) / 2d : preference.CrossDip * dpi;
                var cross = (int)Math.Round(Math.Clamp(preferredCross, 0, Math.Max(0, crossLength - crossSize)));
                var rectangle = IsVerticalPresentation ? new RECT { Left = cross, Top = start, Right = cross + crossSize, Bottom = start + primarySize }
                    : new RECT { Left = start, Top = cross, Right = start + primarySize, Bottom = cross + crossSize };
                Canvas.SetLeft(visual.View, rectangle.Left / dpi); Canvas.SetTop(visual.View, rectangle.Top / dpi);
                visual.View.Width = (rectangle.Right - rectangle.Left) / dpi;
                visual.View.Height = (rectangle.Bottom - rectangle.Top) / dpi;
                _detachedRects[visual.Component] = rectangle;
                if (preference is not null) _appliedComponentPreferences[visual.Component] = preference;
                free = ComponentPlacementPolicy.Subtract(free, [PrimaryRange(rectangle)], (int)Math.Ceiling(4 * dpi));
            }
        }
        finally { _updatingDetachedLayout = false; }
        MediaControl.RefreshComponentSamplingVisibility();
        RefreshDetachedForeground();
        SynchronizeSpectrumTimer();
        SynchronizeMetricsSubscription(MediaControl.EffectivePerformanceSettings);
    }

    private void OnComponentMoveIntent(object? sender, ComponentMoveIntentEventArgs intent)
    {
        if (intent.Cancelled || !CanSaveComponentPlacement || !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var taskbar))
        { ClearComponentGhost(); return; }
        var dpi = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
        if (dpi <= 0) { ClearComponentGhost(); return; }
        var width = Math.Min(taskbar.Right - taskbar.Left, (int)Math.Ceiling(intent.ScreenBounds.Width));
        var height = Math.Min(taskbar.Bottom - taskbar.Top, (int)Math.Ceiling(intent.ScreenBounds.Height));
        var left = Math.Clamp((int)Math.Round(intent.ScreenBounds.Left - taskbar.Left), 0, Math.Max(0, taskbar.Right - taskbar.Left - width));
        var top = Math.Clamp((int)Math.Round(intent.ScreenBounds.Top - taskbar.Top), 0, Math.Max(0, taskbar.Bottom - taskbar.Top - height));
        var preview = new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        if (!intent.Finished)
        {
            if (intent.Dock)
            {
                var available = ComponentPlacementPolicy.Subtract(CurrentComponentRanges(taskbar, dpi, duringGesture: true),
                    _detachedRects.Where(pair => pair.Key != intent.Component).Select(pair => PrimaryRange(pair.Value)), (int)Math.Ceiling(4 * dpi));
                var required = (int)Math.Ceiling(MediaControl.GetMinimumPrimaryLength(dpi) * dpi) +
                    (IsVerticalPresentation ? height : width) + (int)Math.Ceiling(4 * dpi);
                intent.CanDock = available.Any(range => range.Length >= required);
            }
            if (_componentGhost is null)
            {
                _componentGhost = new Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = .8,
                    Fill = new VisualBrush(MediaControl.GetComponentVisual(intent.Component)) { Stretch = Stretch.Fill }
                };
                Panel.SetZIndex(_componentGhost, 100);
                WidgetCanvas.Children.Add(_componentGhost);
            }
            _componentGhostRect = preview;
            _componentGhost.Width = width / dpi; _componentGhost.Height = height / dpi;
            Canvas.SetLeft(_componentGhost, left / dpi); Canvas.SetTop(_componentGhost, top / dpi);
            ApplyBarInputRegion(_windowHandle, _lastInputRegion, false);
            return;
        }
        ClearComponentGhost();
        var ranges = CurrentComponentRanges(taskbar, dpi);
        var others = _detachedRects.Where(pair => pair.Key != intent.Component).Select(pair => PrimaryRange(pair.Value));
        var free = ComponentPlacementPolicy.Subtract(ranges, others, (int)Math.Ceiling(4 * dpi));
        var dock = intent.Dock;
        if (dock)
        {
            // 合并后的本体必须仍有可容纳空间；否则本次保持独立。
            var required = (int)Math.Ceiling(MediaControl.GetMinimumPrimaryLength(dpi) * dpi) +
                (IsVerticalPresentation ? height : width) + (int)Math.Ceiling(4 * dpi);
            dock = free.Any(range => range.Length >= required);
        }
        if (!dock && _renderedPrimaryRect is { } body && HasArea(body))
        {
            var rectangle = IsVerticalPresentation ? new RECT { Left = body.Top, Top = body.Left, Right = body.Bottom, Bottom = body.Right } : body;
            free = ComponentPlacementPolicy.Subtract(free, [PrimaryRange(rectangle)], (int)Math.Ceiling(4 * dpi));
        }
        var requested = IsVerticalPresentation ? top : left;
        var nearest = ComponentPlacementPolicy.Nearest(free, requested, IsVerticalPresentation ? height : width);
        var position = dock ? null : new DetachedComponentPosition(intent.Component, (nearest ?? requested) / dpi, (IsVerticalPresentation ? left : top) / dpi);
        var request = new ComponentPlacementRequest(BarConfigurationTarget.Taskbar, _targetMonitorDeviceId, intent.Component,
            position, intent.State, intent.OriginalEntries, intent.TargetIndex);
        if (_componentPlacementCommand?.CanExecute(request) == true) _componentPlacementCommand.Execute(request);
        UpdatePosition();
    }

    private void ClearComponentGhost()
    {
        if (_componentGhost is not null) WidgetCanvas.Children.Remove(_componentGhost);
        _componentGhost = null; _componentGhostRect = null;
        if (_windowHandle != IntPtr.Zero && !_isClosing && !_isEnvironmentSuspended && !IsTaskbarPresentationSuspended)
            ApplyBarInputRegion(_windowHandle, _lastInputRegion, false);
    }

    private void ReleaseDetachedComponents()
    {
        MediaControl.CancelComponentPlacement(); ClearComponentGhost();
        MediaControl.ComponentMoveIntent -= OnComponentMoveIntent;
        MediaControl.DetachedVisualsChanged -= OnDetachedVisualsChanged;
        MediaControl.ComponentReturnRequested -= ReturnComponent;
        MediaControl.ComponentContextMenuRequested -= ComponentContextMenuRequested;
        if (ActiveComponentMenu is { } menu) menu.IsOpen = false;
        ActiveComponentMenu = null;
        foreach (var view in _detachedViews) WidgetCanvas.Children.Remove(view);
        _detachedViews.Clear(); _detachedRects.Clear();
        MediaControl.ReleaseDetachedVisuals();
        MediaControl.ComponentPlacementAllowed = null;
        foreach (var color in _componentColors.Values) color.Session.Dispose();
        _componentColors.Clear();
    }

    private void ReturnComponent(TaskbarRestComponent component)
    {
        if (!CanSaveComponentPlacement) return;
        var entries = MediaControl.ComponentEntries;
        var request = new ComponentPlacementRequest(BarConfigurationTarget.Taskbar, _targetMonitorDeviceId, component, null,
            MediaControl.ComponentLayoutState, entries, entries.ToList().FindIndex(entry => entry.Component == component));
        if (_componentPlacementCommand?.CanExecute(request) == true) _componentPlacementCommand.Execute(request);
    }

    private void ComponentContextMenuRequested(object? sender, ComponentContextMenuRequestEventArgs request)
    {
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned || IsTaskbarPresentationSuspended) return;
        _componentMenuTarget = request.Component;
        PlayerMenu.ApplyQuickLaunchEntries(SettingsManager.Current.QuickLaunch.Entries ?? []);
        IsPerformanceMenuRequested = SettingsManager.Current.Experiments.MemoryTools &&
            (request.Component == TaskbarRestComponent.Performance || ComponentLayoutPolicy.IsMetric(request.Component));
        IsBatteryMenuRequested = request.Component == TaskbarRestComponent.BluetoothBattery;
        PlayerMenuOpenRequested?.Invoke(this, request.Position);
    }

    internal MenuItem? CreateComponentReturnMenuItem()
    {
        if (_componentMenuTarget is not { } component) return null;
        var item = new MenuItem { IsEnabled = CanSaveComponentPlacement && !TaskbarPresentation.TaskbarBarPositionLocked };
        item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc.ComponentPlacement.Attach");
        item.Click += (_, _) => ReturnComponent(component);
        return item;
    }

    private void RefreshDetachedForeground()
    {
        var visuals = MediaControl.DetachedVisuals.ToDictionary(visual => visual.Component);
        foreach (var obsolete in _componentColors.Keys.Where(key => !visuals.TryGetValue(key, out var visual) ||
            !ReferenceEquals(visual, _componentColors[key].Visual)).ToArray())
        { _componentColors[obsolete].Session.Dispose(); _componentColors.Remove(obsolete); }
        foreach (var visual in visuals.Values)
        {
            if (!_componentColors.TryGetValue(visual.Component, out var color) && _componentSampler is not null)
            {
                color = (visual, new AdaptiveForegroundSamplingSession(_componentSampler, Dispatcher,
                    () => GetComponentSampleRegion(visual.Component), decision => visual.ForegroundChanged?.Invoke(decision)));
                _componentColors.Add(visual.Component, color);
            }
            color.Session?.RequestRefresh();
        }
    }

    private Int32Rect? GetComponentSampleRegion(TaskbarRestComponent component)
    {
        if (_isClosing || _isEnvironmentSuspended || IsBackgroundPruned || IsTaskbarPresentationSuspended || _isDragging ||
            MediaControl.HasComponentGesture || SettingsManager.Current.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic ||
            !_detachedRects.TryGetValue(component, out var rectangle) ||
            !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var taskbar)) return null;
        return IsVerticalPresentation ? new Int32Rect(taskbar.Left + 1, taskbar.Top + rectangle.Top, 1, rectangle.Bottom - rectangle.Top)
            : new Int32Rect(taskbar.Left + rectangle.Left, taskbar.Top + 1, rectangle.Right - rectangle.Left, 1);
    }
}
