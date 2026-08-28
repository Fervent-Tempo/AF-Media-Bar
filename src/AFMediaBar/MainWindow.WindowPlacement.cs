using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar;

/// <summary>
/// 协调任务栏探测、窗口定位和响应式布局；系统查询与占用区域扫描由专用服务负责。
/// Coordinates taskbar discovery, window placement, and responsive layout while dedicated services own system queries.
/// </summary>
public partial class MainWindow
{
    private const int HorizontalMarginAt96Dpi = 8;
    private const int VerticalMarginAt96Dpi = 4;
    private const double CollapsedTriggerVisibleDip = 2;

    private readonly TaskbarPlacementService _taskbarPlacementService = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _placementTimer;
    private TaskbarSettings _taskbarSettings;
    private NativeMethods.Rect? _lastTaskbarRect;
    private int? _automaticLeft;
    // 自动定位只允许一次扫描；扫描期间的新请求会在结束后再补跑一次。
    // Automatic placement allows one scan; concurrent requests schedule one follow-up scan.
    private int _placementRefreshInProgress;
    private int _placementRefreshRequested;

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (IsEnvironmentSuspended || _environmentRecoveryTimer.IsEnabled)
        {
            return;
        }

        try
        {
            if (_windowSettings.HostMode == WindowHostMode.Taskbar)
            {
                RefreshTaskbarSettings();
            }

            PositionOverTaskbar(force: false);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("position-timer", exception);
            ScheduleEnvironmentRecovery("position-timer-failure");
        }
    }

    private async void OnPlacementTimerTick(object? sender, EventArgs e)
    {
        if (IsEnvironmentSuspended || _environmentRecoveryTimer.IsEnabled)
        {
            return;
        }

        try
        {
            await RefreshAutomaticPlacementAsync();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("placement-timer", exception);
            ScheduleEnvironmentRecovery("placement-timer-failure");
        }
    }

    private void Taskbar_OnChanged(TaskbarWindowEvent taskbarEvent)
    {
        try
        {
            if (IsEnvironmentSuspended ||
                _environmentRecoveryTimer.IsEnabled ||
                _windowSettings.HostMode != WindowHostMode.Taskbar)
            {
                return;
            }

            RefreshTaskbarSettings();
            if (TryGetTaskbarBounds(out var bounds))
            {
                var sizeChanged = !_lastTaskbarRect.HasValue ||
                    _lastTaskbarRect.Value.Width != bounds.ScreenBounds.Width ||
                    _lastTaskbarRect.Value.Height != bounds.ScreenBounds.Height;
                if (_placementSettings.AutomaticPlacement && sizeChanged)
                {
                    _automaticLeft = null;
                    _ = RefreshAutomaticPlacementSafelyAsync();
                }

                // Vertical location changes are inherited from the Explorer parent.
                // Repositioning the child during that animation would reintroduce the lag.
                if (taskbarEvent.EventId == NativeMethods.EventObjectLocationChange &&
                    !sizeChanged)
                {
                    return;
                }
            }

            PositionOverTaskbar(force: true);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("taskbar-event", exception);
            ScheduleEnvironmentRecovery("taskbar-event-failure");
        }
    }

    private void RefreshTaskbarSettings()
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar)
        {
            return;
        }

        var settings = TaskbarSettingsService.Read();
        if (settings.Alignment == TaskbarAlignment.Unknown &&
            _taskbarSettings.Alignment != TaskbarAlignment.Unknown)
        {
            settings = settings with { Alignment = _taskbarSettings.Alignment };
        }

        if (settings.Alignment != _taskbarSettings.Alignment)
        {
            _automaticLeft = null;
            _taskbarSettings = settings;
            PositionOverTaskbar(force: true);
            if (_placementSettings.AutomaticPlacement)
            {
                _ = RefreshAutomaticPlacementSafelyAsync();
            }
        }
        else
        {
            _taskbarSettings = settings;
        }
    }

    private bool TryGetTaskbarBounds(out TaskbarHostBounds bounds)
    {
        bounds = default;
        return _taskbarHostService?.TryGetBounds(out bounds) == true;
    }

    private void PositionOverTaskbar(bool force)
    {
        try
        {
            PositionOverTaskbarCore(force);
            SyncWindowStateProjection();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("window-position", exception);
            if (_windowSettings.HostMode == WindowHostMode.Floating)
            {
                RevealFloatingFallback();
            }
            else
            {
                ScheduleEnvironmentRecovery("window-position-failure");
            }
        }
    }

    private void PositionOverTaskbarCore(bool force)
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        if (_windowSettings.HostMode == WindowHostMode.Floating)
        {
            PositionFloatingWindow(force);
            return;
        }

        _floatingEdge = 0;

        CollapseWhenPointerLeavesWindow();

        if (_windowSettings.HideWhenNoMedia &&
            !_hasConnectedMedia &&
            !_windowSettings.AlwaysOnTop &&
            !_isMenuOpen)
        {
            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        if (!_windowSettings.AlwaysOnTop &&
            NativeMethods.ShouldHideForFullScreenApp(_windowHandle))
        {
            if (Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
            }

            StopMarquees();

            return;
        }

        if (!TryGetTaskbarBounds(out var bounds))
        {
            if (_windowSettings.AlwaysOnTop)
            {
                Visibility = Visibility.Visible;
                Topmost = true;
                return;
            }

            Visibility = Visibility.Collapsed;
            StopMarquees();
            return;
        }

        var taskbarRect = bounds.ScreenBounds;
        var scale = bounds.Scale;
        var currentTaskbarEdge = TaskbarEdgeService.TryResolveCurrent();
        if (_unavailableLayoutEdge != currentTaskbarEdge)
        {
            // Explorer 允许用户在运行期间移动任务栏；边缘约束变化后立即重建组合，避免旧边缘容器继续显示。
            // Explorer can move the taskbar at runtime; rebuild immediately so a container cannot remain on the newly occupied edge.
            _unavailableLayoutEdge = currentTaskbarEdge;
            ResetLayoutBodyCorrection();
            ApplyComponentLayout();
            ApplyResponsivePlayerDimensions();
            force = true;
        }
        var verticalLayout = ResolveVerticalTaskbarLayout(taskbarRect);
        ApplyPlayerLayout(verticalLayout);
        ConfigurePopupPlacement(bounds, verticalLayout);
        var playerScale = CalculateTaskbarPlayerScale(bounds, verticalLayout);
        ApplyPlayerScale(playerScale);
        if (_placementSettings.AutomaticPlacement &&
            _lastTaskbarRect.HasValue &&
            _lastTaskbarRect.Value.Width != taskbarRect.Width)
        {
            _automaticLeft = null;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            force = true;
        }

        var width = Math.Max(1, (int)Math.Ceiling(PlayerRoot.Width * playerScale.X * scale));
        var height = Math.Max(1, (int)Math.Ceiling(PlayerRoot.Height * playerScale.Y * scale));
        var collapsedInsets = _activeLayoutProfile is null
            ? new Thickness(0)
            : CalculateCollapsedEdgeInsets(
                _activeLayoutProfile,
                _expandedCollapseContainerIds);
        var collapsedLeft = (int)Math.Round(collapsedInsets.Left * playerScale.X * scale);
        var collapsedTop = (int)Math.Round(collapsedInsets.Top * playerScale.Y * scale);
        var collapsedRight = (int)Math.Round(collapsedInsets.Right * playerScale.X * scale);
        var collapsedBottom = (int)Math.Round(collapsedInsets.Bottom * playerScale.Y * scale);
        int left;
        int top;
        if (verticalLayout)
        {
            var margin = Math.Min(
                (int)Math.Round(VerticalMarginAt96Dpi * scale),
                Math.Max(0, (taskbarRect.Height - height) / 2));
            var minTop = taskbarRect.Top + margin - collapsedTop;
            var maxTop = Math.Max(minTop, taskbarRect.Bottom - margin - height + collapsedBottom);
            top = Math.Clamp(
                taskbarRect.Top + (int)Math.Round(
                    _placementSettings.ManualVerticalOffsetDip * scale) - collapsedTop,
                minTop,
                maxTop);
            left = taskbarRect.Left + (taskbarRect.Width - width) / 2;
        }
        else
        {
            var margin = Math.Min(
                (int)Math.Round(HorizontalMarginAt96Dpi * scale),
                Math.Max(0, (taskbarRect.Width - width) / 2));
            var minLeft = taskbarRect.Left + margin - collapsedLeft;
            var maxLeft = Math.Max(minLeft, taskbarRect.Right - margin - width + collapsedRight);
            var desiredLeft = _placementSettings.AutomaticPlacement
                ? ResolveAutomaticLeft(taskbarRect, scale, minLeft)
                : taskbarRect.Left + (int)Math.Round(
                    _placementSettings.ManualOffsetDip * scale);
            desiredLeft ??= _lastPositionLeft;
            if (!desiredLeft.HasValue)
            {
                _ = RefreshAutomaticPlacementSafelyAsync();
                return;
            }

            left = Math.Clamp(desiredLeft.Value, minLeft, maxLeft);
            var centeredTop = taskbarRect.Top + (taskbarRect.Height - height) / 2;
            top = Math.Clamp(
                centeredTop + (int)Math.Round(
                    _placementSettings.TaskbarTopOffsetDip * scale),
                taskbarRect.Top - collapsedTop,
                Math.Max(taskbarRect.Top - collapsedTop, taskbarRect.Bottom - height + collapsedBottom));
        }

        if (TryResolveLayoutBodyTarget(
                playerScale.X * scale,
                playerScale.Y * scale,
                out var layoutTargetLeft,
                out var layoutTargetTop))
        {
            _layoutBodyCorrectionX = layoutTargetLeft - left;
            _layoutBodyCorrectionY = layoutTargetTop - top;
            left = layoutTargetLeft;
            top = layoutTargetTop;
        }
        else if (_layoutBodyCorrectionX != 0 || _layoutBodyCorrectionY != 0)
        {
            left += _layoutBodyCorrectionX;
            top += _layoutBodyCorrectionY;
        }

        var rectChanged = !_lastTaskbarRect.HasValue ||
            !_lastTaskbarRect.Value.Equals(taskbarRect);
        var positionChanged = _lastPositionLeft != left || _lastPositionTop != top;

        Height = PlayerRoot.Height * playerScale.Y;
        Topmost = _windowSettings.AlwaysOnTop;
        if (!force && !rectChanged && !positionChanged)
        {
            RevealAfterPlacement();
            return;
        }

        _lastTaskbarRect = taskbarRect;
        _lastPositionLeft = left;
        _lastPositionTop = top;
        if (_taskbarHostService?.Position(
                left,
                top,
                width,
                height,
                visible: true,
                topmost: _windowSettings.AlwaysOnTop,
                inputRects: BuildWindowInputRects(playerScale.X * scale)) != true)
        {
            DiagnosticsLogService.Write(
                "taskbar-window-position-failed",
                details: $"Left={left};Top={top};Width={width};Height={height}");
            ScheduleEnvironmentRecovery("taskbar-window-position-failure");
            return;
        }

        RevealAfterPlacement();
    }

    private bool ResolveVerticalTaskbarLayout(NativeMethods.Rect taskbarRect)
    {
        return _windowSettings.LayoutMode switch
        {
            PlayerLayoutMode.Vertical => true,
            PlayerLayoutMode.Horizontal => false,
            _ => taskbarRect.Height > taskbarRect.Width
        };
    }

    private void ConfigurePopupPlacement(TaskbarHostBounds bounds, bool verticalLayout)
    {
        if (!verticalLayout)
        {
            SetPopupPlacement(PlacementMode.Top, 0, -7);
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(bounds.Taskbar, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        var taskbarIsOnLeft = monitor == nint.Zero ||
            !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) ||
            bounds.ScreenBounds.Left + bounds.ScreenBounds.Width / 2 <=
                monitorInfo.Monitor.Left + monitorInfo.Monitor.Width / 2;
        SetPopupPlacement(
            taskbarIsOnLeft ? PlacementMode.Right : PlacementMode.Left,
            taskbarIsOnLeft ? 7 : -7,
            0);
    }

    private void SetPopupPlacement(
        PlacementMode placement,
        double horizontalOffset,
        double verticalOffset)
    {
        foreach (var popup in new[]
        {
            VolumeControlPopup,
            OutputDevicePopup,
            OutputDeviceStatusPopup,
            VolumeStatusPopup
        })
        {
            popup.Placement = placement;
            popup.HorizontalOffset = horizontalOffset;
            popup.VerticalOffset = verticalOffset;
        }
    }

    private (double X, double Y) CalculateTaskbarPlayerScale(
        TaskbarHostBounds bounds,
        bool verticalLayout)
    {
        var requestedScale = _windowSettings.ThicknessScalePercent / 100d;
        var availableThickness = verticalLayout
            ? bounds.ScreenBounds.Width
            : bounds.ScreenBounds.Height;
        var availableLength = verticalLayout
            ? bounds.ScreenBounds.Height
            : bounds.ScreenBounds.Width;
        var designThickness = verticalLayout
            ? VerticalPlayerWidth
            : HorizontalPlayerHeight;
        var designLength = verticalLayout ? PlayerRoot.Height : PlayerRoot.Width;
        var maximumThicknessScale = availableThickness / (designThickness * bounds.Scale);
        var maximumLengthScale = availableLength / (designLength * bounds.Scale);
        var scale = Math.Clamp(
            Math.Min(
                requestedScale,
                Math.Min(maximumThicknessScale, maximumLengthScale)),
            0.1,
            1.25);
        return (scale, scale);
    }

    private void ApplyPlayerScale((double X, double Y) scale)
    {
        PlayerScaleTransform.ScaleX = scale.X;
        PlayerScaleTransform.ScaleY = scale.Y;
    }

    private void ApplyPlayerLayout(bool vertical)
    {
        if (_isVerticalLayout == vertical)
        {
            return;
        }

        var contentVisible = PlayerContent.Visibility == Visibility.Visible ||
            VerticalPlayerContent.Visibility == Visibility.Visible;
        _isVerticalLayout = vertical;
        ResetLayoutBodyCorrection();
        SetPlayerContentVisibility(contentVisible);
        var previousMetricSettings = _metricSettings;
        ApplyComponentLayout();
        if (_metricSettings != previousMetricSettings)
        {
            // 定位调用栈可能正在切换布局；延后应用服务，避免音频设置刷新重入窗口定位。
            // Placement may already be switching layouts; defer service updates to avoid re-entering window placement.
            // 方向切换的服务刷新会延后到定位调用栈返回；关闭期间丢弃回调，避免触及已释放的音频资源。
            // Defer service refresh until placement returns; discard it after close so disposed audio resources are never re-entered.
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isClosed)
                {
                    ApplyMetricSettings();
                }
            });
        }
        ApplyResponsivePlayerDimensions();
        VolumeControlPopup.PlacementTarget = vertical
            ? VerticalVolumeControlHost
            : VolumeControlHost;
        OutputDevicePopup.PlacementTarget = vertical
            ? VerticalOutputDeviceHost
            : OutputDeviceHost;
        OutputDeviceStatusPopup.PlacementTarget = vertical
            ? VerticalOutputDeviceHost
            : OutputDeviceHost;
        VolumeStatusPopup.PlacementTarget = vertical
            ? VerticalVolumeControlHost
            : VolumeControlHost;
        SetExpanded(_isExpanded, animate: false);
        ApplyPlacementSettings();
        if (EdgeCollapseIndicator.Visibility == Visibility.Visible)
        {
            UpdateEdgeCollapseIndicator(visible: true);
        }
        ScheduleMarqueeUpdate();
    }

    private void SetPlayerContentVisibility(bool visible)
    {
        PlayerContent.Visibility = visible && !_isVerticalLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalPlayerContent.Visibility = visible && _isVerticalLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private double CalculateHorizontalPlayerWidth()
    {
        var gap = CalculateLengthGap();
        var centralWidth = _windowSettings.ShowMediaInfo
            ? CentralHostWidth
            : CompactCentralHostWidth;
        return (_windowSettings.ShowArtwork ? 40 + gap : 0) +
            centralWidth +
            (_metricSettings.OutputDeviceSwitcherEnabled ? 36 + gap : 0) +
            (_metricSettings.VolumeControlEnabled ? 36 + gap : 0) +
            (_metricSettings.SelectedCount > 0 ? 74 + gap : 0) +
            1 + gap * 4;
    }

    private double CalculateVerticalPlayerHeight()
    {
        var artworkMargin = VerticalArtworkHost.Margin;
        var centralMargin = VerticalCentralHost.Margin;
        var outputMargin = VerticalOutputDeviceHost.Margin;
        var volumeMargin = VerticalVolumeControlHost.Margin;
        var metricsMargin = VerticalMetricsHost.Margin;
        return (_windowSettings.ShowArtwork
                ? 40 + artworkMargin.Top + artworkMargin.Bottom
                : 0) +
            VerticalCentralHost.Height + centralMargin.Bottom +
            (_metricSettings.OutputDeviceSwitcherEnabled
                ? 40 + outputMargin.Top + outputMargin.Bottom
                : 0) +
            (_metricSettings.VolumeControlEnabled
                ? 40 + volumeMargin.Top + volumeMargin.Bottom
                : 0) +
            (_metricSettings.SelectedCount > 0
                ? 24 + metricsMargin.Top + metricsMargin.Bottom
                : 0);
    }

    private double CalculateLengthGap()
    {
        return Math.Clamp(
            4 + (_windowSettings.LengthScalePercent - 100) * 0.14,
            0.25,
            8);
    }

    private double CalculateControlButtonSpacing()
    {
        return CalculateLengthGap() / 4;
    }

    private double CalculateVerticalControlSpacing()
    {
        return Math.Max(0, CalculateLengthGap() - 4) * 0.5;
    }

    private void ApplyResponsivePlayerDimensions()
    {
        var gap = CalculateLengthGap();
        var buttonSpacing = CalculateControlButtonSpacing();
        var buttonMargin = new Thickness(buttonSpacing, 0, buttonSpacing, 0);
        PreviousButton.Margin = buttonMargin;
        PlayPauseButton.Margin = buttonMargin;
        NextButton.Margin = buttonMargin;
        var controlsWidth = 3 * (36 + buttonSpacing * 2);
        ControlsHost.Width = controlsWidth;
        HorizontalControlsPanel.Width = controlsWidth;

        var verticalButtonSpacing = CalculateVerticalControlSpacing();
        var verticalButtonMargin = new Thickness(
            0,
            verticalButtonSpacing,
            0,
            verticalButtonSpacing);
        VerticalPreviousButton.Margin = verticalButtonMargin;
        VerticalPlayPauseButton.Margin = verticalButtonMargin;
        VerticalNextButton.Margin = verticalButtonMargin;

        ArtworkHost.Margin = new Thickness(gap / 2, 2, gap / 2, 2);
        ArtworkColumn.Width = new GridLength(
            _windowSettings.ShowArtwork ? 40 + gap : 0);
        InfoContentGrid.Margin = new Thickness(5 + gap, 0, 4 + gap, 0);
        var horizontalComponentMargin = new Thickness(gap / 2, 0, gap / 2, 0);
        OutputDeviceHost.Margin = horizontalComponentMargin;
        VolumeControlHost.Margin = horizontalComponentMargin;
        MetricsHost.Margin = horizontalComponentMargin;
        EndDivider.Margin = new Thickness(gap * 2, 0, gap * 2, 0);

        VerticalArtworkHost.Margin = new Thickness(0, gap * 0.75, 0, gap * 1.25);
        VerticalCentralHost.Margin = new Thickness(0, 0, 0, gap * 0.75);
        var verticalControlSpacing = CalculateVerticalControlSpacing();
        var verticalControlMargin = new Thickness(
            0,
            verticalControlSpacing,
            0,
            verticalControlSpacing);
        var verticalCentralHeight = Math.Max(114, 108 + verticalControlSpacing * 6);
        VerticalCentralHost.Height = verticalCentralHeight;
        VerticalInfoHost.Height = verticalCentralHeight;
        VerticalControlsHost.Height = verticalCentralHeight;
        VerticalOutputDeviceHost.Margin = verticalControlMargin;
        VerticalVolumeControlHost.Margin = verticalControlMargin;
        VerticalMetricsHost.Margin = new Thickness(0, gap * 0.75, 0, gap * 0.5);

        if (_activeLayoutProfile is not null)
        {
            var desiredSize = LayoutRuntimeService.CalculateCompositionSize(
                _activeLayoutProfile,
                _expandedCollapseContainerIds);
            PlayerRoot.Width = desiredSize.WidthDip;
            PlayerRoot.Height = desiredSize.HeightDip;
        }
        else
        {
            PlayerRoot.Width = _isVerticalLayout
                ? VerticalPlayerWidth
                : CalculateHorizontalPlayerWidth();
            PlayerRoot.Height = _isVerticalLayout
                ? CalculateVerticalPlayerHeight()
                : HorizontalPlayerHeight;
        }
        VerticalPlayerContent.Height = PlayerRoot.Height;
    }

    private int? ResolveAutomaticLeft(
        NativeMethods.Rect taskbarRect,
        double scale,
        int fallbackLeft)
    {
        if (_automaticLeft.HasValue)
        {
            return _automaticLeft.Value;
        }

        var taskbarWidthDip = (int)Math.Round(taskbarRect.Width / scale);
        var playerWidthDip = (int)Math.Round(
            PlayerRoot.Width * PlayerScaleTransform.ScaleX);
        var cachedOffset = _placementSettings.CachedAutomaticOffsetDip;
        var cachedTaskbarWidth = _placementSettings.CachedTaskbarWidthDip;
        var cachedPlayerWidth = _placementSettings.CachedPlayerWidthDip;
        var cachedAlignment = _placementSettings.CachedTaskbarAlignment;
        var cacheMatches = cachedOffset.HasValue &&
            cachedTaskbarWidth.HasValue &&
            cachedPlayerWidth.HasValue &&
            cachedAlignment.HasValue &&
            Math.Abs(cachedTaskbarWidth.Value - taskbarWidthDip) <= 2 &&
            Math.Abs(cachedPlayerWidth.Value - playerWidthDip) <= 1 &&
            cachedAlignment.Value == _taskbarSettings.Alignment;
        if (cacheMatches)
        {
            _automaticLeft = taskbarRect.Left + (int)Math.Round(
                cachedOffset.GetValueOrDefault() * scale);
            return _automaticLeft.Value;
        }

        if (_taskbarSettings.Alignment == TaskbarAlignment.Left)
        {
            // 重建任务栏后 UI Automation 暴露较慢；精确扫描前暂用可用区中点。
            // UI Automation lags after rebuilds; use the free-area midpoint until scanned.
            var playerWidth = (int)Math.Ceiling(
                PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
            var availableWidth = Math.Max(0, taskbarRect.Width - playerWidth);
            return taskbarRect.Left + availableWidth / 2;
        }

        return fallbackLeft;
    }

    private void RevealAfterPlacement()
    {
        if (_hasPresented && Opacity == 1)
        {
            return;
        }

        _hasPresented = true;
        Opacity = 1;
        ScheduleMarqueeUpdate();
    }

    private async Task RefreshAutomaticPlacementAsync()
    {
        if (_windowSettings.HostMode != WindowHostMode.Taskbar ||
            _isVerticalLayout ||
            !_placementSettings.AutomaticPlacement ||
            _windowHandle == nint.Zero ||
            _isMenuOpen)
        {
            return;
        }

        if (Interlocked.Exchange(ref _placementRefreshInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _placementRefreshRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _placementRefreshRequested, 0);
                await RefreshAutomaticPlacementCoreAsync();
            }
            while (_placementSettings.AutomaticPlacement &&
                !_isMenuOpen &&
                Interlocked.Exchange(ref _placementRefreshRequested, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _placementRefreshInProgress, 0);
            if (_placementSettings.AutomaticPlacement &&
                !_isMenuOpen &&
                Interlocked.Exchange(ref _placementRefreshRequested, 0) != 0)
            {
                _ = RefreshAutomaticPlacementSafelyAsync();
            }
        }
    }

    private async Task RefreshAutomaticPlacementCoreAsync()
    {
        if (!TryGetTaskbarBounds(out var bounds))
        {
            return;
        }

        var taskbar = bounds.Taskbar;
        var taskbarRect = bounds.ScreenBounds;
        var alignment = _taskbarSettings.Alignment;
        var scale = bounds.Scale;
        var margin = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
        var playerWidth = (int)Math.Ceiling(
            PlayerRoot.Width * PlayerScaleTransform.ScaleX * scale);
        TaskbarPlacementResult? placement;
        try
        {
            placement = await _taskbarPlacementService.FindBestLeftAsync(
                taskbar,
                taskbarRect,
                playerWidth,
                margin,
                _automaticLeft).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            return;
        }

        var currentSettings = TaskbarSettingsService.Read();
        if (currentSettings.Alignment != TaskbarAlignment.Unknown)
        {
            _taskbarSettings = currentSettings;
        }

        var hasReliablePlacement = placement.HasValue &&
            (_taskbarSettings.Alignment != TaskbarAlignment.Left ||
                placement.Value.OccupiedElementCount > 0);
        if (!_placementSettings.AutomaticPlacement ||
            _isMenuOpen ||
            !hasReliablePlacement ||
            alignment != _taskbarSettings.Alignment)
        {
            if (alignment != _taskbarSettings.Alignment)
            {
                Interlocked.Exchange(ref _placementRefreshRequested, 1);
            }

            return;
        }

        _automaticLeft = placement!.Value.Left;
        var cachedSettings = _placementSettings with
        {
            CachedAutomaticOffsetDip = (int)Math.Round(
                (placement.Value.Left - taskbarRect.Left) / scale),
            CachedTaskbarWidthDip = (int)Math.Round(taskbarRect.Width / scale),
            CachedPlayerWidthDip = (int)Math.Round(
                PlayerRoot.Width * PlayerScaleTransform.ScaleX),
            CachedTaskbarAlignment = _taskbarSettings.Alignment == TaskbarAlignment.Unknown
                ? null
                : _taskbarSettings.Alignment
        };
        if (cachedSettings != _placementSettings)
        {
            _placementSettings = cachedSettings;
            SavePlacementSettings(showError: false);
        }

        PositionOverTaskbar(force: true);
    }

    private void CollapseWhenPointerLeavesWindow()
    {
        if (!_isExpanded ||
            _isMenuOpen ||
            OutputDevicePopup.IsOpen ||
            VolumeControlPopup.IsOpen ||
            _isDragging ||
            !NativeMethods.GetCursorPos(out var cursor) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return;
        }

        var isInside = cursor.X >= windowRect.Left &&
            cursor.X < windowRect.Right &&
            cursor.Y >= windowRect.Top &&
            cursor.Y < windowRect.Bottom;
        if (!isInside)
        {
            ComponentSurface_OnLayoutPointerNearChanged(pointerNear: false);
            SetExpanded(expanded: false, animate: true);
        }
    }

}
