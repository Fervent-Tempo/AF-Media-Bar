// 组装固定示例的纯呈现树；共用运行表面的模板，不创建窗口、媒体、采样或计时器。
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using ProgressBar = System.Windows.Controls.ProgressBar;

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
        if (compact) { presenter.Width = vertical ? 48 : presenter.PreferredWidth; presenter.Height = vertical ? presenter.PreferredVerticalLength : 38; }
        else { presenter.Width = 122; presenter.Height = 92; }
        return presenter;
    }
    private Border Artwork(double size, bool progress, bool note = false)
    {
        var content = new Grid();
        if (note) content.Children.Add(Themed(new PreviewSymbolIcon { Symbol = SymbolRegular.MusicNote220, FontSize = size * .7 }, SymbolIcon.ForegroundProperty, "AfAccentBrush"));
        else
        {
            content.Background = new LinearGradientBrush(Color.FromRgb(108, 138, 123), Color.FromRgb(211, 187, 158), 50);
            content.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0,32 L 22,6 L 44,22 L 44,44 L 0,44 Z"), Fill = new SolidColorBrush(Color.FromRgb(170, 188, 160)),
                Stretch = Stretch.Fill, Opacity = .8, Margin = new Thickness(2)
            });
        }
        return Themed(new Border { Width = size, Height = size, Child = content, ClipToBounds = true, CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(progress ? 2 : 0) }, Border.BorderBrushProperty, "AfAccentBrush");
    }
    private Button BarButton(TaskbarRestComponent component, TaskbarDensityMetrics density) => new()
    {
        Style = TryFindResource("TaskbarHoverButton") as Style, Width = density.ButtonSize, Height = density.ButtonSize,
        Content = Themed(new PreviewSymbolIcon { Symbol = ComponentSymbolConverter.Resolve(component), FontSize = density.IconSize }, SymbolIcon.ForegroundProperty, "TextFillColorPrimaryBrush")
    };
    private UIElement BuildBar(SettingsPreviewViewModel model)
    {
        if (model.IsEmpty) return Label(model.EmptyText, 13, true);
        var density = TaskbarDensityMetrics.From(model.Experience.Density);
        var line = new StackPanel { Orientation = model.IsVertical ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in model.Items)
        {
            FrameworkElement visual;
            if (item.Metric is { } metric) visual = Metric(metric, model.Performance.Visuals.Get(metric), true, model.IsVertical);
            else if (item.Component == TaskbarRestComponent.Artwork) visual = Artwork(model.Experience.SmallArtwork && !model.ShowIdle ? 32 : 40, model.Experience.ArtworkProgressVisible, model.ShowIdle);
            else if (item.Component == TaskbarRestComponent.MediaText)
            {
                var text = new StackPanel { Width = model.IsVertical ? 110 : 172, VerticalAlignment = VerticalAlignment.Center };
                var title = Label(Translations.Get("Settings.Preview.Track"), 14 * model.Experience.MediaFontSizePercent / 100d);
                title.FontWeight = FontWeights.SemiBold;
                if (model.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic)
                    title.Foreground = model.Appearance.PlayerForegroundMode == PlayerForegroundMode.LightText ? Brushes.White : new SolidColorBrush(Color.FromRgb(26, 26, 26));
                text.Children.Add(title);
                text.Children.Add(Label(Translations.Get("Settings.Preview.Artist"), 11, true));
                visual = text;
            }
            else if (item.Component == TaskbarRestComponent.Spectrum)
            {
                var spectrum = new StackPanel { Orientation = Orientation.Horizontal, Height = 30, VerticalAlignment = VerticalAlignment.Center };
                foreach (var height in new[] { 8, 18, 26, 20, 30, 16, 24, 12 })
                    spectrum.Children.Add(Themed(new Border { Width = 3, Height = height, CornerRadius = new CornerRadius(1), Margin = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, "AfAccentBrush"));
                visual = spectrum;
            }
            else if (item.Component == TaskbarRestComponent.BluetoothBattery)
            {
                var battery = new StackPanel { Orientation = Orientation.Horizontal };
                battery.Children.Add(Themed(new PreviewSymbolIcon { Symbol = SymbolRegular.Bluetooth24, FontSize = 16 }, SymbolIcon.ForegroundProperty, "TextFillColorPrimaryBrush"));
                battery.Children.Add(Label("72%", 12)); visual = battery;
            }
            else visual = BarButton(item.Component, density);
            visual.Margin = model.IsVertical ? new Thickness(0, 0, 0, model.Experience.ComponentSpacingDip) : new Thickness(0, 0, model.Experience.ComponentSpacingDip, 0);
            line.Children.Add(visual);
        }
        var surface = new StackPanel(); surface.Children.Add(line);
        if (model.IsRest && !model.ShowIdle && model.Experience.RestProgressVisible)
            surface.Children.Add(Themed(new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) }, Border.BackgroundProperty, "AfAccentBrush"));
        var bar = Themed(new Border { Child = surface, Padding = new Thickness(10, 6, 4, 6), CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(model.Appearance.ShowTaskbarBackgroundBorder ? 1 : 0) }, Border.BackgroundProperty, "AfSettingsCardBrush");
        return Themed(bar, Border.BorderBrushProperty, "AfSettingsBorderBrush");
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
            header.Children.Add(Artwork(72, false));
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(Translations.Get("Settings.Preview.Track"), 18)); text.Children.Add(Label(Translations.Get("Settings.Preview.Artist"), 12, true));
            Grid.SetColumn(text, 1); header.Children.Add(text); content.Children.Add(header);
        }
        if (settings.MediaControlsVisible)
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            controls.Children.Add(BarButton(TaskbarRestComponent.PreviousTrack, new(36, 20, 0, 0, 0)));
            controls.Children.Add(new Wpf.Ui.Controls.Button { Style = TryFindResource("PanelPlayButtonStyle") as Style, Width = 44, Height = 44, Margin = new Thickness(10, 0, 10, 0), Content = new PreviewSymbolIcon { Symbol = SymbolRegular.Pause24, FontSize = 20 } });
            controls.Children.Add(BarButton(TaskbarRestComponent.NextTrack, new(36, 20, 0, 0, 0))); content.Children.Add(controls);
            content.Children.Add(Themed(new ProgressBar { Value = 38, Maximum = 100, Height = 4, Margin = new Thickness(0, 0, 0, 12) }, ProgressBar.ForegroundProperty, "AfAccentBrush"));
        }
        if (settings.AudioControlsVisible)
        {
            var audio = new StackPanel();
            audio.Children.Add(Label(Translations.Get("Common.OutputDevice"), 12, true));
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Themed(new PreviewSymbolIcon { Symbol = SymbolRegular.Speaker224, FontSize = 18 }, SymbolIcon.ForegroundProperty, "TextFillColorPrimaryBrush"));
            var slider = new Slider { Value = 68, Maximum = 100, Margin = new Thickness(12, 0, 12, 0), IsHitTestVisible = false }; Grid.SetColumn(slider, 1); row.Children.Add(slider);
            var value = Label("68%", 13); Grid.SetColumn(value, 2); row.Children.Add(value); audio.Children.Add(row);
            var card = Card(audio); card.Margin = new Thickness(0, 0, 0, 10); content.Children.Add(card);
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
