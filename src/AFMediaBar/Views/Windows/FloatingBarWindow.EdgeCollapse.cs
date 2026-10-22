// 单个悬浮宿主持有折叠状态、计时器及原生命中区域；正常位置与动画帧分离。
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private readonly DispatcherTimer _edgeHoverTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _edgeMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Border _edgeIndicator = new() { CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed };
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
    private Size WorkAreaDip => new(Monitor.WorkArea.Width / Dpi, Monitor.WorkArea.Height / Dpi);
    internal FloatingBarDockEdge DockEdge => _dockEdge;

    private void InitializeEdgeCollapse()
    {
        _edgeIndicator.SetResourceReference(Border.BackgroundProperty, "AfAccentBrush");
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
            _centerPx = new Point(Monitor.WorkArea.Left + (normal.Left + normal.Width / 2) * Dpi,
                Monitor.WorkArea.Top + (normal.Top + normal.Height / 2) * Dpi);
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
        if (_edgeHoldCount > 0 || _isFullPanelOpen || _compactFlyout.IsVisible || _dragPending)
        {
            SetEdgeTarget(collapse: false);
            return;
        }
        // 在其它窗口按住鼠标不能唤醒已收起的媒体栏；本栏按压也不触发自动收起。
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) return;
        if (!GetCursorPos(out var cursor)) return;
        var pointer = new Point((cursor.X - Monitor.WorkArea.Left) / Dpi, (cursor.Y - Monitor.WorkArea.Top) / Dpi);
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
        _edgeIndicator.Visibility = IsEdgeConcealed ? Visibility.Visible : Visibility.Collapsed;
        var horizontal = _dockEdge is FloatingBarDockEdge.Top or FloatingBarDockEdge.Bottom;
        _edgeIndicator.Width = Math.Min(_normalBoundsDip.Width, horizontal ? 56 : 4);
        _edgeIndicator.Height = Math.Min(_normalBoundsDip.Height, horizontal ? 4 : IsVerticalPresentation ? 72 : 38);
        _edgeIndicator.HorizontalAlignment = _dockEdge == FloatingBarDockEdge.Left ? HorizontalAlignment.Right :
            _dockEdge == FloatingBarDockEdge.Right ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _edgeIndicator.VerticalAlignment = _dockEdge == FloatingBarDockEdge.Top ? VerticalAlignment.Bottom :
            _dockEdge == FloatingBarDockEdge.Bottom ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (_edgeBodySuspended == IsEdgeFullyCollapsed) return;
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
        var visible = Rect.Intersect(new Rect(physical.X, physical.Y, physical.Width, physical.Height), Monitor.WorkArea);
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
