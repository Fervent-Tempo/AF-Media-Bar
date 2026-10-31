// 组装固定示例的纯呈现树；共用运行表面的模板，不创建窗口、媒体、采样或计时器。
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Components;

/// <summary>当前设置的静态场景，订阅只在场景位于可视树时存在。</summary>
public sealed class SettingsPreviewScene : ContentControl
{
    // 当前图标库内部文本有局部前景；显式转接到图标前景，避免主题切换后仍绘制黑色字形。
    private sealed class PreviewSymbolIcon : SymbolIcon
    {
        protected override UIElement InitializeChildren()
        {
            var child = base.InitializeChildren();
            BindingOperations.SetBinding(child, System.Windows.Controls.TextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { Source = this });
            return child;
        }
    }

    private SettingsPreviewViewModel? _model;
    public SettingsPreviewScene()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }
    private void Attach()
    {
        Detach();
        _model = DataContext as SettingsPreviewViewModel;
        if (_model is not null) _model.PropertyChanged += OnChanged;
        Rebuild();
    }
    private void Detach()
    {
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = null;
        Content = null;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Rebuild();
    private void Rebuild()
    {
        if (_model is not { } model) return;
        if (SystemParameters.HighContrast) Foreground = SystemColors.WindowTextBrush;
        else if (model.Appearance.PlayerForegroundMode == PlayerForegroundMode.Automatic)
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        else Foreground = model.Appearance.PlayerForegroundMode == PlayerForegroundMode.LightText ? Brushes.White : new SolidColorBrush(Color.FromRgb(28, 28, 28));
        Content = model.Mode == SettingsPreviewMode.FullPanel ? BuildPanel(model) : BuildBar(model);
    }
    private T Themed<T>(T element, DependencyProperty property, string resource) where T : FrameworkElement
    {
        element.SetResourceReference(property, resource);
        return element;
    }
    private TextBlock Label(string text, double size, bool secondary = false) => Themed(new TextBlock
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    }, TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
    private Border Card(UIElement child) => new() { Child = child, Style = TryFindResource("PanelCardStyle") as Style };
    private MetricPresenter Metric(MetricKind metric, MetricVisualStyle style, bool compact, bool vertical = false)
    {
        var presenter = new MetricPresenter { Metric = metric, VisualStyle = style, Compact = compact, Vertical = vertical, ShowHover = false, IsHitTestVisible = false };
        presenter.ShowPreview();
        if (compact) { presenter.Width = vertical ? 44 : presenter.PreferredWidth; presenter.Height = vertical ? presenter.PreferredVerticalLength : 28; }
        else { presenter.Width = 122; presenter.Height = 92; }
        return presenter;
    }
    private Border Artwork(double size, bool progress, bool note = false)
    {
        var content = new Grid();
        if (note)
        {
            var icon = Themed(new PreviewSymbolIcon { Symbol = SymbolRegular.MusicNote220, FontSize = size * .7 }, SymbolIcon.ForegroundProperty, "AfAccentBrush");
            if (SystemParameters.HighContrast) icon.Foreground = SystemColors.WindowTextBrush;
            else if (ColorHex.TryParse(_model?.Appearance.IdleNoteColor, out var color)) icon.Foreground = new SolidColorBrush(color);
            content.Children.Add(icon);
        }
        else
        {
            content.Background = new LinearGradientBrush(Color.FromRgb(108, 138, 123), Color.FromRgb(211, 187, 158), 50);
            content.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0,32 L 22,6 L 44,22 L 44,44 L 0,44 Z"), Fill = new SolidColorBrush(Color.FromRgb(170, 188, 160)),
                Stretch = Stretch.Fill, Opacity = .8, Margin = new Thickness(2)
            });
        }
        if (progress) content.Children.Add(Themed(new ArtworkProgress { Progress = .38 }, ArtworkProgress.StrokeProperty, "AfAccentBrush"));
        return new Border { Width = size, Height = size, Child = content, ClipToBounds = true, CornerRadius = new CornerRadius(5.5) };
    }
    private T PlayerForeground<T>(T element, DependencyProperty property) where T : FrameworkElement
    {
        BindingOperations.SetBinding(element, property, new Binding(nameof(Foreground)) { Source = this });
        return element;
    }
    private Button BarButton(TaskbarRestComponent component, TaskbarDensityMetrics density)
    {
        FrameworkElement icon;
        var glyph = component switch
        {
            TaskbarRestComponent.OutputDevice => MediaControlGlyphs.OutputDevice,
            TaskbarRestComponent.InputDevice => MediaControlGlyphs.InputDevice,
            TaskbarRestComponent.Volume => MediaControlGlyphs.Volume,
            _ => null
        };
        if (glyph is not null)
        {
            var text = PlayerForeground(new TextBlock { Text = glyph, FontSize = density.IconSize }, TextBlock.ForegroundProperty);
            text.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFontFamily");
            icon = text;
        }
        else icon = PlayerForeground(new PreviewSymbolIcon
        {
            Symbol = component == TaskbarRestComponent.PlayPause ? SymbolRegular.Pause24 : ComponentSymbolConverter.Resolve(component),
            FontSize = density.IconSize
        }, SymbolIcon.ForegroundProperty);
        return new Button
        {
            Style = TryFindResource("TaskbarHoverButton") as Style, Width = density.ButtonSize, Height = density.ButtonSize, Content = icon
        };
    }
    private UIElement BuildSpectrum(SettingsPreviewViewModel model, double crossSize)
    {
        var settings = model.Spectrum;
        var height = Math.Min(SpectrumPresentationPolicy.ResolveContentHeightDip(settings), Math.Max(1, crossSize - 8));
        var count = model.IsVertical ? VerticalTaskbarPresentationPolicy.SpectrumBandCount(crossSize, Math.Min(16, settings.BandCount)) : settings.BandCount;
        var width = count * SpectrumPresentationPolicy.BarPitchDip - SpectrumPresentationPolicy.BarGapDip;
        var canvas = new Canvas { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var bands = Enumerable.Range(0, count).Select(i => (float)(.2 + .65 * Math.Abs(Math.Sin((i + 1) * .8)))).ToArray();
        if (settings.Style == SpectrumStyle.Waveform)
        {
            var outline = SpectrumPresentationPolicy.CreateWaveformOutline(bands, settings.SensitivityPercent, height);
            var (geometry, _) = SpectrumVisualFactory.CreateWaveform(outline);
            canvas.Children.Add(PlayerForeground(new System.Windows.Shapes.Path { Data = geometry }, System.Windows.Shapes.Path.FillProperty));
        }
        else for (var index = 0; index < count; index++)
        {
            if (settings.Style == SpectrumStyle.PixelBars)
            {
                var lit = SpectrumPresentationPolicy.ResolveLitPixelCount(bands[index], settings.SensitivityPercent, height);
                for (var dotIndex = 0; dotIndex < SpectrumPresentationPolicy.ResolvePixelDotCount(height); dotIndex++)
                {
                    var dot = SpectrumVisualFactory.CreatePixel(index, dotIndex, height, Foreground);
                    dot.Opacity = dotIndex < lit ? 1d : SpectrumVisualFactory.UnlitPixelOpacity;
                    canvas.Children.Add(PlayerForeground(dot, Border.BackgroundProperty));
                }
            }
            else
            {
                var scale = SpectrumPresentationPolicy.ResolveBarScale(bands[index], settings.SensitivityPercent, height);
                var (bar, _) = SpectrumVisualFactory.CreateBar(index, height, settings.Style, Foreground, scale);
                canvas.Children.Add(PlayerForeground(bar, Border.BackgroundProperty));
            }
        }
        return canvas;
    }
    private UIElement BuildMetadata(SettingsPreviewViewModel model)
    {
        var scale = model.Experience.MediaFontSizePercent / 100d;
        var title = TaskbarExperiencePolicy.FormatMetadataTitle(Translations.Get("Settings.Preview.Track"),
            Translations.Get("Settings.Preview.Artist"), model.Experience.ContentLayout);
        if (model.IsVertical)
        {
            var columns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            void AddColumn(string text, double size, double opacity)
            {
                var column = new StackPanel { Margin = new Thickness(1, 0, 1, 0), Opacity = opacity };
                var sideways = new System.Text.StringBuilder();
                void FlushSideways()
                {
                    if (sideways.Length == 0) return;
                    var run = PlayerForeground(Label(sideways.ToString(), size), TextBlock.ForegroundProperty);
                    run.LayoutTransform = new RotateTransform(90);
                    column.Children.Add(run);
                    sideways.Clear();
                }
                var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (System.Text.Rune.GetRuneAt(element, 0).Value < 0x2E80) sideways.Append(element);
                    else
                    {
                        FlushSideways();
                        column.Children.Add(PlayerForeground(Label(element, size), TextBlock.ForegroundProperty));
                    }
                }
                FlushSideways();
                columns.Children.Add(column);
            }
            if (model.Experience.ContentLayout != TaskbarContentLayout.CompactInline)
                AddColumn(Translations.Get("Settings.Preview.Artist"), VerticalTaskbarPresentationPolicy.SecondaryFontSizeDip * scale, .5);
            AddColumn(title, VerticalTaskbarPresentationPolicy.PrimaryFontSizeDip * scale, 1);
            return columns;
        }
        var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = TaskbarExperiencePolicy.MaximumMetadataTextWidthDip };
        var titleText = PlayerForeground(Label(title, 14 * scale), TextBlock.ForegroundProperty);
        titleText.TextAlignment = model.Experience.ContentLayout == TaskbarContentLayout.CenteredStack ? TextAlignment.Center : TextAlignment.Left;
        metadata.Children.Add(titleText);
        if (model.Experience.ContentLayout != TaskbarContentLayout.CompactInline)
        {
            var artist = PlayerForeground(Label(Translations.Get("Settings.Preview.Artist"), 12 * scale), TextBlock.ForegroundProperty);
            artist.TextAlignment = titleText.TextAlignment;
            artist.Opacity = .5;
            metadata.Children.Add(artist);
        }
        return metadata;
    }
    private UIElement BuildBar(SettingsPreviewViewModel model)
    {
        if (model.IsEmpty) return Label(model.EmptyText, 13, true);
        var density = TaskbarDensityMetrics.From(model.Experience.Density);
        var crossSize = model.IsVertical ? 48d : 44d * model.ThicknessScale;
        var gap = TaskbarExperiencePolicy.ResolveSectionGap(model.Experience.Density, model.Experience.ComponentSpacingDip) * model.LengthScale;
        var line = new StackPanel { Orientation = model.IsVertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var index = 0; index < model.Items.Count; index++)
        {
            var item = model.Items[index];
            FrameworkElement visual;
            if (item.Metric is { } metric) visual = Metric(metric, model.Performance.Visuals.Get(metric), true, model.IsVertical);
            else if (item.Component == TaskbarRestComponent.Artwork)
            {
                var size = model.IsVertical ? crossSize - 4 : 40 * model.ThicknessScale;
                if (model.Experience.SmallArtwork && !model.ShowIdle) size *= .8;
                visual = Artwork(size, !model.ShowIdle && model.Experience.ArtworkProgressVisible, model.ShowIdle);
            }
            else if (item.Component == TaskbarRestComponent.MediaText) visual = (FrameworkElement)BuildMetadata(model);
            else if (item.Component == TaskbarRestComponent.Spectrum) visual = (FrameworkElement)BuildSpectrum(model, crossSize);
            else if (item.Component == TaskbarRestComponent.BluetoothBattery)
            {
                var battery = new StackPanel { Orientation = model.IsVertical ? Orientation.Vertical : Orientation.Horizontal };
                battery.Children.Add(PlayerForeground(new PreviewSymbolIcon { Symbol = SymbolRegular.Bluetooth24, FontSize = 14 }, SymbolIcon.ForegroundProperty));
                battery.Children.Add(PlayerForeground(Label("72%", 12), TextBlock.ForegroundProperty)); visual = battery;
            }
            else visual = BarButton(item.Component, density);
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            visual.VerticalAlignment = VerticalAlignment.Center;
            var spacing = index + 1 < model.Items.Count ? gap : 0;
            visual.Margin = model.IsVertical ? new Thickness(0, 0, 0, spacing) : new Thickness(0, 0, spacing, 0);
            line.Children.Add(visual);
        }
        var surface = new Grid(); surface.Children.Add(line);
        if (model.IsRest && !model.ShowIdle && model.Experience.RestProgressVisible)
        {
            var track = new Grid();
            if (model.IsVertical)
            {
                track.Width = 2;
                track.HorizontalAlignment = HorizontalAlignment.Right;
                track.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38, GridUnitType.Star) });
                track.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62, GridUnitType.Star) });
            }
            else
            {
                track.Height = 2;
                track.VerticalAlignment = VerticalAlignment.Bottom;
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
            }
            track.Children.Add(PlayerForeground(new Border(), Border.BackgroundProperty));
            surface.Children.Add(track);
        }
        var usesLightText = Foreground is SolidColorBrush foreground && foreground.Color.R > 128;
        var material = TaskbarBackgroundMaterialPolicy.Resolve(model.Appearance, !model.ShowIdle, SystemParameters.HighContrast, usesLightText);
        var layers = new Grid();
        if (material.ArtworkOpacity > 0)
            layers.Children.Add(new Border { CornerRadius = new CornerRadius(6), Opacity = material.ArtworkOpacity,
                Background = new LinearGradientBrush(Color.FromRgb(108, 138, 123), Color.FromRgb(211, 187, 158), 50) });
        layers.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = SystemParameters.HighContrast && material.Tint.A > 0
            ? SystemColors.WindowBrush : new SolidColorBrush(material.Tint) });
        layers.Children.Add(surface);
        return new Border { Child = layers, Padding = new Thickness(2), CornerRadius = new CornerRadius(6),
            BorderBrush = SystemParameters.HighContrast && material.Stroke.A > 0 ? SystemColors.WindowTextBrush : new SolidColorBrush(material.Stroke),
            BorderThickness = new Thickness(1.25), Width = model.IsVertical ? crossSize : double.NaN,
            MinHeight = model.IsVertical ? 0 : crossSize };
    }
    private UIElement BuildPanel(SettingsPreviewViewModel model)
    {
        if (!model.Experience.FullLayerEnabled) return Label(model.EmptyText, 13, true);
        var settings = model.Experience.FullPanel;
        var content = new StackPanel { Width = FullPanelScalePolicy.ContentWidthDip - 26 };
        if (settings.MediaInfoVisible)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(Artwork(76, false));
            var text = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = Label(Translations.Get("Settings.Preview.Track"), 20);
            title.SetResourceReference(TextBlock.FontWeightProperty, "AppTextStrongFontWeight");
            text.Children.Add(title);
            var artist = Label(Translations.Get("Settings.Preview.Artist"), 13, true); artist.Margin = new Thickness(0, 5, 0, 0);
            text.Children.Add(artist);
            var source = Label("AF Media Bar", 12, true); source.Margin = new Thickness(0, 5, 0, 0); text.Children.Add(source);
            Grid.SetColumn(text, 1); header.Children.Add(text); content.Children.Add(header);
        }
        if (settings.MediaControlsVisible)
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            section.Children.Add(new Slider { Minimum = 0, Maximum = 1, Value = .38, Height = 20, Style = TryFindResource("AfAccentSliderStyle") as Style });
            var time = new Grid();
            time.Children.Add(Label("1:25", 12, true));
            var duration = Label("3:40", 12, true); duration.HorizontalAlignment = HorizontalAlignment.Right; time.Children.Add(duration);
            section.Children.Add(time);
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            Wpf.Ui.Controls.Button Transport(SymbolRegular symbol) => new() { Width = 40, Height = 40, Padding = new Thickness(0), Content = new PreviewSymbolIcon { Symbol = symbol, FontSize = 20 } };
            controls.Children.Add(Transport(SymbolRegular.Previous24));
            controls.Children.Add(new Wpf.Ui.Controls.Button { Style = TryFindResource("PanelPlayButtonStyle") as Style, Width = 48, Height = 48,
                Padding = new Thickness(0), Margin = new Thickness(12, 0, 12, 0), Content = new PreviewSymbolIcon { Symbol = SymbolRegular.Pause24, FontSize = 24 } });
            controls.Children.Add(Transport(SymbolRegular.Next24));
            row.Children.Add(controls);
            row.Children.Add(new Wpf.Ui.Controls.Button { Width = 36, Height = 36, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Content = new PreviewSymbolIcon { Symbol = SymbolRegular.ArrowRepeatAll24, FontSize = 18 } });
            section.Children.Add(row); content.Children.Add(section);
        }
        if (settings.AudioControlsVisible)
        {
            var audio = new StackPanel();
            var devices = new Grid();
            devices.ColumnDefinitions.Add(new ColumnDefinition()); devices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); devices.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var input in new[] { false, true })
            {
                var label = Translations.Get(input ? "Common.InputDevice" : "Common.OutputDevice");
                var column = new StackPanel();
                var heading = Label(label, 12, true); heading.Margin = new Thickness(0, 0, 0, 6); column.Children.Add(heading);
                column.Children.Add(new ReleaseOpenComboBox { Style = TryFindResource("PanelDeviceStyle") as Style, ItemsSource = new[] { label }, SelectedIndex = 0 });
                Grid.SetColumn(column, input ? 2 : 0); devices.Children.Add(column);
            }
            audio.Children.Add(devices);
            var volume = new StackPanel();
            var row = new Grid(); row.Children.Add(Label(Translations.Get("Panel.Volume.CurrentAppVolume"), 12, true));
            var value = Label("68%", 12); value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); volume.Children.Add(row);
            volume.Children.Add(new Slider { Value = 68, Maximum = 100, Height = 24, Margin = new Thickness(0, 6, 0, 0), Style = TryFindResource("AfAccentSliderStyle") as Style });
            var divider = Themed(new Border { Child = volume, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0) }, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            audio.Children.Add(divider);
            var card = Card(audio); card.Margin = new Thickness(0, 0, 0, 12); content.Children.Add(card);
        }
        if (settings.PerformanceVisible)
        {
            var metrics = new FullPanelMetricsView();
            foreach (var presenter in metrics.Presenters)
            {
                presenter.VisualStyle = settings.PerformanceVisuals.Get(presenter.Metric);
                presenter.ShowHover = false; presenter.IsHitTestVisible = false; presenter.ShowPreview();
            }
            content.Children.Add(Card(metrics));
        }
        var panel = Themed(new Border { Child = content, Padding = new Thickness(12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            LayoutTransform = new ScaleTransform(settings.ScalePercent / 100d, settings.ScalePercent / 100d) }, Border.BackgroundProperty, "AfSettingsCardBrush");
        return Themed(panel, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
    }
}
