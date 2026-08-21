using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Controls;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar;

/// <summary>
/// 协调布局档案选择、组件表面更新和组件动作转发；不持有媒体或音频底层资源。
/// Coordinates profile selection, component-surface updates, and action forwarding without owning media or audio resources.
/// </summary>
public partial class MainWindow
{
    private readonly LayoutRuntimeService _layoutRuntimeService = new();
    private LayoutDocument _layoutDocument = null!;
    private LayoutProfile? _activeLayoutProfile;
    private ComponentLayoutSurface? _componentSurface;
    private readonly List<EdgeSurfaceState> _edgeSurfaces = [];
    // 只记录当前实际展开的边缘容器；折叠时不把展开内容计入窗口尺寸，避免拖动边界被透明区域顶住。
    // Tracks only expanded edge containers so collapsed content cannot enlarge the draggable window bounds.
    private readonly HashSet<string> _expandedEdgeContainerIds = new(StringComparer.Ordinal);
    // 悬停槽位使用独立的真实指针状态，不能复用旧版全局展开状态，否则禁用自动收起时会永久显示靠近内容。
    // Hover slots keep an independent real-pointer state; reusing legacy expansion would pin near content whenever auto-collapse is disabled.
    private bool _isLayoutPointerNear;
    private LayoutEdge? _unavailableLayoutEdge;

    private void InitializeComponentLayout(LayoutDocument document)
    {
        _layoutDocument = document;
        _componentSurface = new ComponentLayoutSurface();
        _componentSurface.CommandRequested += ComponentSurface_OnCommandRequested;
        _componentSurface.MetricsRequested += ComponentSurface_OnMetricsRequested;
        _componentSurface.WheelRequested += ComponentSurface_OnWheelRequested;
        _componentSurface.SourceRequested += ComponentSurface_OnSourceRequested;
        ComponentSurfaceHost.Child = _componentSurface;

        // 旧节点暂时作为弹窗锚点和行为回退保留；将其设为透明可避免第二棵树参与视觉合成。
        // Legacy nodes remain as popup anchors and behavior fallback; transparency prevents a second visible tree from being composited.
        PlayerContent.Opacity = 0;
        VerticalPlayerContent.Opacity = 0;
        PlayerContent.IsHitTestVisible = false;
        VerticalPlayerContent.IsHitTestVisible = false;
        ApplyComponentLayout();
    }

    private void ApplyComponentLayout(bool animateEdgeState = false)
    {
        if (_componentSurface is null || _layoutDocument is null)
        {
            return;
        }

        _activeLayoutProfile = _layoutRuntimeService.ResolveProfile(
            _layoutDocument,
            _isVerticalLayout);
        _unavailableLayoutEdge = _windowSettings.HostMode == WindowHostMode.Taskbar
            ? TaskbarEdgeService.TryResolveCurrent()
            : null;
        _expandedEdgeContainerIds.RemoveWhere(instanceId =>
            !_activeLayoutProfile.EdgeContainers.Any(container =>
                container.Enabled && container.InstanceId == instanceId));
        // 运行时悬停状态由每个容器自己的命中事件决定；重建树时先用离开态，再按当前鼠标位置恢复，避免一次全局 MouseEnter 让所有容器同时靠近。
        // Runtime hover state comes from each container's own hit events; rebuild from leave state and restore from actual mouse position to avoid switching every container at once.
        _componentSurface.Apply(_activeLayoutProfile, pointerNear: false);
        if (_isLayoutPointerNear)
        {
            _componentSurface.RefreshPointerNearFromMouse();
        }
        var stripSize = LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile);
        var compositionSize = LayoutRuntimeService.CalculateCompositionSize(
            _activeLayoutProfile,
            _unavailableLayoutEdge,
            _expandedEdgeContainerIds);
        var edgeInsets = CalculateEdgeInsets(
            _activeLayoutProfile,
            _unavailableLayoutEdge,
            _expandedEdgeContainerIds);
        ComponentCompositionHost.Width = compositionSize.WidthDip;
        ComponentCompositionHost.Height = compositionSize.HeightDip;
        LayoutDragSurface.Width = stripSize.WidthDip;
        LayoutDragSurface.Height = stripSize.HeightDip;
        LayoutDragSurface.Margin = new Thickness(edgeInsets.Left, edgeInsets.Top, 0, 0);
        LayoutEdgeSurfaceHost.Width = compositionSize.WidthDip;
        LayoutEdgeSurfaceHost.Height = compositionSize.HeightDip;
        ComponentSurfaceHost.Width = stripSize.WidthDip;
        ComponentSurfaceHost.Height = stripSize.HeightDip;
        ComponentSurfaceHost.Margin = new Thickness(edgeInsets.Left, edgeInsets.Top, 0, 0);
        ComponentSurfaceHost.CornerRadius = new CornerRadius(
            Math.Clamp(_activeLayoutProfile.Surface.CornerRadiusDip, 0, 32));
        ComponentSurfaceHost.Visibility = Visibility.Visible;
        RebuildEdgeSurfaces(
            _activeLayoutProfile,
            stripSize,
            compositionSize,
            edgeInsets,
            _unavailableLayoutEdge,
            _expandedEdgeContainerIds,
            animateEdgeState);
        RefreshLayoutPointerStateAfterMeasure();
        _metricSettings = LayoutRuntimeService.ResolveComponentSettings(
            _activeLayoutProfile,
            _settingsCoordinator.Current.Metrics);
        ApplyComponentMetricRefreshInterval();
    }

    private void RebuildEdgeSurfaces(
        LayoutProfile profile,
        LayoutSize stripSize,
        LayoutSize compositionSize,
        Thickness edgeInsets,
        LayoutEdge? unavailableEdge,
        IReadOnlySet<string> expandedEdgeContainerIds,
        bool animateEdgeState)
    {
        DisposeEdgeSurfaces();
        foreach (var model in profile.EdgeContainers.Where(model =>
                     model.Enabled &&
                     model.Edge != unavailableEdge))
        {
            var expandedSize = LayoutRuntimeService.MeasureEdgeContainer(profile, model);
            var collapsedSize = LayoutRuntimeService.MeasureEdgeTrigger(profile, model);
            if (collapsedSize.WidthDip <= 0 || collapsedSize.HeightDip <= 0)
            {
                continue;
            }

            var surface = new ComponentLayoutSurface();
            surface.CommandRequested += ComponentSurface_OnCommandRequested;
            surface.MetricsRequested += ComponentSurface_OnMetricsRequested;
            surface.WheelRequested += ComponentSurface_OnWheelRequested;
            surface.SourceRequested += ComponentSurface_OnSourceRequested;
            surface.ApplyEdge(profile, model);
            surface.RefreshPointerNearFromMouse();
            var host = new Border
            {
                Background = TryFindResource("TaskbarReadabilityBrush") as Brush ?? Brushes.Transparent,
                BorderBrush = TryFindResource("TaskbarHoverBrush") as Brush ?? Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Clamp(profile.Surface.CornerRadiusDip, 0, 32)),
                ClipToBounds = true,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = surface
            };
            Panel.SetZIndex(host, 30);
            var state = CreateEdgeSurfaceState(
                model,
                host,
                surface,
                expandedSize,
                collapsedSize,
                stripSize,
                compositionSize,
                edgeInsets);
            host.Tag = state;
            host.MouseEnter += EdgeSurfaceHost_OnMouseEnter;
            host.MouseLeave += EdgeSurfaceHost_OnMouseLeave;
            _edgeSurfaces.Add(state);
            LayoutEdgeSurfaceHost.Children.Add(host);
            ApplyEdgeSurfaceState(
                state,
                expandedEdgeContainerIds.Contains(model.InstanceId),
                animateEdgeState);
        }
    }

    private void RefreshLayoutPointerStateAfterMeasure()
    {
        if (!_isLayoutPointerNear)
        {
            return;
        }

        // WPF 在重建视觉树后要到下一轮布局才会更新 ActualWidth/ActualHeight；延迟一次命中刷新，避免静止鼠标停在离开槽。
        // WPF updates ActualWidth/ActualHeight on the next layout pass; refresh hit state once after that pass so a stationary pointer cannot remain in the leave slot.
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed || !_isLayoutPointerNear)
            {
                return;
            }

            _componentSurface?.RefreshPointerNearFromMouse();
            foreach (var state in _edgeSurfaces)
            {
                state.Surface.RefreshPointerNearFromMouse();
            }
        });
    }

    private static EdgeSurfaceState CreateEdgeSurfaceState(
        LayoutEdgeContainer model,
        Border host,
        ComponentLayoutSurface surface,
        LayoutSize expandedSize,
        LayoutSize collapsedSize,
        LayoutSize stripSize,
        LayoutSize compositionSize,
        Thickness edgeInsets)
    {
        var expandedLeft = model.Edge switch
        {
            LayoutEdge.Left => edgeInsets.Left - expandedSize.WidthDip,
            LayoutEdge.Right => edgeInsets.Left + stripSize.WidthDip,
            _ => edgeInsets.Left + (stripSize.WidthDip - expandedSize.WidthDip) / 2 + model.OffsetDip
        };
        var expandedTop = model.Edge switch
        {
            LayoutEdge.Top => edgeInsets.Top - expandedSize.HeightDip,
            LayoutEdge.Bottom => edgeInsets.Top + stripSize.HeightDip,
            _ => edgeInsets.Top + (stripSize.HeightDip - expandedSize.HeightDip) / 2 + model.OffsetDip
        };
        expandedLeft = Math.Clamp(expandedLeft, 0, Math.Max(0, compositionSize.WidthDip - expandedSize.WidthDip));
        expandedTop = Math.Clamp(expandedTop, 0, Math.Max(0, compositionSize.HeightDip - expandedSize.HeightDip));

        var collapsedWidth = collapsedSize.WidthDip;
        var collapsedHeight = collapsedSize.HeightDip;
        var collapsedLeft = model.Edge switch
        {
            // 留出少量可见触发像素；其余触发区允许落在工作区外，不再阻挡长条拖动。
            // Keep a small visible activation strip; the remaining trigger may extend outside the work area and no longer blocks strip dragging.
            LayoutEdge.Left => edgeInsets.Left - collapsedWidth + CollapsedTriggerVisibleDip,
            LayoutEdge.Right => edgeInsets.Left + stripSize.WidthDip - CollapsedTriggerVisibleDip,
            _ => edgeInsets.Left + (stripSize.WidthDip - collapsedWidth) / 2 + model.OffsetDip
        };
        var collapsedTop = model.Edge switch
        {
            LayoutEdge.Top => edgeInsets.Top - collapsedHeight + CollapsedTriggerVisibleDip,
            LayoutEdge.Bottom => edgeInsets.Top + stripSize.HeightDip - CollapsedTriggerVisibleDip,
            _ => edgeInsets.Top + (stripSize.HeightDip - collapsedHeight) / 2 + model.OffsetDip
        };
        return new EdgeSurfaceState(
            model,
            host,
            surface,
            new Rect(expandedLeft, expandedTop, expandedSize.WidthDip, expandedSize.HeightDip),
            new Rect(collapsedLeft, collapsedTop, collapsedWidth, collapsedHeight));
    }

    private static Thickness CalculateEdgeInsets(
        LayoutProfile profile,
        LayoutEdge? unavailableEdge,
        IReadOnlySet<string> expandedEdgeContainerIds)
    {
        var sizes = profile.EdgeContainers
            .Where(container => container.Enabled &&
                container.Edge != unavailableEdge)
            .Select(container =>
            {
                var size = expandedEdgeContainerIds.Contains(container.InstanceId)
                    ? LayoutRuntimeService.MeasureEdgeContainer(profile, container)
                    : LayoutRuntimeService.MeasureEdgeTrigger(profile, container);
                return (container.Edge, Size: size);
            })
            .ToArray();
        return new Thickness(
            sizes.Where(item => item.Edge == LayoutEdge.Left).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Top).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Right).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Bottom).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max());
    }

    /// <summary>
    /// 仅返回折叠触发条的外侧尺寸；拖动与任务栏定位会允许这部分落在工作区外，避免触发条把长条本体顶离屏幕边缘。
    /// Returns only collapsed trigger insets; placement lets these pixels extend outside the work area so triggers cannot push the strip body away from an edge.
    /// </summary>
    private static Thickness CalculateCollapsedEdgeInsets(
        LayoutProfile profile,
        LayoutEdge? unavailableEdge,
        IReadOnlySet<string> expandedEdgeContainerIds)
    {
        var sizes = profile.EdgeContainers
            .Where(container => container.Enabled &&
                container.Edge != unavailableEdge &&
                !expandedEdgeContainerIds.Contains(container.InstanceId))
            .Select(container =>
            {
                var size = LayoutRuntimeService.MeasureEdgeTrigger(profile, container);
                return (container.Edge, Size: size);
            })
            .ToArray();
        return new Thickness(
            sizes.Where(item => item.Edge == LayoutEdge.Left).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Top).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Right).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Bottom).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max());
    }

    private Thickness ResolveCollapsedActiveEdgeInsets()
    {
        return _activeLayoutProfile is null
            ? new Thickness(0)
            : CalculateCollapsedEdgeInsets(
                _activeLayoutProfile,
                _unavailableLayoutEdge,
                _expandedEdgeContainerIds);
    }

    /// <summary>
    /// 将布局中的真实可见区域换算为宿主客户区像素，供 Win32 输入区域裁剪使用；折叠内容不在列表中，因此不会形成透明碰撞。
    /// Converts visible layout regions to host-client pixels for Win32 input clipping; collapsed content is omitted and cannot remain a transparent collision area.
    /// </summary>
    private IReadOnlyList<NativeMethods.Rect>? BuildWindowInputRects(double scale)
    {
        if (_activeLayoutProfile is null)
        {
            return null;
        }

        var edgeInsets = CalculateEdgeInsets(
            _activeLayoutProfile,
            _unavailableLayoutEdge,
            _expandedEdgeContainerIds);
        var stripSize = LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile);
        var regions = new List<NativeMethods.Rect>
        {
            ToNativeRect(
                edgeInsets.Left,
                edgeInsets.Top,
                stripSize.WidthDip,
                stripSize.HeightDip,
                scale)
        };
        foreach (var state in _edgeSurfaces)
        {
            var bounds = _expandedEdgeContainerIds.Contains(state.Model.InstanceId)
                ? state.ExpandedBounds
                : state.CollapsedBounds;
            regions.Add(ToNativeRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height, scale));
        }

        return regions;
    }

    private static NativeMethods.Rect ToNativeRect(
        double left,
        double top,
        double width,
        double height,
        double scale)
    {
        var pixelLeft = (int)Math.Round(left * scale);
        var pixelTop = (int)Math.Round(top * scale);
        var pixelRight = Math.Max(pixelLeft + 1, (int)Math.Round((left + width) * scale));
        var pixelBottom = Math.Max(pixelTop + 1, (int)Math.Round((top + height) * scale));
        return new NativeMethods.Rect
        {
            Left = pixelLeft,
            Top = pixelTop,
            Right = pixelRight,
            Bottom = pixelBottom
        };
    }

    private void EdgeSurfaceHost_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border { Tag: EdgeSurfaceState state })
        {
            if (_expandedEdgeContainerIds.Add(state.Model.InstanceId))
            {
                ApplyComponentLayout(animateEdgeState: true);
                ApplyResponsivePlayerDimensions();
                PositionOverTaskbar(force: true);
            }
        }
    }

    private void EdgeSurfaceHost_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border { Tag: EdgeSurfaceState state } &&
            !state.Host.IsMouseOver &&
            !IsEdgeSurfacePointerNear(state, state.ExpandedBounds))
        {
            if (_expandedEdgeContainerIds.Remove(state.Model.InstanceId))
            {
                ApplyComponentLayout(animateEdgeState: true);
                ApplyResponsivePlayerDimensions();
                PositionOverTaskbar(force: true);
            }
        }
    }

    /// <summary>
    /// 边缘容器的 ProximityDip 在触发条外形成预展开区域；状态只在跨越阈值时重建，避免鼠标移动热路径反复测量窗口。
    /// ProximityDip creates a pre-expand area around an edge trigger; rebuild only on threshold crossings so mouse movement never causes repeated layout measurement.
    /// </summary>
    private void ComponentCompositionHost_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isClosed || _isDragging || _edgeSurfaces.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var state in _edgeSurfaces)
        {
            var id = state.Model.InstanceId;
            var isExpanded = _expandedEdgeContainerIds.Contains(id);
            var targetBounds = isExpanded ? state.ExpandedBounds : state.CollapsedBounds;
            var near = IsEdgeSurfacePointerNear(state, targetBounds);
            if (isExpanded)
            {
                if (!near && !state.Host.IsMouseOver)
                {
                    changed |= _expandedEdgeContainerIds.Remove(id);
                }
            }
            else if (near)
            {
                changed |= _expandedEdgeContainerIds.Add(id);
            }
        }

        if (!changed)
        {
            return;
        }

        ApplyComponentLayout(animateEdgeState: true);
        ApplyResponsivePlayerDimensions();
        PositionOverTaskbar(force: true);
    }

    private bool IsEdgeSurfacePointerNear(EdgeSurfaceState state, Rect bounds)
    {
        var proximity = Math.Clamp(state.Model.ProximityDip, 0, 256);
        var point = Mouse.GetPosition(LayoutEdgeSurfaceHost);
        return point.X >= bounds.Left - proximity &&
            point.X <= bounds.Right + proximity &&
            point.Y >= bounds.Top - proximity &&
            point.Y <= bounds.Bottom + proximity;
    }

    private static void ApplyEdgeSurfaceState(EdgeSurfaceState state, bool expanded, bool animate)
    {
        var rect = expanded ? state.ExpandedBounds : state.CollapsedBounds;
        Canvas.SetLeft(state.Host, rect.Left);
        Canvas.SetTop(state.Host, rect.Top);
        state.Host.Width = rect.Width;
        state.Host.Height = rect.Height;
        state.Surface.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        // 折叠状态移除展开内容子树，避免不可见组件仍参与命中测试或挡住长条拖动。
        // Remove the expanded subtree while collapsed so invisible widgets cannot retain hit testing or block strip dragging.
        state.Host.Child = expanded ? state.Surface : null;
        state.Host.Background = expanded
            ? state.ExpandedBackground
            : state.TriggerBackground;
        state.Host.BeginAnimation(UIElement.OpacityProperty, null);
        state.Host.Opacity = 1;
        if (!animate || !state.Model.Animation.Enabled || state.Model.Animation.DurationMilliseconds <= 0)
        {
            return;
        }

        var easing = state.Model.Animation.Easing switch
        {
            LayoutEasingKind.Linear => null,
            LayoutEasingKind.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        state.Host.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = 0.35,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(
                    Math.Clamp(state.Model.Animation.DurationMilliseconds, 0, 2_000)),
                BeginTime = TimeSpan.FromMilliseconds(
                    Math.Clamp(state.Model.Animation.DelayMilliseconds, 0, 2_000)),
                EasingFunction = easing
            });
    }

    private void DisposeEdgeSurfaces()
    {
        foreach (var state in _edgeSurfaces)
        {
            state.Host.MouseEnter -= EdgeSurfaceHost_OnMouseEnter;
            state.Host.MouseLeave -= EdgeSurfaceHost_OnMouseLeave;
            state.Surface.CommandRequested -= ComponentSurface_OnCommandRequested;
            state.Surface.MetricsRequested -= ComponentSurface_OnMetricsRequested;
            state.Surface.WheelRequested -= ComponentSurface_OnWheelRequested;
            state.Surface.SourceRequested -= ComponentSurface_OnSourceRequested;
            state.Surface.Dispose();
        }
        _edgeSurfaces.Clear();
        LayoutEdgeSurfaceHost.Children.Clear();
    }

    private void ComponentSurface_OnCommandRequested(
        object? sender,
        LayoutCommandEventArgs e)
    {
        switch (e.Command)
        {
            case MediaCommandKind.Previous:
                _ = RunMediaCommandAsync(_mediaSessionService.SkipPreviousAsync);
                break;
            case MediaCommandKind.PlayPause:
                _ = RunMediaCommandAsync(_mediaSessionService.TogglePlayPauseAsync);
                break;
            case MediaCommandKind.Next:
                _ = RunMediaCommandAsync(_mediaSessionService.SkipNextAsync);
                break;
            case MediaCommandKind.SelectSource:
                ShowSelectedMediaSource();
                break;
            case MediaCommandKind.AdjustVolume:
                if (e.PlacementTarget is not null)
                {
                    VolumeControlPopup.PlacementTarget = e.PlacementTarget;
                    VolumeStatusPopup.PlacementTarget = e.PlacementTarget;
                }
                VolumeControlButton_OnClick(
                    e.PlacementTarget ?? VolumeControlButton,
                    new RoutedEventArgs());
                break;
            case MediaCommandKind.SelectOutputDevice:
                if (e.PlacementTarget is not null)
                {
                    OutputDevicePopup.PlacementTarget = e.PlacementTarget;
                    OutputDeviceStatusPopup.PlacementTarget = e.PlacementTarget;
                }
                OutputDeviceButton_OnClick(
                    e.PlacementTarget ?? OutputDeviceButton,
                    new RoutedEventArgs());
                break;
        }
    }

    private void ComponentSurface_OnMetricsRequested(
        object? sender,
        LayoutMetricsEventArgs e)
    {
        if (e.OpenTaskManager)
        {
            OpenTaskManager();
        }
    }

    private void ComponentSurface_OnWheelRequested(
        object? sender,
        LayoutWheelEventArgs e)
    {
        if (e.Command == MediaCommandKind.SelectOutputDevice)
        {
            OutputDevicePopup.PlacementTarget = e.PlacementTarget;
            OutputDeviceStatusPopup.PlacementTarget = e.PlacementTarget;
            QueueOutputDeviceFromWheel(e.Delta, useCompactStatus: true);
            return;
        }

        if (e.Command == MediaCommandKind.AdjustVolume)
        {
            VolumeControlPopup.PlacementTarget = e.PlacementTarget;
            VolumeStatusPopup.PlacementTarget = e.PlacementTarget;
            QueueVolumeWheel(e.Delta, useCompactStatus: true);
        }
    }

    private void ComponentSurface_OnSourceRequested(object? sender, EventArgs e)
    {
        ShowSelectedMediaSource();
    }

    private void ComponentSurface_OnLayoutPointerNearChanged(bool pointerNear)
    {
        _isLayoutPointerNear = pointerNear;
        _componentSurface?.RefreshPointerNearFromMouse();
    }

    private void ComponentSurface_OnSnapshotChanged(MediaSnapshot snapshot)
    {
        _componentSurface?.SetMediaSnapshot(snapshot);
        foreach (var state in _edgeSurfaces)
        {
            state.Surface.SetMediaSnapshot(snapshot);
        }
    }

    private void ComponentSurface_OnMetricsChanged(string text)
    {
        _componentSurface?.SetMetricsText(text);
        foreach (var state in _edgeSurfaces)
        {
            state.Surface.SetMetricsText(text);
        }
    }

    private void ComponentSurface_OnMetricsSnapshotChanged(SystemMetricsSnapshot snapshot)
    {
        _componentSurface?.SetMetricsSnapshot(snapshot);
        foreach (var state in _edgeSurfaces)
        {
            state.Surface.SetMetricsSnapshot(snapshot);
        }
    }

    private void ComponentSurface_OnSpectrumChanged(IReadOnlyList<float> values)
    {
        _componentSurface?.SetSpectrum(values);
        foreach (var state in _edgeSurfaces)
        {
            state.Surface.SetSpectrum(values);
        }
    }

    private void ComponentSurface_OnLayoutSettingsChanged(LayoutDocument document)
    {
        var previousMetricSettings = _metricSettings;
        _layoutDocument = document;
        ApplyComponentLayout();
        if (_metricSettings != previousMetricSettings)
        {
            ApplyMetricSettings();
        }
        ApplyResponsivePlayerDimensions();
        PositionOverTaskbar(force: true);
    }

    private void ApplyComponentMetricRefreshInterval()
    {
        // 构造早期组件表面先于指标定时器创建；空值保护只覆盖这一短暂初始化阶段。
        // The component surface is created before the metrics timer; this guard covers only that brief initialization stage.
        if (_metricsTimer is null)
        {
            return;
        }

        _metricsTimer.Interval = TimeSpan.FromMilliseconds(
            LayoutRuntimeService.ResolveMetricRefreshInterval(
                _activeLayoutProfile,
                fallbackMilliseconds: 2_500));
    }

    private sealed class EdgeSurfaceState(
        LayoutEdgeContainer model,
        Border host,
        ComponentLayoutSurface surface,
        Rect expandedBounds,
        Rect collapsedBounds)
    {
        internal LayoutEdgeContainer Model { get; } = model;
        internal Border Host { get; } = host;
        internal ComponentLayoutSurface Surface { get; } = surface;
        internal Rect ExpandedBounds { get; } = expandedBounds;
        internal Rect CollapsedBounds { get; } = collapsedBounds;
        internal Brush ExpandedBackground { get; } = host.Background;
        internal Brush TriggerBackground { get; } = host.BorderBrush;
    }

}
