using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Adapters;
using AFMediaBar.Layout.Models;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildArtwork(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as ArtworkWidgetSettings ?? new ArtworkWidgetSettings(6, false, true);
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = _mediaSnapshot.Artwork.AsImageSource(),
            IsHitTestVisible = false
        };
        var placeholder = new TextBlock
        {
            Text = "\uE8D6",
            FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        SetDynamicResource(placeholder, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
        var grid = new Grid();
        grid.Children.Add(placeholder);
        grid.Children.Add(image);
        var useArtworkColor = settings.UseMediaPrimaryColor && _mediaSnapshot.Artwork is not null;
        var border = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(Math.Clamp(settings.CornerRadiusDip, 0, 32)),
            Background = useArtworkColor ? ResolveArtworkBackground(settings) : Brushes.Transparent,
            Child = grid,
            Cursor = settings.OpenSourceOnClick ? Cursors.Hand : Cursors.Arrow,
            ToolTip = settings.OpenSourceOnClick ? Loc.Get("Main.Menu.ShowSource") : null
        };
        if (!useArtworkColor)
        {
            SetDynamicResource(border, Border.BackgroundProperty, ResolveContentResourceKey("TaskbarSurfaceBrush"));
        }
        if (settings.OpenSourceOnClick)
        {
            SetIsInteractiveElement(border, true);
            border.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                if (_designMode) return;
                SourceRequested?.Invoke(this, EventArgs.Empty);
            };
        }
        border.Tag = (image, placeholder, settings);
        return border;
    }
}
