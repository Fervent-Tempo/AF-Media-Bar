// 设置页内的位置示意与原尺寸卡片，共用真实通知内容及定位算法；不显示桌面窗口。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>根据目标工作区的 DIP 尺寸实时预览通知位置和文字样式。</summary>
public partial class TrackNotificationPreview : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(TrackChangeNotificationSettings),
        typeof(TrackNotificationPreview), new PropertyMetadata(TrackChangeNotificationSettings.Default, OnPreviewChanged));
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(MediaSnapshot),
        typeof(TrackNotificationPreview), new PropertyMetadata(null, OnPreviewChanged));
    public static readonly DependencyProperty WorkAreaSizeProperty = DependencyProperty.Register(nameof(WorkAreaSize), typeof(Size),
        typeof(TrackNotificationPreview), new PropertyMetadata(new Size(1920, 1040), OnPreviewChanged));
    public TrackChangeNotificationSettings Settings { get => (TrackChangeNotificationSettings)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public MediaSnapshot? Snapshot { get => (MediaSnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public Size WorkAreaSize { get => (Size)GetValue(WorkAreaSizeProperty); set => SetValue(WorkAreaSizeProperty, value); }

    public TrackNotificationPreview()
    {
        InitializeComponent();
        PositionCard.SizeChanged += (_, _) => UpdatePosition();
        UpdatePosition();
    }
    private static void OnPreviewChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((TrackNotificationPreview)sender).UpdatePosition();
    private void UpdatePosition()
    {
        if (Screen is null) return;
        var size = WorkAreaSize;
        var width = double.IsFinite(size.Width) && size.Width > 0 ? size.Width : 1920;
        var height = double.IsFinite(size.Height) && size.Height > 0 ? size.Height : 1040;
        Screen.Width = width;
        Screen.Height = height;
        PositionCard.Measure(new Size(388, double.PositiveInfinity));
        var settings = Settings.Normalize();
        var position = NotificationPlacementCalculator.Calculate(new Rect(0, 0, width, height), PositionCard.DesiredSize,
            settings.Position, 16, settings.HorizontalOffsetDip, settings.VerticalOffsetDip);
        Canvas.SetLeft(PositionCard, position.X);
        Canvas.SetTop(PositionCard, position.Y);
    }
}
