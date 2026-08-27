using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFMediaBar.Layout.Models;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildMediaText(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ?? new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1);
        if (settings.TextKind == MediaTextKind.TitleAndArtist)
        {
            var titleFontSize = Math.Clamp(settings.FontSizeDip, 6, 72);
            var artistFontSize = Math.Max(6, titleFontSize - 3);
            var titleHeight = Math.Max(22, Math.Ceiling(titleFontSize * 1.25));
            var artistHeight = Math.Max(18, Math.Ceiling(artistFontSize * 1.25));
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = widget.Geometry?.WidthDip ?? (IsVertical ? 68 : 150),
                Height = widget.Geometry?.HeightDip ?? titleHeight + artistHeight,
                ClipToBounds = true
            };
            var title = new TextBlock { FontSize = titleFontSize, Height = titleHeight, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var artist = new TextBlock { FontSize = artistFontSize, Height = artistHeight, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            SetDynamicResource(title, TextBlock.FontFamilyProperty, "AppDisplayFontFamily");
            SetDynamicResource(title, TextBlock.FontWeightProperty, "PlayerTitleFontWeight");
            SetDynamicResource(title, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarPrimaryTextBrush"));
            SetDynamicResource(artist, TextBlock.FontFamilyProperty, "AppTextFontFamily");
            SetDynamicResource(artist, TextBlock.FontWeightProperty, "PlayerTextFontWeight");
            SetDynamicResource(artist, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
            stack.Children.Add(title);
            stack.Children.Add(artist);
            _mediaTextKinds[widget.InstanceId] = MediaTextKind.TitleAndArtist;
            stack.Tag = (title, artist);
            return stack;
        }

        var text = new TextBlock
        {
            FontSize = Math.Clamp(settings.FontSizeDip, 6, 72),
            TextWrapping = settings.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = IsVertical ? 68 : 210,
            Height = Math.Max(40, Math.Max(12, Math.Ceiling(Math.Clamp(settings.FontSizeDip, 6, 72) * 1.25)) * Math.Clamp(settings.MaxLines, 1, MaximumMediaTextLines)),
            TextAlignment = TextAlignment.Center,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(text, TextBlock.FontFamilyProperty, "AppDisplayFontFamily");
        SetDynamicResource(text, TextBlock.FontWeightProperty, "PlayerTitleFontWeight");
        SetDynamicResource(text, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarPrimaryTextBrush"));
        if (settings.MaxLines > 1)
        {
            var lineHeight = Math.Max(12, Math.Ceiling(Math.Clamp(settings.FontSizeDip, 6, 72) * 1.25));
            text.Height = double.NaN;
            text.Width = double.NaN;
            text.LineHeight = lineHeight;
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            text.MaxHeight = lineHeight * Math.Clamp(settings.MaxLines, 1, MaximumMediaTextLines);
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            var host = new Grid
            {
                Width = IsVertical ? 68 : 210,
                Height = Math.Max(40, lineHeight * Math.Clamp(settings.MaxLines, 1, MaximumMediaTextLines)),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = text
            };
            host.Children.Add(text);
            _mediaTextKinds[widget.InstanceId] = settings.TextKind;
            return host;
        }

        _mediaTextKinds[widget.InstanceId] = settings.TextKind;
        if (settings.EnableMarquee && settings.MaxLines <= 1)
        {
            _marqueeStates[widget.InstanceId] = new(text, string.Empty, 0);
        }
        return text;
    }

    private FrameworkElement BuildMediaSource(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ?? new MediaTextWidgetSettings(MediaTextKind.Source, false, 11, 1);
        var text = BuildMediaText(widget with { Settings = settings with { TextKind = MediaTextKind.Source } });
        if (GetTextBlock(text) is { } textBlock)
        {
            SetDynamicResource(textBlock, TextBlock.ForegroundProperty, ResolveContentResourceKey("TaskbarSecondaryTextBrush"));
            textBlock.Cursor = Cursors.Hand;
            textBlock.ToolTip = Loc.Get("Main.Menu.ShowSource");
            SetIsInteractiveElement(textBlock, true);
            if (text is FrameworkElement host)
            {
                SetIsInteractiveElement(host, true);
                host.MouseLeftButtonUp += (_, args) =>
                {
                    args.Handled = true;
                    if (!_designMode) SourceRequested?.Invoke(this, EventArgs.Empty);
                };
            }
        }
        return text;
    }
}
