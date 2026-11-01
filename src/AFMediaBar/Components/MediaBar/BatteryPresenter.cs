// 面板、媒体栏和设置预览共用的矢量电量呈现；不持有设备会话或计时器。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

/// <summary>以设备轮廓和数值呈现电量，未知读数不绘制为零电量。</summary>
public sealed class BatteryPresenter : Control
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(BluetoothBatterySnapshot), typeof(BatteryPresenter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSnapshotChanged));
    public static readonly DependencyProperty VisualStyleProperty = DependencyProperty.Register(nameof(VisualStyle), typeof(BatteryVisualStyle), typeof(BatteryPresenter), new FrameworkPropertyMetadata(BatteryVisualStyle.Numeric, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty VerticalProperty = DependencyProperty.Register(nameof(Vertical), typeof(bool), typeof(BatteryPresenter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(BatteryPresenter), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(nameof(FillColor), typeof(string), typeof(BatteryPresenter), new PropertyMetadata(null, OnFillColorChanged));
    public string? FillColor { get => (string?)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }
    public BluetoothBatterySnapshot? Snapshot { get => (BluetoothBatterySnapshot?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public BatteryVisualStyle VisualStyle { get => (BatteryVisualStyle)GetValue(VisualStyleProperty); set => SetValue(VisualStyleProperty, value); }
    public bool Vertical { get => (bool)GetValue(VerticalProperty); set => SetValue(VerticalProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public BatteryPresenter()
    {
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        SetResourceReference(AccentProperty, "AfAccentBrush");
        SetResourceReference(FontFamilyProperty, "AppTextFontFamily");
        FontSize = 12;
        IsHitTestVisible = false;
    }
    private static void OnSnapshotChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (BatteryPresenter)sender;
        var reading = presenter.Snapshot;
        var text = reading is null ? Translations.Get("Battery.NoReading") : reading.DeviceName + " · " + reading.Label;
        System.Windows.Automation.AutomationProperties.SetName(presenter, text);
        presenter.ToolTip = reading?.Details;
    }
    private static void OnFillColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (BatteryPresenter)sender;
        if (Classes.Utils.ColorHex.TryParse(args.NewValue as string, out var color))
        {
            var brush = new SolidColorBrush(color); brush.Freeze(); presenter.Accent = brush;
        }
        else presenter.SetResourceReference(AccentProperty, "AfAccentBrush");
    }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
    protected override Size MeasureOverride(Size constraint)
    {
        var desired = VisualStyle == BatteryVisualStyle.Graphic ? new Size(26, 24) : Vertical ? new Size(40, 42) : new Size(70, 28);
        return new(Math.Min(constraint.Width, desired.Width), Math.Min(constraint.Height, desired.Height));
    }
    private static readonly Geometry[] Outlines =
    [
        Shape("M7,3 L17,3 Q20,3 20,6 L20,23 Q20,26 17,26 L7,26 Q4,26 4,23 L4,6 Q4,3 7,3 Z"),
        Shape("M12,2 C6,2 4,6 4,12 L4,19 C4,29 20,29 20,19 L20,12 C20,6 18,2 12,2 Z"),
        Shape("M3,7 L21,7 Q23,7 23,9 L23,22 Q23,24 21,24 L3,24 Q1,24 1,22 L1,9 Q1,7 3,7 Z"),
        Shape("M2,16 L2,12 C2,-1 22,-1 22,12 L22,23 Q22,26 18,26 L16,26 L16,15 L19,15 L19,12 C19,3 5,3 5,12 L5,15 L8,15 L8,26 L6,26 Q2,26 2,23 Z"),
        Shape("M6,8 L18,8 C23,8 24,24 20,25 L15,21 L9,21 L4,25 C0,24 1,8 6,8 Z")
    ];
    private static readonly Geometry Lightning = Shape("M4,0 L0,5 L3,5 L2,9 L7,3 L4,3 Z");
    private static Geometry Shape(string path) { var geometry = Geometry.Parse(path); geometry.Freeze(); return geometry; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var snapshot = Snapshot;
        var foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Foreground;
        var graphic = VisualStyle == BatteryVisualStyle.Graphic;
        var percent = snapshot?.Percent is >= 0 and <= 100 ? snapshot.Percent : null;
        var color = SystemParameters.HighContrast ? foreground : Accent;
        var iconScale = graphic ? Math.Min(.72, Math.Min(ActualWidth / 32, ActualHeight / 28)) : .65;
        var x = graphic || Vertical ? (ActualWidth - 24 * iconScale) / 2 : 1;
        var y = graphic ? (ActualHeight - 28 * iconScale) / 2 : Vertical ? 0 : (ActualHeight - 28 * iconScale) / 2;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(iconScale, iconScale));
        var kind = snapshot?.Kind ?? BatteryDeviceKind.Unknown;
        var shape = Outlines[Enum.IsDefined(kind) ? (int)kind : 0];
        dc.PushOpacity(.12); dc.DrawGeometry(foreground, null, shape); dc.Pop();
        if (graphic && percent is { } level)
        {
            dc.PushClip(shape);
            dc.PushOpacity(snapshot?.IsRealtime == true ? .85 : .4);
            dc.DrawRectangle(color, null, new Rect(0, 28 * (1 - level / 100d), 24, 28 * level / 100d));
            dc.Pop(); dc.Pop();
        }
        var pen = new Pen(foreground, 1.4);
        dc.DrawGeometry(null, pen, shape);
        switch (snapshot?.Kind)
        {
            case BatteryDeviceKind.Mouse:
                dc.DrawLine(pen, new(12, 3), new(12, 12));
                dc.DrawRoundedRectangle(foreground, null, new Rect(10.5, 6, 3, 5), 1, 1); break;
            case BatteryDeviceKind.Keyboard:
                for (var row = 0; row < 2; row++) for (var col = 0; col < 5; col++) dc.DrawRectangle(foreground, null, new Rect(4 + col * 3.4, 11 + row * 4, 1.5, 1.5));
                dc.DrawLine(pen, new(7, 20), new(17, 20)); break;
            case BatteryDeviceKind.Controller:
                dc.DrawLine(pen, new(5, 14), new(11, 14)); dc.DrawLine(pen, new(8, 11), new(8, 17));
                dc.DrawEllipse(foreground, null, new(17, 13), 1, 1); dc.DrawEllipse(foreground, null, new(19, 16), 1, 1); break;
        }
        if (graphic && percent is null) DrawText(dc, "?", foreground, 12, new(8, 8));
        dc.Pop(); dc.Pop();
        if (!graphic)
        {
            var label = percent is { } value ? value + "%" : "—";
            var text = Text(label, foreground, 12);
            dc.DrawText(text, Vertical ? new Point((ActualWidth - text.Width) / 2, 22) : new Point(21, (ActualHeight - text.Height) / 2));
        }
        var markerX = Math.Max(0, ActualWidth - 7);
        if (snapshot?.IsCharging == true)
        {
            dc.PushTransform(new TranslateTransform(markerX, 0));
            dc.DrawGeometry(foreground, null, Lightning); dc.Pop();
        }
        else if (percent is not null && snapshot?.IsRealtime != true)
        {
            var markerPen = new Pen(foreground, .9);
            dc.DrawEllipse(null, markerPen, new(markerX + 3, 4), 2.7, 2.7);
            dc.DrawLine(markerPen, new(markerX + 3, 2), new(markerX + 3, 4));
            dc.DrawLine(markerPen, new(markerX + 3, 4), new(markerX + 4.5, 4.7));
        }

    }
    private FormattedText Text(string text, Brush brush, double size) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void DrawText(DrawingContext dc, string text, Brush brush, double size, Point point) => dc.DrawText(Text(text, brush, size), point);
}
