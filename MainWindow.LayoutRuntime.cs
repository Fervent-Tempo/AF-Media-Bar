using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Controls;
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
    private LayoutEdge? _unavailableLayoutEdge;

    private void InitializeComponentLayout(LayoutDocument document)
    {
        _layoutDocument = document;
        _componentSurface = new ComponentLayoutSurface();
        _componentSurface.CommandRequested += ComponentSurface_OnCommandRequested;
        _componentSurface.MetricsRequested += ComponentSurface_OnMetricsRequested;
        _componentSurface.SourceRequested += ComponentSurface_OnSourceRequested;
        ComponentSurfaceHost.Child = _componentSurface;

        // 旧节点暂时作为弹窗锚点和行为回退保留；将其设为透明可避免第二棵树参与视觉合成。
        // Legacy nodes remain as popup anchors and behavior fallback; transparency prevents a second visible tree from being composited.
        PlayerContent.Opacity = 0;
        VerticalPlayerContent.Opacity = 0;
        ApplyComponentLayout();
    }

    private void ApplyComponentLayout()
    {
        if (_componentSurface is null || _layoutDocument is null)
        {
            return;
        }

        _activeLayoutProfile = _layoutRuntimeService.ResolveProfile(
            _layoutDocument,
            _windowSettings.HostMode,
            _isVerticalLayout);
        _unavailableLayoutEdge = _activeLayoutProfile.HostMode == WindowHostMode.Taskbar
            ? TaskbarEdgeService.TryResolveCurrent()
            : null;
        _componentSurface.Apply(_activeLayoutProfile, _isExpanded);
        var stripSize = LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile);
        var compositionSize = LayoutRuntimeService.CalculateCompositionSize(
            _activeLayoutProfile,
            _unavailableLayoutEdge);
        var edgeInsets = CalculateEdgeInsets(_activeLayoutProfile, _unavailableLayoutEdge);
        ComponentCompositionHost.Width = compositionSize.WidthDip;
        ComponentCompositionHost.Height = compositionSize.HeightDip;
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
            _unavailableLayoutEdge);
        ApplyComponentMetricRefreshInterval();
    }

    private void RebuildEdgeSurfaces(
        LayoutProfile profile,
        LayoutSize stripSize,
        LayoutSize compositionSize,
        Thickness edgeInsets,
        LayoutEdge? unavailableEdge)
    {
        DisposeEdgeSurfaces();
        foreach (var model in profile.EdgeContainers.Where(model =>
                     model.Enabled &&
                     (profile.HostMode != WindowHostMode.Taskbar || model.Edge != unavailableEdge)))
        {
            var size = LayoutRuntimeService.MeasureEdgeContainer(profile, model);
            if (size.WidthDip <= 0 || size.HeightDip <= 0)
            {
                continue;
            }

            var surface = new ComponentLayoutSurface();
            surface.CommandRequested += ComponentSurface_OnCommandRequested;
            surface.MetricsRequested += ComponentSurface_OnMetricsRequested;
            surface.SourceRequested += ComponentSurface_OnSourceRequested;
            surface.ApplyEdge(profile, model);
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
                size,
                stripSize,
                compositionSize,
                edgeInsets);
            host.Tag = state;
            host.MouseEnter += EdgeSurfaceHost_OnMouseEnter;
            host.MouseLeave += EdgeSurfaceHost_OnMouseLeave;
            _edgeSurfaces.Add(state);
            LayoutEdgeSurfaceHost.Children.Add(host);
            ApplyEdgeSurfaceState(state, expanded: false, animate: false);
        }
    }

    private static EdgeSurfaceState CreateEdgeSurfaceState(
        LayoutEdgeContainer model,
        Border host,
        ComponentLayoutSurface surface,
        LayoutSize size,
        LayoutSize stripSize,
        LayoutSize compositionSize,
        Thickness edgeInsets)
    {
        var expandedLeft = model.Edge switch
        {
            LayoutEdge.Left => edgeInsets.Left - size.WidthDip,
            LayoutEdge.Right => edgeInsets.Left + stripSize.WidthDip,
            _ => edgeInsets.Left + (stripSize.WidthDip - size.WidthDip) / 2 + model.OffsetDip
        };
        var expandedTop = model.Edge switch
        {
            LayoutEdge.Top => edgeInsets.Top - size.HeightDip,
            LayoutEdge.Bottom => edgeInsets.Top + stripSize.HeightDip,
            _ => edgeInsets.Top + (stripSize.HeightDip - size.HeightDip) / 2 + model.OffsetDip
        };
        expandedLeft = Math.Clamp(expandedLeft, 0, Math.Max(0, compositionSize.WidthDip - size.WidthDip));
        expandedTop = Math.Clamp(expandedTop, 0, Math.Max(0, compositionSize.HeightDip - size.HeightDip));

        var trigger = Math.Clamp(model.TriggerThicknessDip, 2, 24);
        var collapsedWidth = model.Edge is LayoutEdge.Top or LayoutEdge.Bottom
            ? Math.Min(Math.Max(36, size.WidthDip), 72)
            : trigger;
        var collapsedHeight = model.Edge is LayoutEdge.Left or LayoutEdge.Right
            ? Math.Min(Math.Max(36, size.HeightDip), 72)
            : trigger;
        var collapsedLeft = model.Edge switch
        {
            LayoutEdge.Left => edgeInsets.Left - trigger,
            LayoutEdge.Right => edgeInsets.Left + stripSize.WidthDip,
            _ => edgeInsets.Left + (stripSize.WidthDip - collapsedWidth) / 2 + model.OffsetDip
        };
        var collapsedTop = model.Edge switch
        {
            LayoutEdge.Top => edgeInsets.Top - trigger,
            LayoutEdge.Bottom => edgeInsets.Top + stripSize.HeightDip,
            _ => edgeInsets.Top + (stripSize.HeightDip - collapsedHeight) / 2 + model.OffsetDip
        };
        return new EdgeSurfaceState(
            model,
            host,
            surface,
            new Rect(expandedLeft, expandedTop, size.WidthDip, size.HeightDip),
            new Rect(collapsedLeft, collapsedTop, collapsedWidth, collapsedHeight));
    }

    private static Thickness CalculateEdgeInsets(LayoutProfile profile, LayoutEdge? unavailableEdge)
    {
        var sizes = profile.EdgeContainers
            .Where(container => container.Enabled &&
                (profile.HostMode != WindowHostMode.Taskbar || container.Edge != unavailableEdge))
            .Select(container => (container.Edge, Size: LayoutRuntimeService.MeasureEdgeContainer(profile, container)))
            .ToArray();
        return new Thickness(
            sizes.Where(item => item.Edge == LayoutEdge.Left).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Top).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Right).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max(),
            sizes.Where(item => item.Edge == LayoutEdge.Bottom).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max());
    }

    private void EdgeSurfaceHost_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border { Tag: EdgeSurfaceState state })
        {
            ApplyEdgeSurfaceState(state, expanded: true, animate: true);
        }
    }

    private void EdgeSurfaceHost_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border { Tag: EdgeSurfaceState state } && !state.Host.IsMouseOver)
        {
            ApplyEdgeSurfaceState(state, expanded: false, animate: true);
        }
    }

    private static void ApplyEdgeSurfaceState(EdgeSurfaceState state, bool expanded, bool animate)
    {
        var rect = expanded ? state.ExpandedBounds : state.CollapsedBounds;
        Canvas.SetLeft(state.Host, rect.Left);
        Canvas.SetTop(state.Host, rect.Top);
        state.Host.Width = rect.Width;
        state.Host.Height = rect.Height;
        state.Surface.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        state.Host.Background = expanded
            ? state.ExpandedBackground
            : state.TriggerBackground;
        state.Host.BeginAnimation(UIElement.OpacityProperty, null);
        state.Host.Opacity = 1;
        if (!animate || !state.Model.Animation.Enabled || state.Model.Animation.DurationMilliseconds <= 0)
        {
            return;
        }

        state.Host.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = 0.35,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(
                    Math.Clamp(state.Model.Animation.DurationMilliseconds, 0, 2_000)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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

    private void ComponentSurface_OnSourceRequested(object? sender, EventArgs e)
    {
        ShowSelectedMediaSource();
    }

    private void ComponentSurface_OnLayoutPointerNearChanged(bool pointerNear)
    {
        _componentSurface?.SetPointerNear(pointerNear);
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
        _layoutDocument = document;
        ApplyComponentLayout();
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
