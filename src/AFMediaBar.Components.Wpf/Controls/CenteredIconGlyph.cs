using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AFMediaBar.Components.Wpf.Controls;

/// <summary>
/// Centers the rendered glyph outline instead of the font's advance box and baseline.
/// </summary>
public sealed class CenteredIconGlyph : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(CenteredIconGlyph),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrEmpty(Glyph) || Foreground is null || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var text = new FormattedText(
            Glyph,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground,
            dpi.PixelsPerDip);
        var outline = text.BuildGeometry(new Point(0, 0));
        var bounds = outline.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        var offset = new Vector(
            ((RenderSize.Width - bounds.Width) / 2) - bounds.Left,
            ((RenderSize.Height - bounds.Height) / 2) - bounds.Top);
        drawingContext.PushTransform(new TranslateTransform(offset.X, offset.Y));
        drawingContext.DrawGeometry(Foreground, null, outline);
        drawingContext.Pop();
    }
}
