// 单个悬浮宿主持有折叠状态、采样、计时器及原生命中区域；停止时取消采样，关闭时释放。
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private readonly DispatcherTimer _edgeHoverTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _edgeMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Border _edgeIndicator = new() { CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private readonly AdaptiveForegroundSamplingSession _edgeContrastSampling;
    private PlayerForegroundDecision? _edgeForegroundDecision;
    private readonly ColorTransitionBrush _edgeLineColor = new();
    private readonly ColorTransitionBrush _edgeOutlineColor = new();
    private bool _edgeContrastReady;
    private long _edgeContrastRefreshTick;
    private FloatingBarDockEdge _dockEdge;
    private Rect _normalBoundsDip;
    private double _edgeProgress;
    private double _edgeTarget;
    private double _edgeFrom;
    private long _edgeMotionTick;
    private bool _edgeObservedExpanded;
    private bool _edgeRegionApplied;
    private Int32Rect? _edgeClipBoundsPx;
    private bool _edgeFaulted;
    private bool _edgeBodySuspended;
    private int _edgeHoldCount;
    private int _edgeHoldGeneration;
    private bool IsEdgeConcealed => _edgeProgress > 0 || _edgeTarget > 0;
    private bool IsEdgeFullyCollapsed => _edgeProgress >= 1 && _edgeTarget == 1;
    private Size WorkAreaDip => new(PlacementArea.Width / Dpi, PlacementArea.Height / Dpi);
    internal FloatingBarDockEdge DockEdge => _dockEdge;

    private void InitializeEdgeCollapse()
    {
        ApplyEdgeContrast(null);
        _edgeIndicator.SetResourceReference(ToolTipProperty, "Loc.Placement.Floating.EdgeCollapse.Reveal");
        var surface = new Grid { Background = System.Windows.Media.Brushes.Transparent };
        surface.Children.Add(_mediaControl);
        surface.Children.Add(_edgeIndicator);
        Content = surface;
        _edgeHoverTimer.Tick += ObserveEdgePointer;
        _edgeMotionTimer.Tick += AdvanceEdgeMotion;
    }

    private Rect ResolveEdgeBounds(Rect normal)
    {
        if (!_dragging && SettingsManager.Current.FloatingBar.EdgeCollapseEnabled)
        {
            if (_dockEdge == FloatingBarDockEdge.None)
                _dockEdge = FloatingBarEdgeCollapsePolicy.DetectEdge(normal, WorkAreaDip, IsVerticalPresentation);
            normal = FloatingBarEdgeCollapsePolicy.Anchor(normal, WorkAreaDip, _dockEdge);
            _centerPx = new Point(PlacementArea.Left + (normal.Left + normal.Width / 2) * Dpi,
                PlacementArea.Top + (normal.Top + normal.Height / 2) * Dpi);
        }
        _normalBoundsDip = normal;
        var collapsed = FloatingBarEdgeCollapsePolicy.CollapsedBounds(normal, _dockEdge);
        return new Rect(normal.Left + (collapsed.Left - normal.Left) * _edgeProgress,
            normal.Top + (collapsed.Top - normal.Top) * _edgeProgress, normal.Width, normal.Height);
    }

    private void UpdateEdgeObservation()
    {
        var enabled = !_edgeFaulted && IsOperational && IsVisible && SettingsManager.Current.FloatingBar.EdgeCollapseEnabled &&
            _dockEdge != FloatingBarDockEdge.None && !_dragging;
        if (enabled) _edgeHoverTimer.Start();
        else _edgeHoverTimer.Stop();
    }

    private void ObserveEdgePointer(object? sender, EventArgs args)
    {
        UpdateEdgeObservation();
        if (!_edgeHoverTimer.IsEnabled) return;
        RefreshEdgeContrast();
        if (_edgeHoldCount > 0 || _isFullPanelOpen || _compactFlyout.IsVisible || _dragPending)
        {
            SetEdgeTarget(collapse: false);
            return;
        }
        // 在其它窗口按住鼠标不能唤醒已收起的媒体栏；本栏按压也不触发自动收起。
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) return;
        if (!GetCursorPos(out var cursor)) return;
        var pointer = new Point((cursor.X - PlacementArea.Left) / Dpi, (cursor.Y - PlacementArea.Top) / Dpi);
        var nearTrigger = FloatingBarEdgeCollapsePolicy.ShouldExpand(_normalBoundsDip, WorkAreaDip, pointer, _dockEdge);
        if (_edgeTarget == 1)
        {
            if (nearTrigger) SetEdgeTarget(collapse: false);
        }
        else if (!nearTrigger && FloatingBarEdgeCollapsePolicy.ShouldCollapse(_normalBoundsDip, pointer, _edgeObservedExpanded))
            SetEdgeTarget(collapse: true);
    }

    private void SetEdgeTarget(bool collapse, bool instant = false)
    {
        if (_isClosing || collapse && (_edgeFaulted || _dockEdge == FloatingBarDockEdge.None)) return;
        var target = collapse ? 1d : 0d;
        if (_edgeTarget == target && !instant) return;
        if (!collapse && _edgeTarget == 1) _edgeObservedExpanded = true;
        _edgeTarget = target;
        _edgeFrom = _edgeProgress;
        _edgeMotionTick = Stopwatch.GetTimestamp();
        _externalFullPanelHandle?.Dismiss(immediate: true);
        if (collapse)
        {
            PersistInferredDockEdge();
            ClearInteraction();
            PlacementTransitionStarted?.Invoke(this, EventArgs.Empty);
        }
        if (instant || !MotionPolicy.ResolveCurrent().UseTransitions || _edgeFrom == target)
        {
            _edgeMotionTimer.Stop();
            _edgeProgress = target;
        }
        else _edgeMotionTimer.Start();
        UpdateEdgePresentation();
        ApplyBounds();
    }

    private void PersistInferredDockEdge()
    {
        if (_savedPosition is not { DockEdge: FloatingBarDockEdge.None } || _dockEdge == FloatingBarDockEdge.None ||
            !string.Equals(TargetMonitorDeviceId, Monitor.DeviceId, StringComparison.OrdinalIgnoreCase)) return;
        // 旧记录只在首次实际收起时补齐附着信息，保存的仍是正常中心，不是动画窗口坐标。
        _savedPosition = new FloatingBarPosition(TargetMonitorDeviceId,
            (_centerPx.X - Monitor.WorkArea.Left) / Dpi, (_centerPx.Y - Monitor.WorkArea.Top) / Dpi)
        { DockEdge = _dockEdge };
        _savingPosition = true;
        try
        {
            var settings = SettingsManager.Current.FloatingBar;
            SettingsManager.Current.FloatingBar = settings with
            {
                Positions = settings.Positions.Where(p => !string.Equals(p.MonitorDeviceId, TargetMonitorDeviceId, StringComparison.OrdinalIgnoreCase))
                    .Append(_savedPosition).ToArray()
            };
        }
        finally { _savingPosition = false; }
    }

    private void AdvanceEdgeMotion(object? sender, EventArgs args)
    {
        if (_isClosing) return;
        if (!IsOperational) { StopEdgeCollapse(); return; }
        var duration = MotionPolicy.ResolveCurrent().StandardDuration.TotalMilliseconds;
        var progress = duration <= 0 ? 1 : Math.Clamp(Stopwatch.GetElapsedTime(_edgeMotionTick).TotalMilliseconds / duration, 0, 1);
        _edgeProgress = _edgeFrom + (_edgeTarget - _edgeFrom) * (1 - Math.Pow(1 - progress, 3));
        if (progress >= 1) { _edgeProgress = _edgeTarget; _edgeMotionTimer.Stop(); }
        UpdateEdgePresentation();
        ApplyBounds();
    }

    private void UpdateEdgePresentation()
    {
        _mediaControl.IsHitTestVisible = !IsEdgeConcealed;
        _edgeIndicator.Visibility = IsEdgeConcealed && _edgeContrastReady ? Visibility.Visible : Visibility.Collapsed;
        var horizontal = _dockEdge is FloatingBarDockEdge.Top or FloatingBarDockEdge.Bottom;
        _edgeIndicator.Width = Math.Min(_normalBoundsDip.Width, horizontal ? 56 : 4);
        _edgeIndicator.Height = Math.Min(_normalBoundsDip.Height, horizontal ? 4 : IsVerticalPresentation ? 72 : 38);
        _edgeIndicator.HorizontalAlignment = _dockEdge == FloatingBarDockEdge.Left ? HorizontalAlignment.Right :
            _dockEdge == FloatingBarDockEdge.Right ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _edgeIndicator.VerticalAlignment = _dockEdge == FloatingBarDockEdge.Top ? VerticalAlignment.Bottom :
            _dockEdge == FloatingBarDockEdge.Bottom ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (_edgeBodySuspended == IsEdgeFullyCollapsed) return;
        _edgeContrastReady = false;
        _edgeIndicator.Visibility = Visibility.Collapsed;
        _edgeLineColor.Finish();
        _edgeOutlineColor.Finish();
        _edgeContrastSampling.Invalidate(clearDecision: false);
        _edgeContrastRefreshTick = 0;
        _edgeBodySuspended = IsEdgeFullyCollapsed;
        _mediaControl.Visibility = _edgeBodySuspended ? Visibility.Hidden : Visibility.Visible;
        _mediaControl.ApplyHostVisibilitySuspension(_edgeBodySuspended || !IsOperational);
        SynchronizeResources();
        if (_edgeBodySuspended)
        {
            _backgroundTimer.Stop();
            _foregroundSamplingSession.Invalidate(clearDecision: false);
        }
        else if (IsOperational) _backgroundTimer.Start();
    }

    private void ApplyEdgeContrast(PlayerForegroundDecision? decision)
    {
        if (_isClosing) return;
        if (decision is not null) _edgeForegroundDecision = decision;
        var brushes = HandleIndicatorAppearance.Resolve(_edgeForegroundDecision);
        var duration = ColorTransitionBrush.ResolveDuration(_edgeContrastReady && IsEdgeFullyCollapsed && IsVisible && decision is not null);
        _edgeIndicator.Background = _edgeLineColor.Apply(brushes.Line, duration);
        _edgeIndicator.BorderBrush = _edgeOutlineColor.Apply(brushes.Outline, duration);
        if (decision is not null) _edgeContrastReady = true;
        _edgeIndicator.Visibility = IsEdgeConcealed && _edgeContrastReady ? Visibility.Visible : Visibility.Collapsed;
    }

    private Int32Rect? GetEdgeContrastBounds()
    {
        if (!IsOperational || !IsVisible || !IsEdgeFullyCollapsed || _dockEdge == FloatingBarDockEdge.None) return null;
        // 在放置区域内、折叠窗口之外取色，避开横条与轮廓，防止颜色自反馈和跨屏采样。
        var area = PlacementArea;
        var inset = (FloatingBarEdgeCollapsePolicy.VisibleStripDip + 1) * Dpi;
        var horizontal = _dockEdge is FloatingBarDockEdge.Top or FloatingBarDockEdge.Bottom;
        var length = (horizontal ? _edgeIndicator.Width : _edgeIndicator.Height) * Dpi;
        var bounds = horizontal
            ? new Rect(_centerPx.X - length / 2,
                _dockEdge == FloatingBarDockEdge.Top ? area.Top + inset : area.Bottom - inset - Dpi, length, Dpi)
            : new Rect(_dockEdge == FloatingBarDockEdge.Left ? area.Left + inset : area.Right - inset - Dpi,
                _centerPx.Y - length / 2, Dpi, length);
        bounds.Intersect(new Rect(area.Left, area.Top, area.Width, area.Height));
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return null;
        return new Int32Rect((int)Math.Ceiling(bounds.Left), (int)Math.Ceiling(bounds.Top),
            Math.Max(1, (int)Math.Floor(bounds.Width)), Math.Max(1, (int)Math.Floor(bounds.Height)));
    }

    private void RefreshEdgeContrast()
    {
        if (!IsEdgeFullyCollapsed || !IsOperational || !IsVisible ||
            Stopwatch.GetElapsedTime(_edgeContrastRefreshTick) < TimeSpan.FromMilliseconds(250)) return;
        // 首次取色前不绘制旧色；一个刷新周期仍无有效样本时使用已有回退色，不能丢失位置提示。
        if (_edgeContrastRefreshTick != 0) _edgeContrastReady = true;
        _edgeContrastRefreshTick = Stopwatch.GetTimestamp();
        ApplyEdgeContrast(null);
        if (SystemParameters.HighContrast) _edgeContrastSampling.Invalidate(clearDecision: false);
        else _edgeContrastSampling.RequestRefresh();
    }

    private bool ApplyEdgeRegion(Int32Rect physical)
    {
        if (!IsEdgeConcealed)
        {
            if (!_edgeRegionApplied) return true;
            if (!SetWindowRgn(_handle, nint.Zero, true)) return false;
            _edgeRegionApplied = false;
            _edgeClipBoundsPx = null;
            return true;
        }
        // 裁剪区域跟随放置区域：覆盖任务栏时折叠动画会滑出屏幕边缘，
        // 仍按工作区求交集会把整段动画判成不可见，进而误报折叠失败。
        var visible = Rect.Intersect(new Rect(physical.X, physical.Y, physical.Width, physical.Height), PlacementArea);
        if (visible.IsEmpty) return false;
        var clip = new Int32Rect((int)Math.Ceiling(visible.Left - physical.X), (int)Math.Ceiling(visible.Top - physical.Y),
            (int)Math.Floor(visible.Right) - (int)Math.Ceiling(visible.Left), (int)Math.Floor(visible.Bottom) - (int)Math.Ceiling(visible.Top));
        if (_edgeRegionApplied && _edgeClipBoundsPx == clip) return true;
        var region = CreateRectRgn(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height);
        if (region == nint.Zero) return false;
        // SetWindowRgn 成功后区域归系统；失败时仍由调用方释放。
        if (!SetWindowRgn(_handle, region, true)) { DeleteObject(region); return false; }
        _edgeRegionApplied = true;
        _edgeClipBoundsPx = clip;
        return true;
    }

    private void FailEdgePlacement()
    {
        _edgeFaulted = true;
        _log.Warn("FloatingBar", "贴边折叠定位或裁剪失败，恢复正常位置");
        StopEdgeCollapse();
        _nativeBoundsPx = null;
        ApplyBounds();
    }

    private void StopEdgeCollapse()
    {
        _edgeHoverTimer.Stop();
        _edgeMotionTimer.Stop();
        _edgeProgress = _edgeTarget = 0;
        _edgeObservedExpanded = false;
        _edgeHoldGeneration++;
        _edgeHoldCount = 0;
        // 可见窗口先回到正常坐标，再由 ApplyBounds 解除裁剪，避免在相邻屏幕闪现正文。
        if (!IsVisible && _edgeRegionApplied && _handle != nint.Zero && SetWindowRgn(_handle, nint.Zero, true)) _edgeRegionApplied = false;
        _edgeClipBoundsPx = null;
        UpdateEdgePresentation();
    }

    /// <summary>打开本宿主菜单或准备异步菜单期间保持展开；宿主停止后旧租约失效。</summary>
    internal IDisposable KeepEdgeExpanded()
    {
        Dispatcher.VerifyAccess();
        var hold = new EdgeInteractionHold(this, _edgeHoldGeneration);
        if (!_isClosing) { _edgeHoldCount++; SetEdgeTarget(collapse: false); }
        return hold;
    }

    private sealed class EdgeInteractionHold(FloatingBarWindow owner, int generation) : IDisposable
    {
        private FloatingBarWindow? _owner = owner;
        public void Dispose()
        {
            var host = Interlocked.Exchange(ref _owner, null);
            if (host is not null && !host._isClosing && generation == host._edgeHoldGeneration && host._edgeHoldCount > 0)
                host._edgeHoldCount--;
        }
    }
}
