// 分类概览只呈现导航卡片；页面由设置窗口作用域持有，离页释放语言订阅。
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Components;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Views.Pages;

/// <summary>按导航目录呈现当前分类可用的二级设置页。</summary>
public partial class SettingsCategoryPage : Page
{
    private SettingsSectionDefinition? _section;
    private SettingsMode _mode;
    public string? SectionId => _section?.Id;

    public SettingsCategoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Translations.LanguageChanged -= OnLanguageChanged;
            Translations.LanguageChanged += OnLanguageChanged;
            Rebuild();
        };
        Unloaded += (_, _) => Translations.LanguageChanged -= OnLanguageChanged;
    }

    public void ShowSection(SettingsSectionDefinition section, SettingsMode mode)
    {
        _section = section;
        _mode = mode;
        CategoryHeader.SetResourceReference(SettingsGroupStrip.TitleProperty, "Loc." + section.TitleKey);
        CategoryHeader.PageIcon = new SymbolIcon(Enum.Parse<SymbolRegular>(section.IconName));
        Rebuild();
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => Rebuild();

    private void Rebuild()
    {
        PageRoot.Children.Clear();
        if (_section is null) return;
        foreach (var key in _section.Pages)
        {
            if (SettingsPageCatalog.Find(key, _mode) is not { } definition) continue;
            var icon = new SymbolIcon(Enum.Parse<SymbolRegular>(definition.IconName))
            { FontSize = 20, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorPrimaryBrush");
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.TextProperty, "Loc." + definition.TitleKey);
            labels.Children.Add(title);
            if (definition.DescriptionKey is { } descriptionKey)
            {
                var description = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
                description.SetResourceReference(TextBlock.TextProperty, "Loc." + descriptionKey);
                description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                labels.Children.Add(description);
            }
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(icon);
            Grid.SetColumn(labels, 1);
            content.Children.Add(labels);
            var card = new SettingsNavigationButton { Destination = key, Content = content };
            card.SetResourceReference(StyleProperty, "AfSettingsCategoryCardStyle");
            AutomationProperties.SetName(card, Translations.Get(definition.TitleKey));
            PageRoot.Children.Add(card);
        }
    }
}
