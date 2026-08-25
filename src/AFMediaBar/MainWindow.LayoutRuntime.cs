using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    // 只记录当前实际展开的折叠容器；折叠时不把展开内容计入窗口尺寸，避免拖动边界被透明区域顶住。
    // Tracks only currently expanded collapse containers so collapsed content cannot enlarge the draggable window bounds.
    private readonly HashSet<string> _expandedCollapseContainerIds = new(StringComparer.Ordinal);
    private DispatcherTimer? _layoutEdgePointerTimer;
    private Point? _layoutBodyAnchorScreen;
    private int _layoutBodyCorrectionX;
    private int _layoutBodyCorrectionY;
    private SystemMetricsSnapshot? _lastComponentMetricsSnapshot;
    // 悬停槽位使用独立的真实指针状态，不能复用旧版全局展开状态，否则禁用自动收起时会永久显示靠近内容。
    // Hover slots keep an independent real-pointer state; reusing legacy expansion would pin near content whenever auto-collapse is disabled.
    private bool _isLayoutPointerNear;
    private LayoutEdge? _unavailableLayoutEdge;

    private void InitializeComponentLayout(LayoutDocument document)
    {
        _layoutDocument = document;
        _layoutEdgePointerTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Input,
            OnLayoutEdgePointerTimerTick,
            Dispatcher);
        _layoutEdgePointerTimer.Stop();
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
        _expandedCollapseContainerIds.RemoveWhere(instanceId =>
            !_activeLayoutProfile.CollapseContainers.Any(container =>
                container.Enabled && container.InstanceId == instanceId));
        // 运行时悬停状态由每个容器自己的命中事件决定；重建树时先用离开态，再按当前鼠标位置恢复，避免一次全局 MouseEnter 让所有容器同时靠近。
        // Runtime hover state comes from each container's own hit events; rebuild from leave state and restore from actual mouse position to avoid switching every container at once.
        _componentSurface.Apply(_activeLayoutProfile, pointerNear: false);
        if (_isLayoutPointerNear)
        {
            _componentSurface.RefreshPointerNearFromMouse();
        }

        var grid = LayoutGridSettings.Normalize(_activeLayoutProfile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var bodyGrid = LayoutRuntimeService.CalculateBodyGridBounds(_activeLayoutProfile)
            ?? LayoutGridRect.Unit(0, 0);
        var compositionGrid = LayoutRuntimeService.CalculateCompositionGridBounds(
            _activeLayoutProfile,
            _expandedCollapseContainerIds) ?? bodyGrid;
        var stripSize = LayoutRuntimeService.GridRectToDip(bodyGrid, cell);
        var compositionSize = LayoutRuntimeService.GridRectToDip(compositionGrid, cell);
        // body 相对组合原点的偏移；组合原点以占用联合矩形左上角为准，不含编辑画布前导空白。
        // Body offset within the composition; the composition origin is the occupied union's top-left corner.
        var bodyOffset = new Thickness(
            (bodyGrid.X - compositionGrid.X) * cell,
            (bodyGrid.Y - compositionGrid.Y) * cell,
            0,
            0);
        ComponentCompositionHost.Width = compositionSize.WidthDip;
        ComponentCompositionHost.Height = compositionSize.HeightDip;
        LayoutDragSurface.Width = stripSize.WidthDip;
        LayoutDragSurface.Height = stripSize.HeightDip;
        LayoutDragSurface.Margin = bodyOffset;
        LayoutEdgeSurfaceHost.Width = compositionSize.WidthDip;
        LayoutEdgeSurfaceHost.Height = compositionSize.HeightDip;
        ComponentSurfaceHost.Width = stripSize.WidthDip;
        ComponentSurfaceHost.Height = stripSize.HeightDip;
        ComponentSurfaceHost.Margin = bodyOffset;
        ComponentSurfaceHost.CornerRadius = new CornerRadius(
            Math.Clamp(_activeLayoutProfile.Surface.CornerRadiusDip, 0, 32));
        ComponentSurfaceHost.Visibility = Visibility.Visible;
        RebuildCollapseSurfaces(
            _activeLayoutProfile,
            compositionGrid,
            bodyGrid,
            cell,
            animateEdgeState);
        UpdateLayoutEdgePointerTimer();
        RefreshLayoutPointerStateAfterMeasure();
        _metricSettings = LayoutRuntimeService.ResolveComponentSettings(
            _activeLayoutProfile,
            _settingsCoordinator.Current.Metrics);
        ApplyComponentMetricRefreshInterval();
    }

    private void RebuildCollapseSurfaces(
        LayoutProfile profile,
        LayoutGridRect compositionGrid,
        LayoutGridRect bodyGrid,
        int cell,
        bool animateEdgeState)
    {
        DisposeEdgeSurfaces();
        foreach (var model in profile.CollapseContainers.Where(model =>
                     model.Enabled &&
                     model.Attachment.AttachmentSide != _unavailableLayoutEdge))
        {
            if (!LayoutGridConstraintService.ResolveAttachment(model, profile).Valid)
            {
                continue;
            }

            var surface = new ComponentLayoutSurface();
            surface.CommandRequested += ComponentSurface_OnCommandRequested;
            surface.MetricsRequested += ComponentSurface_OnMetricsRequested;
            surface.WheelRequested += ComponentSurface_OnWheelRequested;
            surface.SourceRequested += ComponentSurface_OnSourceRequested;
            surface.ApplyEdge(profile, model);
            surface.SetMediaSnapshot(_lastSnapshot ?? MediaSnapshot.Disconnected);
            surface.SetMetricsText(MetricsText.Text);
            if (_lastComponentMetricsSnapshot is { } metricsSnapshot)
            {
                surface.SetMetricsSnapshot(metricsSnapshot);
            }
            surface.SetSpectrum(_audioSpectrum);
            surface.RefreshPointerNearFromMouse();
            var host = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Clamp(profile.Surface.CornerRadiusDip, 0, 32)),
                ClipToBounds = true,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = surface
            };
            host.SetResourceReference(Border.BackgroundProperty, "TaskbarReadabilityBrush");
            host.SetResourceReference(Border.BorderBrushProperty, "TaskbarHoverBrush");
            Panel.SetZIndex(host, 30);
            var state = CreateEdgeSurfaceState(
                model,
                host,
                surface,
                compositionGrid,
                profile,
                cell);
            host.Tag = state;
            host.MouseEnter += EdgeSurfaceHost_OnMouseEnter;
            host.MouseLeave += EdgeSurfaceHost_OnMouseLeave;
            _edgeSurfaces.Add(state);
            LayoutEdgeSurfaceHost.Children.Add(host);
            ApplyEdgeSurfaceState(
                state,
                _expandedCollapseContainerIds.Contains(model.InstanceId),
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
        LayoutCollapseContainer model,
        Border host,
        ComponentLayoutSurface surface,
        LayoutGridRect compositionGrid,
        LayoutProfile profile,
        int cell)
    {
        // 展开矩形来自持久化网格位置；折叠矩形位于公共边，只保留触发厚度，长度限制在公共边交集内。
        // The expanded rect comes from persisted grid bounds; the collapsed rect keeps the trigger thickness on the shared edge.
        var expandedBounds = GridRectToDipRect(model.GridBounds, compositionGrid, cell);
        var triggerGrid = LayoutRuntimeService.CalculateCollapseTriggerBounds(model, profile);
        var collapsedBounds = GridRectToDipRect(triggerGrid, compositionGrid, cell);
        return new EdgeSurfaceState(
            model,
            host,
            surface,
            expandedBounds,
            collapsedBounds,
            collapsedBounds);
    }

    private static Rect GridRectToDipRect(
        LayoutGridRect rect,
        LayoutGridRect origin,
        int cell) =>
        new(
            (rect.X - origin.X) * cell,
            (rect.Y - origin.Y) * cell,
            Math.Max(0, rect.Width * cell),
            Math.Max(0, rect.Height * cell));

    /// <summary>
    /// 计算所有启用折叠容器相对 body 的外侧占用，用于窗口定位与输入矩形。
    /// Computes the outer footprint that enabled collapse containers extend beyond the body.
    /// </summary>
    private static Thickness CalculateEdgeInsets(
        LayoutProfile profile,
        IReadOnlySet<string> expandedCollapseContainerIds)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var body = LayoutRuntimeService.CalculateBodyGridBounds(profile);
        if (body is null)
        {
            return new Thickness(0);
        }

        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;
        foreach (var collapse in profile.CollapseContainers.Where(item => item.Enabled))
        {
            var footprint = expandedCollapseContainerIds.Contains(collapse.InstanceId)
                ? collapse.GridBounds
                : LayoutRuntimeService.CalculateCollapseTriggerBounds(collapse, profile);
            left = Math.Max(left, Math.Max(0, body.X - footprint.X) * cell);
            top = Math.Max(top, Math.Max(0, body.Y - footprint.Y) * cell);
            right = Math.Max(right, Math.Max(0, footprint.Right - body.Right) * cell);
            bottom = Math.Max(bottom, Math.Max(0, footprint.Bottom - body.Bottom) * cell);
        }

        return new Thickness(left, top, right, bottom);
    }

    /// <summary>
    /// 仅返回折叠状态折叠容器的外侧尺寸；拖动与任务栏定位会允许这部分落在工作区外，避免触发条把长条本体顶离屏幕边缘。
    /// Returns only collapsed-trigger insets; placement lets these pixels extend outside the work area so triggers cannot push the strip body away from an edge.
    /// </summary>
    private static Thickness CalculateCollapsedEdgeInsets(
        LayoutProfile profile,
        IReadOnlySet<string> expandedCollapseContainerIds)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var body = LayoutRuntimeService.CalculateBodyGridBounds(profile);
        if (body is null)
        {
            return new Thickness(0);
        }

        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;
        foreach (var collapse in profile.CollapseContainers.Where(item =>
                     item.Enabled &&
                     !expandedCollapseContainerIds.Contains(item.InstanceId)))
        {
            var footprint = LayoutRuntimeService.CalculateCollapseTriggerBounds(collapse, profile);
            left = Math.Max(left, Math.Max(0, body.X - footprint.X) * cell);
            top = Math.Max(top, Math.Max(0, body.Y - footprint.Y) * cell);
            right = Math.Max(right, Math.Max(0, footprint.Right - body.Right) * cell);
            bottom = Math.Max(bottom, Math.Max(0, footprint.Bottom - body.Bottom) * cell);
        }

        return new Thickness(left, top, right, bottom);
    }

    private Thickness ResolveCollapsedActiveEdgeInsets()
    {
        return _activeLayoutProfile is null
            ? new Thickness(0)
            : CalculateCollapsedEdgeInsets(
                _activeLayoutProfile,
                _expandedCollapseContainerIds);
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

        var grid = LayoutGridSettings.Normalize(_activeLayoutProfile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var bodyGrid = LayoutRuntimeService.CalculateBodyGridBounds(_activeLayoutProfile)
            ?? LayoutGridRect.Unit(0, 0);
        var compositionGrid = LayoutRuntimeService.CalculateCompositionGridBounds(
            _activeLayoutProfile,
            _expandedCollapseContainerIds) ?? bodyGrid;
        var bodyBounds = GridRectToDipRect(bodyGrid, compositionGrid, cell);
        var regions = new List<NativeMethods.Rect>
        {
            ToNativeRect(
                bodyBounds.Left,
                bodyBounds.Top,
                bodyBounds.Width,
                bodyBounds.Height,
                scale)
        };
        foreach (var state in _edgeSurfaces)
        {
            var bounds = _expandedCollapseContainerIds.Contains(state.Model.InstanceId)
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
            SetEdgeSurfaceExpanded(state, expanded: true);
        }
    }

    private void EdgeSurfaceHost_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border { Tag: EdgeSurfaceState state } &&
            !state.Host.IsMouseOver &&
            !IsEdgeSurfacePointerInside(state) &&
            !IsEdgeSurfacePointerNear(state, state.CollapsedBounds))
        {
            SetEdgeSurfaceExpanded(state, expanded: false);
        }
    }

    /// <summary>
    /// 折叠容器的 ProximityDip 在触发条外形成预展开区域；状态只在跨越阈值时重建，避免鼠标移动热路径反复测量窗口。
    /// ProximityDip creates a pre-expand area around a collapse trigger; rebuild only on threshold crossings so mouse movement never causes repeated layout measurement.
    /// </summary>
    private void ComponentCompositionHost_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isClosed || _isDragging || _edgeSurfaces.Count == 0)
        {
            return;
        }

        foreach (var state in _edgeSurfaces.ToArray())
        {
            var id = state.Model.InstanceId;
            var isExpanded = _expandedCollapseContainerIds.Contains(id);
            var nearTrigger = IsEdgeSurfacePointerNear(state, state.CollapsedBounds);
            var insideExpanded = IsEdgeSurfacePointerInside(state);
            if (isExpanded)
            {
                if (!insideExpanded && !nearTrigger && !state.Host.IsMouseOver)
                {
                    SetEdgeSurfaceExpanded(state, expanded: false);
                    return;
                }
            }
            else if (nearTrigger)
            {
                SetEdgeSurfaceExpanded(state, expanded: true);
                return;
            }
        }
    }

    private void OnLayoutEdgePointerTimerTick(object? sender, EventArgs e)
    {
        if (_isClosed || _isDragging || !IsVisible || _edgeSurfaces.Count == 0)
        {
            UpdateLayoutEdgePointerTimer();
            return;
        }

        foreach (var state in _edgeSurfaces.ToArray())
        {
            var isExpanded = _expandedCollapseContainerIds.Contains(state.Model.InstanceId);
            var nearTrigger = IsEdgeSurfacePointerNear(state, state.CollapsedBounds);
            var insideExpanded = IsEdgeSurfacePointerInside(state);
            if (isExpanded && state.TargetExpanded)
            {
                if (!insideExpanded && !nearTrigger && !state.Host.IsMouseOver)
                {
                    SetEdgeSurfaceExpanded(state, expanded: false);
                    return;
                }
            }
            else if (isExpanded)
            {
                if (nearTrigger || insideExpanded || state.Host.IsMouseOver)
                {
                    SetEdgeSurfaceExpanded(state, expanded: true);
                    return;
                }
            }
            else if (nearTrigger)
            {
                SetEdgeSurfaceExpanded(state, expanded: true);
                return;
            }
        }

        UpdateLayoutEdgePointerTimer();
    }

    private void UpdateLayoutEdgePointerTimer()
    {
        if (_layoutEdgePointerTimer is null)
        {
            return;
        }

        if (_isClosed || _edgeSurfaces.Count == 0)
        {
            _layoutEdgePointerTimer.Stop();
        }
        else if (!_layoutEdgePointerTimer.IsEnabled)
        {
            _layoutEdgePointerTimer.Start();
        }
    }

    private void SetEdgeSurfaceExpanded(EdgeSurfaceState state, bool expanded)
    {
        var instanceId = state.Model.InstanceId;
        if (expanded)
        {
            if (_expandedCollapseContainerIds.Contains(instanceId) && state.TargetExpanded)
            {
                UpdateLayoutEdgePointerTimer();
                return;
            }

            CaptureLayoutBodyAnchor();
            if (_expandedCollapseContainerIds.Add(instanceId))
            {
                ApplyComponentLayout(animateEdgeState: true);
                RepositionPreservingLayoutBody();
            }
            else if (!state.TargetExpanded)
            {
                BeginEdgeSurfaceTransition(state, expanded: true);
            }
            UpdateLayoutEdgePointerTimer();
            return;
        }

        if (!_expandedCollapseContainerIds.Contains(instanceId) || !state.TargetExpanded)
        {
            return;
        }

        CaptureLayoutBodyAnchor();

        if (!state.Model.Animation.Enabled || state.Model.Animation.DurationMilliseconds <= 0)
        {
            CompleteEdgeSurfaceCollapse(state);
            return;
        }

        BeginEdgeSurfaceTransition(state, expanded: false);
    }

    private bool IsEdgeSurfacePointerNear(EdgeSurfaceState state, Rect bounds)
    {
        if (!TryGetLayoutPointerPosition(out var point))
        {
            return false;
        }

        var proximity = Math.Clamp(state.Model.ProximityDip, 0, 256);
        if (bounds.Contains(point))
        {
            return true;
        }

        var distanceX = point.X - (bounds.Left + bounds.Width / 2);
        var distanceY = point.Y - (bounds.Top + bounds.Height / 2);
        return distanceX * distanceX + distanceY * distanceY <= proximity * proximity;
    }

    private bool IsEdgeSurfacePointerInside(EdgeSurfaceState state)
    {
        if (!TryGetLayoutPointerPosition(out var point))
        {
            return false;
        }

        return point.X >= state.ExpandedBounds.Left &&
            point.X <= state.ExpandedBounds.Right &&
            point.Y >= state.ExpandedBounds.Top &&
            point.Y <= state.ExpandedBounds.Bottom;
    }

    /// <summary>
    /// 使用真实屏幕坐标读取窗口外的指针；WPF Mouse.GetPosition 在 HWND 输入区域之外可能停留在最后一次窗口内位置。
    /// Reads the real screen cursor outside the HWND; WPF Mouse.GetPosition can retain the last in-window position beyond the native input region.
    /// </summary>
    private bool TryGetLayoutPointerPosition(out Point point)
    {
        point = default;
        if (!NativeMethods.GetCursorPos(out var cursor) ||
            PresentationSource.FromVisual(LayoutEdgeSurfaceHost) is null)
        {
            return false;
        }

        try
        {
            point = LayoutEdgeSurfaceHost.PointFromScreen(new Point(cursor.X, cursor.Y));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyEdgeSurfaceState(EdgeSurfaceState state, bool expanded, bool animate)
    {
        state.TargetExpanded = expanded;
        if (animate && expanded && state.Model.Animation.Enabled &&
            state.Model.Animation.DurationMilliseconds > 0)
        {
            CommitEdgeSurfaceState(
                state,
                expanded: false,
                retainContent: true,
                state.TransitionCollapsedBounds);
            BeginEdgeSurfaceTransition(state, expanded: true);
            return;
        }

        CommitEdgeSurfaceState(state, expanded, retainContent: false);
    }

    private void BeginEdgeSurfaceTransition(EdgeSurfaceState state, bool expanded)
    {
        var version = ++state.TransitionVersion;
        state.TargetExpanded = expanded;
        var targetRect = expanded
            ? state.ExpandedBounds
            : state.TransitionCollapsedBounds;
        var currentRect = new Rect(
            Canvas.GetLeft(state.Host),
            Canvas.GetTop(state.Host),
            state.Host.Width,
            state.Host.Height);
        var currentOpacity = state.Host.Opacity;
        state.Host.Child = state.Surface;
        state.Surface.Visibility = Visibility.Visible;
        state.Host.SetResourceReference(Border.BackgroundProperty, "TaskbarReadabilityBrush");
        // 延迟期间必须保持当前呈现值；目标基值只在版本校验后的完成回调中提交。
        // Keep the current presentation throughout the delay; commit target base values only from the version-checked completion callback.
        ClearEdgeAnimations(state.Host);
        SetEdgeSurfaceBaseRect(state.Host, currentRect);
        state.Host.Opacity = currentOpacity;
        var durationMilliseconds = Math.Clamp(
            state.Model.Animation.DurationMilliseconds,
            1,
            2_000);
        var delayMilliseconds = Math.Clamp(
            state.Model.Animation.DelayMilliseconds,
            0,
            2_000);
        var easing = state.Model.Animation.Easing switch
        {
            LayoutEasingKind.Linear => null,
            LayoutEasingKind.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginEdgeAnimation(state.Host, Canvas.LeftProperty, currentRect.Left, targetRect.Left, durationMilliseconds, delayMilliseconds, easing);
        BeginEdgeAnimation(state.Host, Canvas.TopProperty, currentRect.Top, targetRect.Top, durationMilliseconds, delayMilliseconds, easing);
        BeginEdgeAnimation(state.Host, FrameworkElement.WidthProperty, currentRect.Width, targetRect.Width, durationMilliseconds, delayMilliseconds, easing);
        BeginEdgeAnimation(state.Host, FrameworkElement.HeightProperty, currentRect.Height, targetRect.Height, durationMilliseconds, delayMilliseconds, easing);
        var opacityAnimation = new DoubleAnimation
        {
            From = currentOpacity,
            To = expanded ? 1 : 0.35,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = easing
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (_isClosed || state.TransitionVersion != version)
            {
                return;
            }

            if (expanded)
            {
                CommitEdgeSurfaceState(state, expanded: true, retainContent: false);
            }
            else
            {
                CompleteEdgeSurfaceCollapse(state);
            }
        };
        state.Host.BeginAnimation(
            UIElement.OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CompleteEdgeSurfaceCollapse(EdgeSurfaceState state)
    {
        if (!_expandedCollapseContainerIds.Remove(state.Model.InstanceId))
        {
            return;
        }

        state.TransitionVersion++;
        var retainBodyCorrection = _expandedCollapseContainerIds.Count > 0;
        if (!retainBodyCorrection)
        {
            _layoutBodyCorrectionX = 0;
            _layoutBodyCorrectionY = 0;
        }
        ApplyComponentLayout();
        RepositionPreservingLayoutBody();
        UpdateLayoutEdgePointerTimer();
    }

    private void CaptureLayoutBodyAnchor()
    {
        if (_layoutBodyAnchorScreen.HasValue || !IsLoaded ||
            ComponentSurfaceHost.ActualWidth <= 0 ||
            ComponentSurfaceHost.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            _layoutBodyAnchorScreen = ComponentSurfaceHost.PointToScreen(new Point());
        }
        catch
        {
            _layoutBodyAnchorScreen = null;
        }
    }

    private void RepositionPreservingLayoutBody()
    {
        ApplyResponsivePlayerDimensions();
        try
        {
            PositionOverTaskbar(force: true);
        }
        finally
        {
            _layoutBodyAnchorScreen = null;
        }
    }

    private bool TryResolveLayoutBodyTarget(
        double scaleX,
        double scaleY,
        out int left,
        out int top)
    {
        left = 0;
        top = 0;
        if (!_layoutBodyAnchorScreen.HasValue || _activeLayoutProfile is null)
        {
            return false;
        }

        // body 相对组合原点的偏移；保持 body 屏幕锚点不动，组合原点随 body 移动。
        // Keep the body's screen anchor fixed; the composition origin follows the body offset.
        var grid = LayoutGridSettings.Normalize(_activeLayoutProfile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var bodyGrid = LayoutRuntimeService.CalculateBodyGridBounds(_activeLayoutProfile)
            ?? LayoutGridRect.Unit(0, 0);
        var compositionGrid = LayoutRuntimeService.CalculateCompositionGridBounds(
            _activeLayoutProfile,
            _expandedCollapseContainerIds) ?? bodyGrid;
        left = (int)Math.Round(
            _layoutBodyAnchorScreen.Value.X - (bodyGrid.X - compositionGrid.X) * cell * scaleX);
        top = (int)Math.Round(
            _layoutBodyAnchorScreen.Value.Y - (bodyGrid.Y - compositionGrid.Y) * cell * scaleY);
        return true;
    }

    private static void CommitEdgeSurfaceState(
        EdgeSurfaceState state,
        bool expanded,
        bool retainContent,
        Rect? bounds = null)
    {
        state.TransitionVersion++;
        state.TargetExpanded = expanded;
        var rect = bounds ?? (expanded ? state.ExpandedBounds : state.CollapsedBounds);
        ClearEdgeAnimations(state.Host);
        SetEdgeSurfaceBaseRect(state.Host, rect);
        state.Host.Opacity = expanded || !retainContent ? 1 : 0.35;
        state.Surface.Visibility = expanded || retainContent
            ? Visibility.Visible
            : Visibility.Collapsed;
        // 折叠完成后才移除展开子树；过渡期间保留它以获得连续裁切，同时最终命中区域仍只含触发条。
        // Remove expanded content only after collapse; retaining it during transition enables continuous clipping while the final hit region remains trigger-only.
        state.Host.Child = expanded || retainContent ? state.Surface : null;
        state.Host.SetResourceReference(
            Border.BackgroundProperty,
            expanded || retainContent ? "TaskbarReadabilityBrush" : "TaskbarHoverBrush");
    }

    private static void SetEdgeSurfaceBaseRect(FrameworkElement host, Rect rect)
    {
        Canvas.SetLeft(host, rect.Left);
        Canvas.SetTop(host, rect.Top);
        host.Width = rect.Width;
        host.Height = rect.Height;
    }

    private static void ClearEdgeAnimations(FrameworkElement host)
    {
        host.BeginAnimation(Canvas.LeftProperty, null);
        host.BeginAnimation(Canvas.TopProperty, null);
        host.BeginAnimation(FrameworkElement.WidthProperty, null);
        host.BeginAnimation(FrameworkElement.HeightProperty, null);
        host.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static void BeginEdgeAnimation(
        FrameworkElement target,
        DependencyProperty property,
        double from,
        double to,
        int durationMilliseconds,
        int delayMilliseconds,
        IEasingFunction? easing)
    {
        target.BeginAnimation(
            property,
            new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
                EasingFunction = easing
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void DisposeEdgeSurfaces()
    {
        _layoutEdgePointerTimer?.Stop();
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
        _lastComponentMetricsSnapshot = snapshot;
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
        ResetLayoutBodyCorrection();
        _layoutDocument = document;
        ApplyComponentLayout();
        if (_metricSettings != previousMetricSettings)
        {
            ApplyMetricSettings();
        }
        ApplyResponsivePlayerDimensions();
        PositionOverTaskbar(force: true);
    }

    private void ResetLayoutBodyCorrection()
    {
        _layoutBodyAnchorScreen = null;
        _layoutBodyCorrectionX = 0;
        _layoutBodyCorrectionY = 0;
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
        LayoutCollapseContainer model,
        Border host,
        ComponentLayoutSurface surface,
        Rect expandedBounds,
        Rect collapsedBounds,
        Rect transitionCollapsedBounds)
    {
        internal LayoutCollapseContainer Model { get; } = model;
        internal Border Host { get; } = host;
        internal ComponentLayoutSurface Surface { get; } = surface;
        internal Rect ExpandedBounds { get; } = expandedBounds;
        internal Rect CollapsedBounds { get; } = collapsedBounds;
        internal Rect TransitionCollapsedBounds { get; } = transitionCollapsedBounds;
        internal bool TargetExpanded { get; set; }
        internal int TransitionVersion { get; set; }
    }

}