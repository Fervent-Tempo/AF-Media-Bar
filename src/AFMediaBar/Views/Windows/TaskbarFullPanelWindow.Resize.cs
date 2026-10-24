// 窗口拥有局部缩放手势和捕获，设置提交交给注入的呈现 ViewModel。
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
    private Point _resizeOriginScreen;
    private Size _resizeBaseSize;
    private bool _isResizing;
    private bool _positionQueued;
    private FullPanelPlacementSide _resolvedSide;

    private IEnumerable<MetricChart> PanelCharts() => [CpuChart, RamChart, GpuChart, ProcessChart, UploadChart, DownloadChart];

    private void ApplyChartStyles(MetricVisualSettings visuals)
    {
        CpuChart.VisualStyle = visuals.SystemCpu;
        RamChart.VisualStyle = visuals.SystemMemory;
        GpuChart.VisualStyle = visuals.SystemGpu;
        ProcessChart.VisualStyle = visuals.ProcessMemory;
        UploadChart.VisualStyle = DownloadChart.VisualStyle = visuals.SystemNetwork;
        foreach (var chart in PanelCharts())
        {
            if (chart.Parent is FrameworkElement parent)
            {
                var help = !chart.IsPercentage && chart.VisualStyle is MetricVisualStyle.Bar or MetricVisualStyle.Ring
                    ? Translations.Get("Performance.Visual.Relative") : string.Empty;
                parent.ToolTip = string.IsNullOrEmpty(help) ? null : help;
                System.Windows.Automation.AutomationProperties.SetHelpText(parent, help);
            }
        }
    }

    private void ApplyScaleGeometry(Size limit)
    {
        // 阴影留白放在缩放层外，使可见贴合边及边缘命中区不随比例偏移。
        var inset = Math.Min(FullPanelScalePolicy.ShadowInsetDip, Math.Min(limit.Width, limit.Height) / 4);
        var availableWidth = limit.Width - inset * 2;
        _effectiveScale = Math.Max(TaskbarFullPanelSettings.MinimumScalePercent / 100d,
            Math.Min(FullPanelScalePolicy.Clamp(_requestedScale), availableWidth / FullPanelScalePolicy.ContentWidthDip));
        // 极窄工作区下保留 75% 字体并缩窄逻辑布局，不将临时夹取写回用户偏好。
        PanelSizingRoot.Width = Math.Min(FullPanelScalePolicy.ContentWidthDip, availableWidth / _effectiveScale);
        PanelSizingRoot.Margin = new Thickness(inset);
        ResizeLayer.Margin = new Thickness(Math.Max(0, inset - 3));
        ContentScale.ScaleX = ContentScale.ScaleY = _effectiveScale;
        Width = PanelSizingRoot.Width * _effectiveScale + inset * 2;
        PanelContentScroll.MaxHeight = Math.Max(1, (limit.Height - inset * 2) / _effectiveScale - 26);
    }

    private void OnPanelContentSizeChanged(object sender, SizeChangedEventArgs e) => QueuePanelPosition();

    private void QueuePanelPosition()
    {
        if (_positionQueued || _isClosing || !IsVisible) return;
        _positionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _positionQueued = false;
            if (!_isClosing && IsVisible && _anchor is Rect anchor && _targetMonitorDeviceId is string target)
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
        var attached = _resolvedSide switch
        {
            FullPanelPlacementSide.Above => 'B',
            FullPanelPlacementSide.Below => 'T',
            FullPanelPlacementSide.Left => 'R',
            _ => 'L'
        };
        foreach (Thumb thumb in ResizeLayer.Children)
            thumb.Visibility = ((string)thumb.Tag).Contains(attached) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizing = true;
        _resizeSavedScale = _requestedScale;
        _resizeInitialScale = _effectiveScale;
        _resizeOriginScreen = PointToScreen(Mouse.GetPosition(this));
        _resizeBaseSize = new Size(PanelRoot.ActualWidth, PanelRoot.ActualHeight);
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
        _requestedScale = FullPanelScalePolicy.DragScale(_resizeInitialScale, delta, _resizeBaseSize, (string)((Thumb)sender).Tag, _resolvedSide);
        if (_anchor is Rect anchor && _targetMonitorDeviceId is string target) PositionNear(anchor, target);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        if (e.Canceled) _requestedScale = _resizeSavedScale;
        else if (!_isClosing) _presentation.CommitScale((int)Math.Round(_requestedScale * 100));
        QueuePanelPosition();
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
