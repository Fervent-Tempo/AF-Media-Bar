// 窗口拥有局部尺寸手势和捕获，设置提交交给注入的呈现 ViewModel。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using AFMediaBar.Resources;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarFullPanelWindow
{
    private double _requestedScale = 1;
    private double _effectiveScale = 1;
    private double _resizeInitialScale;
    private double _resizeSavedScale;
    private Size _resizeBaseSize;
    private Point _resizeOriginScreen;
    private bool _isResizing;
    private bool _positionQueued;
    private Rect _panelWorkArea;
    private Rect _resizeInitialBounds;
    private (Rect Bounds, string Edge, FullPanelPlacementSide Side)? _resizePlacement;
    private (Rect Bounds, string Edge, FullPanelPlacementSide Side)? _resizeSavedPlacement;
    private string _resizeEdge = string.Empty;
    private FullPanelPlacementSide _resolvedSide;

    private IEnumerable<MetricPresenter> PanelPresenters() => PanelMetrics.Presenters;

    private void ApplyChartStyles(MetricVisualSettings visuals)
    {
        foreach (var presenter in PanelPresenters()) presenter.VisualStyle = visuals.Get(presenter.Metric);
    }

    private void ApplyScaleGeometry(Size limit)
    {
        // 窗口与内容共用一个比例，阴影留白固定，避免缩小时产生空白或错开命中区。
        var inset = Math.Min(FullPanelScalePolicy.ShadowInsetDip, Math.Min(limit.Width, limit.Height) / 4);
        var availableWidth = limit.Width - inset * 2;
        _effectiveScale = Math.Max(TaskbarFullPanelSettings.MinimumScalePercent / 100d,
            Math.Min(FullPanelScalePolicy.Clamp(_requestedScale), availableWidth / FullPanelScalePolicy.ContentWidthDip));
        PanelSizingRoot.Width = Math.Min(FullPanelScalePolicy.ContentWidthDip, availableWidth / _effectiveScale);
        PanelSizingRoot.Margin = new Thickness(inset);
        ResizeLayer.Margin = new Thickness(Math.Max(0, inset - 3));
        ContentScale.ScaleX = ContentScale.ScaleY = _effectiveScale;
        Width = PanelSizingRoot.Width * _effectiveScale + inset * 2;
        SizeToContent = SizeToContent.Height;
        Height = double.NaN;
        PanelContentScroll.MaxHeight = Math.Max(1, (limit.Height - inset * 2) / _effectiveScale - 26);
    }

    private void OnPanelContentSizeChanged(object sender, SizeChangedEventArgs e) => QueuePanelPosition();

    private void QueuePanelPosition()
    {
        if (_positionQueued || _isClosing || _isResizing || _windowPresentation.IsPreparing || !IsVisible) return;
        _positionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _positionQueued = false;
            if (!_isClosing && !_isResizing && IsVisible && _anchor is Rect anchor && _targetMonitorDeviceId is string target)
                PositionNear(anchor, target);
        }));
    }

    private void InitializeResizeGrips()
    {
        foreach (var edge in new[] { "L", "R", "T", "B", "LT", "RT", "LB", "RB" })
        {
            var thumb = new Thumb
            {
                Tag = edge,
                Focusable = false,
                IsTabStop = false,
                HorizontalAlignment = edge.Contains('L') ? HorizontalAlignment.Left : edge.Contains('R') ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = edge.Contains('T') ? VerticalAlignment.Top : edge.Contains('B') ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Cursor = edge.Length == 1 ? edge is "L" or "R" ? Cursors.SizeWE : Cursors.SizeNS : edge is "LT" or "RB" ? Cursors.SizeNWSE : Cursors.SizeNESW
            };
            if (edge.Contains('L') || edge.Contains('R')) thumb.Width = edge.Length == 2 ? 12 : 6;
            if (edge.Contains('T') || edge.Contains('B')) thumb.Height = edge.Length == 2 ? 12 : 6;
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
            thumb.DragStarted += ResizeGrip_DragStarted;
            thumb.DragDelta += ResizeGrip_DragDelta;
            thumb.DragCompleted += ResizeGrip_DragCompleted;
            ResizeLayer.Children.Add(thumb);
        }
    }

    private void UpdateResizeGrips()
    {
        foreach (Thumb thumb in ResizeLayer.Children)
            thumb.Visibility = FullPanelScalePolicy.CanResizeEdge((string)thumb.Tag, _resolvedSide) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _resizeEdge = (string)((Thumb)sender).Tag;
        if (_isClosing || !FullPanelScalePolicy.CanResizeEdge(_resizeEdge, _resolvedSide)) return;
        _isResizing = true;
        _resizeOriginScreen = PointToScreen(Mouse.GetPosition(this));
        _resizeInitialBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        _resizeSavedScale = _requestedScale;
        _resizeInitialScale = _effectiveScale;
        _resizeBaseSize = new Size(PanelRoot.ActualWidth, PanelRoot.ActualHeight);
        _resizeSavedPlacement = _resizePlacement;
        _resizePlacement = (_resizeInitialBounds, _resizeEdge, _resolvedSide);
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PanelScale.ScaleX = PanelScale.ScaleY = 1;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isResizing || _isClosing) return;
        var delta = PointToScreen(Mouse.GetPosition(this)) - _resizeOriginScreen;
        var dpi = VisualTreeHelper.GetDpi(this);
        delta = new Vector(delta.X / dpi.DpiScaleX, delta.Y / dpi.DpiScaleY);
        var limit = FullPanelScalePolicy.GetResizeLimit(_resizeInitialBounds, _panelWorkArea, _resizeEdge, _resolvedSide);
        _requestedScale = FullPanelScalePolicy.DragScale(_resizeInitialScale, delta, _resizeBaseSize, _resizeEdge, limit);
        if (_anchor is Rect anchor && _targetMonitorDeviceId is string target) PositionNear(anchor, target);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        // 点击边框或拖回原尺寸时保留原缩放与定位。
        if (e.Canceled || Math.Abs(ActualWidth - _resizeInitialBounds.Width) < 0.01 && Math.Abs(ActualHeight - _resizeInitialBounds.Height) < 0.01)
        {
            _requestedScale = _resizeSavedScale;
            _resizePlacement = _resizeSavedPlacement;
        }
        else if (!_isClosing) _presentation.CommitScale((int)Math.Round(_requestedScale * 100));
        QueuePanelPosition();
    }

    private void CancelActiveResize()
    {
        foreach (Thumb thumb in ResizeLayer.Children)
            if (thumb.IsDragging) thumb.CancelDrag();
    }

    private void CancelPanelResize()
    {
        _isResizing = false;
        foreach (Thumb thumb in ResizeLayer.Children)
        {
            if (thumb.IsDragging) thumb.CancelDrag();
            thumb.DragStarted -= ResizeGrip_DragStarted;
            thumb.DragDelta -= ResizeGrip_DragDelta;
            thumb.DragCompleted -= ResizeGrip_DragCompleted;
        }
    }
}
