using System.Windows;
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
        _componentSurface.Apply(_activeLayoutProfile, _isExpanded);
        var desiredSize = LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile);
        ComponentSurfaceHost.Width = desiredSize.WidthDip;
        ComponentSurfaceHost.Height = desiredSize.HeightDip;
        ComponentSurfaceHost.CornerRadius = new CornerRadius(
            Math.Clamp(_activeLayoutProfile.Surface.CornerRadiusDip, 0, 32));
        ComponentSurfaceHost.Visibility = Visibility.Visible;
        ApplyComponentMetricRefreshInterval();
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
    }

    private void ComponentSurface_OnMetricsChanged(string text)
    {
        _componentSurface?.SetMetricsText(text);
    }

    private void ComponentSurface_OnMetricsSnapshotChanged(SystemMetricsSnapshot snapshot)
    {
        _componentSurface?.SetMetricsSnapshot(snapshot);
    }

    private void ComponentSurface_OnSpectrumChanged(IReadOnlyList<float> values)
    {
        _componentSurface?.SetSpectrum(values);
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

}
