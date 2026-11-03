// 设置页内的通知位置编辑器；拥有局部鼠标捕获，取消或卸载时释放，不显示桌面窗口。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>以目标工作区 DIP 尺寸呈现定位点，通过拖动和选择锚点表达位置调整。</summary>
public partial class TrackNotificationPreview : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(TrackChangeNotificationSettings),
        typeof(TrackNotificationPreview), new PropertyMetadata(TrackChangeNotificationSettings.Default, OnPreviewChanged));
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(MediaSnapshot),
        typeof(TrackNotificationPreview), new PropertyMetadata(null, OnPreviewChanged));
    public static readonly DependencyProperty WorkAreaSizeProperty = DependencyProperty.Register(nameof(WorkAreaSize), typeof(Size),
        typeof(TrackNotificationPreview), new PropertyMetadata(new Size(1920, 1040), OnPreviewChanged));
    public static readonly DependencyProperty PlacementCommandProperty = DependencyProperty.Register(nameof(PlacementCommand), typeof(ICommand),
        typeof(TrackNotificationPreview), new PropertyMetadata(null));
    public TrackChangeNotificationSettings Settings { get => (TrackChangeNotificationSettings)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public MediaSnapshot? Snapshot { get => (MediaSnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public Size WorkAreaSize { get => (Size)GetValue(WorkAreaSizeProperty); set => SetValue(WorkAreaSizeProperty, value); }
    public ICommand? PlacementCommand { get => (ICommand?)GetValue(PlacementCommandProperty); set => SetValue(PlacementCommandProperty, value); }

    private const double PlacementMargin = 16;
    private const double SnapRadius = 14;
    private readonly List<Button> _anchors = [];
    private bool _isDragging;
    private Vector _dragOffset;
    private NotificationPlacement? _dragPlacement;

    public TrackNotificationPreview()
    {
        InitializeComponent();
        foreach (var position in Enum.GetValues<TrackChangeNotificationPosition>())
        {
            var anchor = new Button { Tag = position, Style = (Style)Resources["NotificationAnchorStyle"] };
            anchor.SetResourceReference(ToolTipProperty, "Loc.Media.Notification.Position." + position);
            anchor.SetResourceReference(AutomationProperties.NameProperty, "Loc.Media.Notification.Position." + position);
            anchor.Click += OnAnchorClicked;
            _anchors.Add(anchor);
            AnchorPoints.Children.Add(anchor);
        }
        PositionCard.SizeChanged += (_, _) => UpdatePosition();
        UpdatePosition();
    }
    private static void OnPreviewChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var preview = (TrackNotificationPreview)sender;
        preview.CancelDrag();
        preview.UpdatePosition();
    }
    private void UpdatePosition()
    {
        if (Screen is null) return;
        var size = WorkAreaSize;
        var width = double.IsFinite(size.Width) && size.Width > 0 ? size.Width : 1920;
        var height = double.IsFinite(size.Height) && size.Height > 0 ? size.Height : 1040;
        Screen.Width = width;
        Screen.Height = height;
        AnchorPoints.Width = width;
        AnchorPoints.Height = height;
        PositionCard.Measure(new Size(400, double.PositiveInfinity));
        var settings = Settings.Normalize();
        var placement = _dragPlacement ?? new NotificationPlacement(settings.Position, settings.HorizontalOffsetDip, settings.VerticalOffsetDip);
        var position = NotificationPlacementCalculator.Calculate(new Rect(0, 0, width, height), PositionCard.DesiredSize,
            placement.Position, PlacementMargin, placement.HorizontalOffset, placement.VerticalOffset);
        Canvas.SetLeft(PositionCard, position.X);
        Canvas.SetTop(PositionCard, position.Y);
        var pointSize = 16 / PreviewScale;
        foreach (var anchor in _anchors)
        {
            var point = NotificationPlacementCalculator.Calculate(new Rect(0, 0, width, height), PositionCard.DesiredSize,
                (TrackChangeNotificationPosition)anchor.Tag, PlacementMargin);
            anchor.Width = anchor.Height = pointSize;
            anchor.BorderThickness = new Thickness(2 / PreviewScale);
            Canvas.SetLeft(anchor, point.X + PositionCard.DesiredSize.Width / 2 - pointSize / 2);
            Canvas.SetTop(anchor, point.Y + PositionCard.DesiredSize.Height / 2 - pointSize / 2);
        }
    }

    private double PreviewScale
    {
        get
        {
            var scale = Math.Min(ScreenView.ActualWidth / Screen.Width, ScreenView.ActualHeight / Screen.Height);
            return double.IsFinite(scale) && scale > 0 ? scale : 1;
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdatePosition();

    private void OnAnchorClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: TrackChangeNotificationPosition position }) ApplyPlacement(new NotificationPlacement(position, 0, 0));
    }

    private void OnDragStarted(object sender, MouseButtonEventArgs args)
    {
        PositionCard.Focus();
        if (!PositionCard.CaptureMouse()) return;
        _dragOffset = args.GetPosition(Screen) - new Point(Canvas.GetLeft(PositionCard), Canvas.GetTop(PositionCard));
        _isDragging = true;
        args.Handled = true;
    }

    private void OnDragMoved(object sender, MouseEventArgs args)
    {
        if (!_isDragging) return;
        if (args.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
        _dragPlacement = NotificationPlacementCalculator.ResolveDrag(new Rect(0, 0, Screen.Width, Screen.Height), PositionCard.DesiredSize,
            args.GetPosition(Screen) - _dragOffset, PlacementMargin, SnapRadius / PreviewScale);
        UpdatePosition();
        args.Handled = true;
    }

    private void OnDragCompleted(object sender, MouseButtonEventArgs args)
    {
        if (!_isDragging) return;
        var placement = _dragPlacement;
        CancelDrag();
        if (placement is { } value) ApplyPlacement(value);
        args.Handled = true;
    }

    private void ApplyPlacement(NotificationPlacement placement)
    {
        if (PlacementCommand is { } command)
        {
            if (command.CanExecute(placement)) command.Execute(placement);
        }
        else SetCurrentValue(SettingsProperty, Settings with { Position = placement.Position, HorizontalOffsetDip = placement.HorizontalOffset, VerticalOffsetDip = placement.VerticalOffset });
    }

    private void CancelDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragPlacement = null;
        if (PositionCard.IsMouseCaptured) PositionCard.ReleaseMouseCapture();
        UpdatePosition();
    }

    private void OnCaptureLost(object sender, MouseEventArgs args) => CancelDrag();
    private void OnUnloaded(object sender, RoutedEventArgs args) => CancelDrag();
    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !_isDragging) return;
        CancelDrag();
        args.Handled = true;
    }
}
