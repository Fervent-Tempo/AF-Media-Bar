// 实际通知和设置预览共用的只读卡片；不拥有计时器、窗口或媒体订阅。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Components;

/// <summary>使用同一份通知设置呈现标题、歌手与封面。</summary>
public partial class TrackNotificationContent : UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(MediaSnapshot),
        typeof(TrackNotificationContent), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(TrackChangeNotificationSettings),
        typeof(TrackNotificationContent), new PropertyMetadata(TrackChangeNotificationSettings.Default, OnPresentationChanged));
    public MediaSnapshot? Snapshot { get => (MediaSnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public TrackChangeNotificationSettings Settings { get => (TrackChangeNotificationSettings)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }

    public TrackNotificationContent() { InitializeComponent(); ApplyPresentation(); }
    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((TrackNotificationContent)sender).ApplyPresentation();
    private void ApplyPresentation()
    {
        if (TitleText is null) return;
        var snapshot = Snapshot ?? MediaSnapshot.Disconnected;
        var settings = Settings.Normalize();
        TitleText.Text = snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.SourceName : snapshot.Artist;
        TitleText.FontSize = settings.TitleFontSize;
        ArtistText.FontSize = settings.ArtistFontSize;
        ArtistText.Opacity = settings.ArtistColor is null ? 0.7 : 1;
        ApplyColor(TitleText, settings.TitleColor);
        ApplyColor(ArtistText, settings.ArtistColor);
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        var bitmap = snapshot.Artwork as BitmapSource;
        var box = ArtworkBoxPolicy.Resolve(44, bitmap?.PixelWidth ?? 0, bitmap?.PixelHeight ?? 0);
        ArtworkBorder.Width = box.Width;
        ArtworkColumn.Width = new GridLength(box.Width);
    }
    private static void ApplyColor(TextBlock text, string? hex)
    {
        if (ColorHex.TryParse(hex, out var color))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            text.Foreground = brush;
        }
        else text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
    }
}
