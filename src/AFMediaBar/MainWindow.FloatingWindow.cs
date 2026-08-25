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
/// 协调浮动窗口定位、贴边折叠和动画，只把最终窗口操作委托给任务栏宿主服务。
/// Coordinates floating placement, edge collapse, and animation while delegating final HWND operations to the host service.
/// </summary>
public partial class MainWindow
{
    private const int EdgeVisiblePixels = 6;
    private const int EdgeActivationDistance = 72;
    private const int EdgeActivationSpanPadding = 80;
    private const int EdgeAnimationDurationMilliseconds = 180;

    private readonly DispatcherTimer _edgeAnimationTimer;
    private readonly DispatcherTimer _edgeHoverTimer;
    private int? _floatingNormalLeft;
    private int? _floatingNormalTop;
    private int _floatingEdge;
    private int _expandedEdge;
    private int _lastFloatingWidth;
    private int _lastFloatingHeight;
    private NativeMethods.Rect _edgeAnimationFrom;
    private NativeMethods.Rect _edgeAnimationTo;
    private DateTime _edgeAnimationStarted;
    private bool _edgeAnimationExpanding;
    private bool _edgeAnimationHasTarget;
    private bool _floatingFallbackActive;

    /// <summary>
    /// 在宿主模式重建前恢复浮动窗口的正常坐标，避免折叠动画留下屏幕外坐标被保存到任务栏模式。
    /// Restores the normal floating coordinates before host recreation so a collapsed animation cannot persist an off-screen taskbar position.
    /// </summary>
    private void PrepareHostModeTransition()
    {
        if (_windowSettings.HostMode != WindowHostMode.Floating)
        {
            return;
        }

        _edgeAnimationTimer.Stop();
        _edgeAnimationHasTarget = false;
        _floatingEdge = 0;
        _expandedEdge = 0;
        _isExpanded = true;
        UpdateEdgeCollapseIndicator(visible: false);
        SetPlayerContentVisibility(visible: true);

        if (_windowHandle == nint.Zero ||
            _taskbarHostService is null ||
            !NativeMethods.GetWindowRect(_windowHandle, out var rect))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(
            _windowHandle,
            NativeMethods.MonitorDefaultToNearest);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        var left = Math.Clamp(
            _floatingNormalLeft ?? rect.Left,
            info.WorkArea.Left,
            Math.Max(info.WorkArea.Left, info.WorkArea.Right - width));
        var top = Math.Clamp(
            _floatingNormalTop ?? rect.Top,
            info.WorkArea.Top,
            Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - height));
        _floatingNormalLeft = left;
        _floatingNormalTop = top;
        _windowSettings = _windowSettings with
        {
            FloatingLeft = left,
            FloatingTop = top
        };
        if (!_taskbarHostService.Position(
                left,
                top,
                width,
                height,
                visible: true,
                topmost: _windowSettings.AlwaysOnTop,
                refresh: false))
        {
            DiagnosticsLogService.Write(
                "host-mode-transition-position-failed",
                details: $"Left={left};Top={top};Width={width};Height={height}");
        }
    }

    private void PositionFloatingWindow(bool force)
    {
        if (_windowHandle == nint.Zero || _taskbarHostService is null)
        {
            return;
        }

        if (_floatingFallbackActive && !force)
        {
            return;
        }

        var verticalLayout = _windowSettings.LayoutMode == PlayerLayoutMode.Vertical;
        ApplyPlayerLayout(verticalLayout);
        CollapseWhenPointerLeavesWindow();
        if (!_windowSettings.EdgeAutoCollapse &&
            (_floatingEdge != 0 || _expandedEdge != 0))
        {
            _edgeAnimationTimer.Stop();
            _edgeAnimationHasTarget = false;
            _floatingEdge = 0;
            _expandedEdge = 0;
            UpdateEdgeCollapseIndicator(visible: false);
            force = true;
        }
        var left = _windowSettings.FloatingLeft ?? _floatingNormalLeft;
        var top = _windowSettings.FloatingTop ?? _floatingNormalTop;
        if ((!left.HasValue || !top.HasValue) &&
            NativeMethods.GetWindowRect(_windowHandle, out var currentRect))
        {
            left ??= currentRect.Left;
            top ??= currentRect.Top;
        }

        var monitor = left.HasValue && top.HasValue
            ? NativeMethods.MonitorFromPoint(
                new NativeMethods.Point { X = left.Value, Y = top.Value },
                2)
            : NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var desktopBounds = monitorInfo.WorkArea;
        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var playerScale = CalculateFloatingPlayerScale(desktopBounds, scale);
        ApplyPlayerScale(playerScale);
        var width = Math.Clamp(
            (int)Math.Ceiling(PlayerRoot.Width * playerScale.X * scale),
            1,
            desktopBounds.Width);
        var height = Math.Clamp(
            (int)Math.Ceiling(PlayerRoot.Height * playerScale.Y * scale),
            1,
            desktopBounds.Height);
        var collapsedInsets = ResolveCollapsedActiveEdgeInsets();
        var collapsedLeft = (int)Math.Round(collapsedInsets.Left * playerScale.X * scale);
        var collapsedTop = (int)Math.Round(collapsedInsets.Top * playerScale.Y * scale);
        var collapsedRight = (int)Math.Round(collapsedInsets.Right * playerScale.X * scale);
        var collapsedBottom = (int)Math.Round(collapsedInsets.Bottom * playerScale.Y * scale);
        Height = PlayerRoot.Height * playerScale.Y;
        left ??= desktopBounds.Left + 16 - collapsedLeft;
        top ??= desktopBounds.Bottom - height - 16 + collapsedBottom;
        var sizeChanged = _lastFloatingWidth > 0 &&
            (_lastFloatingWidth != width || _lastFloatingHeight != height);
        var anchoredEdge = _floatingEdge != 0 ? _floatingEdge : _expandedEdge;
        if (sizeChanged)
        {
            force = true;
        }

        if (sizeChanged && anchoredEdge != 0)
        {
            // 组件改变尺寸时按新尺寸重算贴边坐标，避免右侧或底部留下旧宽高造成的空白。
            // Re-anchor with the new size so stale width or height cannot leave gaps at the right or bottom edge.
            var normalLeft = anchoredEdge switch
            {
                1 => desktopBounds.Left,
                2 => desktopBounds.Right - width,
                _ => Math.Clamp(
                    _floatingNormalLeft ?? left.Value,
                    desktopBounds.Left,
                    desktopBounds.Right - width)
            };
            var normalTop = anchoredEdge switch
            {
                3 => desktopBounds.Top,
                4 => desktopBounds.Bottom - height,
                _ => Math.Clamp(
                    _floatingNormalTop ?? top.Value,
                    desktopBounds.Top,
                    desktopBounds.Bottom - height)
            };

            if (_edgeAnimationTimer.IsEnabled && _edgeAnimationHasTarget)
            {
                _edgeAnimationTimer.Stop();
                _edgeAnimationHasTarget = false;
                if (_edgeAnimationExpanding)
                {
                    _floatingEdge = 0;
                    _expandedEdge = anchoredEdge;
                    UpdateEdgeCollapseIndicator(visible: false);
                }
                else
                {
                    _floatingEdge = anchoredEdge;
                    _expandedEdge = 0;
                    UpdateEdgeCollapseIndicator(visible: true);
                    SetPlayerContentVisibility(visible: false);
                }
            }

            left = normalLeft;
            top = normalTop;
            _floatingNormalLeft = normalLeft;
            _floatingNormalTop = normalTop;
            _windowSettings = _windowSettings with
            {
                FloatingLeft = normalLeft,
                FloatingTop = normalTop
            };
            SaveWindowSettings(showError: false);
        }

        if (_floatingEdge == 0)
        {
            // 折叠触发条仍留在窗口中接收鼠标，但它不参与工作区碰撞；长条本体可以真正贴到四个屏幕边缘。
            // Collapsed triggers remain in the window for pointer activation but do not participate in work-area collision, so the strip body can reach every screen edge.
            left = Math.Clamp(
                left.Value,
                desktopBounds.Left - collapsedLeft,
                desktopBounds.Right - width + collapsedRight);
            top = Math.Clamp(
                top.Value,
                desktopBounds.Top - collapsedTop,
                desktopBounds.Bottom - height + collapsedBottom);
            _floatingNormalLeft = left;
            _floatingNormalTop = top;
            if (_expandedEdge != 0)
            {
                _windowSettings = _windowSettings with
                {
                    FloatingLeft = left,
                    FloatingTop = top
                };
                SaveWindowSettings(showError: false);
            }
        }
        else
        {
            left = _floatingEdge == 1
                ? desktopBounds.Left - width + EdgeVisiblePixels
                : _floatingEdge == 2
                    ? desktopBounds.Right - EdgeVisiblePixels
                    : left;
            top = _floatingEdge == 3
                ? desktopBounds.Top - height + EdgeVisiblePixels
                : _floatingEdge == 4
                    ? desktopBounds.Bottom - EdgeVisiblePixels
                    : top;
        }

        var layoutTargetLeft = 0;
        var layoutTargetTop = 0;
        var preservingLayoutBody = _floatingEdge == 0 &&
            TryResolveLayoutBodyTarget(
                playerScale.X * scale,
                playerScale.Y * scale,
                out layoutTargetLeft,
                out layoutTargetTop);
        if (preservingLayoutBody)
        {
            _layoutBodyCorrectionX = layoutTargetLeft - left.Value;
            _layoutBodyCorrectionY = layoutTargetTop - top.Value;
            left = layoutTargetLeft;
            top = layoutTargetTop;
        }
        else if (_floatingEdge == 0 &&
                 (_layoutBodyCorrectionX != 0 || _layoutBodyCorrectionY != 0))
        {
            left += _layoutBodyCorrectionX;
            top += _layoutBodyCorrectionY;
        }

        ConfigureFloatingPopupPlacement(
            desktopBounds,
            preservingLayoutBody ? left.Value : _floatingNormalLeft ?? left.Value,
            preservingLayoutBody ? top.Value : _floatingNormalTop ?? top.Value,
            width,
            height,
            verticalLayout);

        _lastFloatingWidth = width;
        _lastFloatingHeight = height;

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            force = true;
        }

        if (!_taskbarHostService.IsFloating &&
            !_taskbarHostService.SetFloating(true))
        {
            DiagnosticsLogService.Write("floating-window-detach-failed");
            RevealFloatingFallback();
            return;
        }

        if (!_edgeAnimationTimer.IsEnabled &&
            (force || _lastPositionLeft != left || _lastPositionTop != top))
        {
            _lastPositionLeft = left;
            _lastPositionTop = top;
            if (!_taskbarHostService.Position(
                    left.Value,
                    top.Value,
                    width,
                    height,
                    visible: true,
                    topmost: _windowSettings.AlwaysOnTop,
                    inputRects: BuildWindowInputRects(playerScale.X * scale)))
            {
                DiagnosticsLogService.Write("floating-window-position-failed");
                RevealFloatingFallback();
                return;
            }
        }

        _floatingFallbackActive = false;
        RevealAfterPlacement();
    }

    private void RevealFloatingFallback()
    {
        if (_floatingFallbackActive)
        {
            return;
        }

        _floatingFallbackActive = true;
        Visibility = Visibility.Visible;
        RevealAfterPlacement();
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwShowNoActivate);
    }

    private (double X, double Y) CalculateFloatingPlayerScale(
        NativeMethods.Rect desktopBounds,
        double dpiScale)
    {
        var requestedScale = _windowSettings.ThicknessScalePercent / 100d;
        var maximumX = desktopBounds.Width / (PlayerRoot.Width * dpiScale);
        var maximumY = desktopBounds.Height / (PlayerRoot.Height * dpiScale);
        var scale = Math.Clamp(
            Math.Min(requestedScale, Math.Min(maximumX, maximumY)),
            0.1,
            1.25);
        return (scale, scale);
    }

    private void ConfigureFloatingPopupPlacement(
        NativeMethods.Rect desktopBounds,
        int left,
        int top,
        int width,
        int height,
        bool verticalLayout)
    {
        if (verticalLayout)
        {
            var openToRight = left + width / 2 <=
                desktopBounds.Left + desktopBounds.Width / 2;
            SetPopupPlacement(
                openToRight ? PlacementMode.Right : PlacementMode.Left,
                openToRight ? 7 : -7,
                0);
            return;
        }

        var openDownward = top + height / 2 <=
            desktopBounds.Top + desktopBounds.Height / 2;
        SetPopupPlacement(
            openDownward ? PlacementMode.Bottom : PlacementMode.Top,
            0,
            openDownward ? 7 : -7);
    }

    private void UpdateFloatingEdgeCollapse()
    {
        if (_windowSettings.HostMode != WindowHostMode.Floating ||
            !_windowSettings.EdgeAutoCollapse ||
            _windowHandle == nint.Zero ||
            _isDragging ||
            _isMenuOpen ||
            _edgeAnimationTimer.IsEnabled ||
            !NativeMethods.GetWindowRect(_windowHandle, out var rect) ||
            !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        const int edgeTolerance = 10;
        var desktopBounds = info.WorkArea;
        if (_floatingEdge != 0)
        {
            var normalLeft = _floatingNormalLeft ?? rect.Left;
            var normalTop = _floatingNormalTop ?? rect.Top;
            var nearHorizontalSpan = cursor.X >= normalLeft - EdgeActivationSpanPadding &&
                cursor.X < normalLeft + rect.Width + EdgeActivationSpanPadding;
            var nearVerticalSpan = cursor.Y >= normalTop - EdgeActivationSpanPadding &&
                cursor.Y < normalTop + rect.Height + EdgeActivationSpanPadding;
            var nearEdge = _floatingEdge switch
            {
                1 => cursor.X >= desktopBounds.Left - EdgeActivationDistance &&
                    cursor.X <= desktopBounds.Left + EdgeActivationDistance && nearVerticalSpan,
                2 => cursor.X >= desktopBounds.Right - EdgeActivationDistance &&
                    cursor.X <= desktopBounds.Right + EdgeActivationDistance && nearVerticalSpan,
                3 => cursor.Y >= desktopBounds.Top - EdgeActivationDistance &&
                    cursor.Y <= desktopBounds.Top + EdgeActivationDistance && nearHorizontalSpan,
                _ => cursor.Y >= desktopBounds.Bottom - EdgeActivationDistance &&
                    cursor.Y <= desktopBounds.Bottom + EdgeActivationDistance && nearHorizontalSpan
            };
            if (nearEdge)
            {
                StartEdgeAnimation(expanding: true, rect, desktopBounds);
            }

            return;
        }

        if (_expandedEdge != 0)
        {
            const int expandedProximity = 64;
            var nearExpandedWindow = cursor.X >= rect.Left - expandedProximity &&
                cursor.X < rect.Right + expandedProximity &&
                cursor.Y >= rect.Top - expandedProximity &&
                cursor.Y < rect.Bottom + expandedProximity;
            if (nearExpandedWindow)
            {
                return;
            }

            _floatingEdge = _expandedEdge;
            _expandedEdge = 0;
            StartEdgeAnimation(expanding: false, rect, desktopBounds);
            return;
        }

        var touchesLeft = rect.Left <= desktopBounds.Left + edgeTolerance;
        var touchesRight = rect.Right >= desktopBounds.Right - edgeTolerance;
        var touchesTop = rect.Top <= desktopBounds.Top + edgeTolerance;
        var touchesBottom = rect.Bottom >= desktopBounds.Bottom - edgeTolerance;
        var edge = _isVerticalLayout
            ? touchesLeft ? 1 :
                touchesRight ? 2 :
                touchesTop ? 3 :
                touchesBottom ? 4 : 0
            : touchesTop ? 3 :
                touchesBottom ? 4 :
                touchesLeft ? 1 :
                touchesRight ? 2 : 0;
        if (edge == 0 ||
            (cursor.X >= rect.Left && cursor.X < rect.Right &&
                cursor.Y >= rect.Top && cursor.Y < rect.Bottom))
        {
            return;
        }

        _floatingNormalLeft = rect.Left;
        _floatingNormalTop = rect.Top;
        _windowSettings = _windowSettings with
        {
            FloatingLeft = rect.Left,
            FloatingTop = rect.Top
        };
        _floatingEdge = edge;
        StartEdgeAnimation(expanding: false, rect, desktopBounds);
    }

    private void OnEdgeHoverTimerTick(object? sender, EventArgs e)
    {
        // 整窗贴边折叠已由布局中的边缘折叠容器取代；保留计时器回调签名以兼容旧生命周期清理，但不再触发整窗移动。
        // Whole-window edge collapse is replaced by layout edge containers; keep the callback shape for legacy lifecycle cleanup without moving the entire window.
    }

    private void StartEdgeAnimation(
        bool expanding,
        NativeMethods.Rect currentRect,
        NativeMethods.Rect desktopBounds)
    {
        if (_taskbarHostService is null || _floatingEdge == 0)
        {
            return;
        }

        var normalLeft = Math.Clamp(
            _floatingNormalLeft ?? currentRect.Left,
            desktopBounds.Left,
            desktopBounds.Right - currentRect.Width);
        var normalTop = Math.Clamp(
            _floatingNormalTop ?? currentRect.Top,
            desktopBounds.Top,
            desktopBounds.Bottom - currentRect.Height);
        var collapsedLeft = _floatingEdge == 1
            ? desktopBounds.Left - currentRect.Width + EdgeVisiblePixels
            : _floatingEdge == 2
                ? desktopBounds.Right - EdgeVisiblePixels
                : normalLeft;
        var collapsedTop = _floatingEdge == 3
            ? desktopBounds.Top - currentRect.Height + EdgeVisiblePixels
            : _floatingEdge == 4
                ? desktopBounds.Bottom - EdgeVisiblePixels
                : normalTop;

        _edgeAnimationFrom = currentRect;
        _edgeAnimationTo = new NativeMethods.Rect
        {
            Left = expanding ? normalLeft : collapsedLeft,
            Top = expanding ? normalTop : collapsedTop,
            Right = (expanding ? normalLeft : collapsedLeft) + currentRect.Width,
            Bottom = (expanding ? normalTop : collapsedTop) + currentRect.Height
        };
        _edgeAnimationStarted = DateTime.UtcNow;
        _edgeAnimationExpanding = expanding;
        _edgeAnimationHasTarget = true;
        if (expanding)
        {
            SetPlayerContentVisibility(visible: true);
        }
        UpdateEdgeCollapseIndicator(visible: !expanding);
        _edgeAnimationTimer.Stop();
        _edgeAnimationTimer.Start();
    }

    private void OnEdgeAnimationTick(object? sender, EventArgs e)
    {
        if (!_edgeAnimationHasTarget || _taskbarHostService is null)
        {
            _edgeAnimationTimer.Stop();
            return;
        }

        var elapsed = (DateTime.UtcNow - _edgeAnimationStarted).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / EdgeAnimationDurationMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var left = (int)Math.Round(_edgeAnimationFrom.Left +
            (_edgeAnimationTo.Left - _edgeAnimationFrom.Left) * eased);
        var top = (int)Math.Round(_edgeAnimationFrom.Top +
            (_edgeAnimationTo.Top - _edgeAnimationFrom.Top) * eased);
        if (!_taskbarHostService.Position(
                left,
                top,
                _edgeAnimationTo.Width,
                _edgeAnimationTo.Height,
                visible: true,
                topmost: _windowSettings.AlwaysOnTop,
                refresh: false))
        {
            _edgeAnimationTimer.Stop();
            _edgeAnimationHasTarget = false;
            DiagnosticsLogService.Write("edge-animation-position-failed");
            ScheduleEnvironmentRecovery("edge-animation-position-failure");
            return;
        }
        _lastPositionLeft = left;
        _lastPositionTop = top;

        if (progress < 1)
        {
            return;
        }

        _edgeAnimationTimer.Stop();
        _edgeAnimationHasTarget = false;
        _taskbarHostService.Redraw();
        if (_edgeAnimationExpanding)
        {
            _expandedEdge = _floatingEdge;
            _floatingEdge = 0;
            UpdateEdgeCollapseIndicator(visible: false);
        }
        else
        {
            _expandedEdge = 0;
            UpdateEdgeCollapseIndicator(visible: true);
            SetPlayerContentVisibility(visible: false);
        }
    }

    private void UpdateEdgeCollapseIndicator(bool visible)
    {
        EdgeCollapseIndicator.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!visible)
        {
            SetPlayerContentVisibility(visible: true);
            return;
        }

        var horizontalEdge = _floatingEdge is 3 or 4;
        EdgeCollapseIndicator.Width = horizontalEdge ? 56 : 4;
        EdgeCollapseIndicator.Height = horizontalEdge
            ? 4
            : _isVerticalLayout ? 72 : 38;
        EdgeCollapseIndicator.HorizontalAlignment = _floatingEdge == 1
            ? HorizontalAlignment.Right
            : _floatingEdge == 2
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
        EdgeCollapseIndicator.VerticalAlignment = _floatingEdge == 3
            ? VerticalAlignment.Bottom
            : _floatingEdge == 4
                ? VerticalAlignment.Top
                : VerticalAlignment.Center;
    }

}
