using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using AFMediaBar.Components.Wpf.Controls;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Controls;

internal sealed partial class ComponentLayoutSurface
{
    private FrameworkElement BuildCommand(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as CommandWidgetSettings ??
            new CommandWidgetSettings(
                MediaCommandKind.PlayPause,
                CommandWidgetSettings.DefaultButtonSizeDip);
        var button = new Button
        {
            Width = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Height = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Cursor = Cursors.Hand,
            Style = GetResource<Style>(_componentSkinService.ResolveResourceKey(widget, _useMenuThemeForContent)),
            Tag = settings.Command,
            ToolTip = GetCommandTooltip(settings.Command),
            Content = new CenteredIconGlyph
            {
                Width = DefaultCommandGlyphSizeDip,
                Height = DefaultCommandGlyphSizeDip,
                Glyph = GetCommandGlyph(settings.Command),
                FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, args) =>
        {
            args.Handled = true;
            if (_designMode)
            {
                return;
            }

            CommandRequested?.Invoke(
                this,
                new LayoutCommandEventArgs(settings.Command, button));
        };
        if (settings.Command is MediaCommandKind.SelectOutputDevice or MediaCommandKind.AdjustVolume)
        {
            button.PreviewMouseWheel += (_, args) =>
            {
                args.Handled = true;
                if (_designMode)
                {
                    return;
                }

                WheelRequested?.Invoke(
                    this,
                    new LayoutWheelEventArgs(settings.Command, args.Delta, button));
            };
        }
        SetIsInteractiveElement(button, true);
        return button;
    }
}
