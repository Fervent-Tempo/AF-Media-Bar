using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;
/// <summary>
/// 负责设置快照同步、搜索索引和页面导航，不直接持久化任何设置。
/// Coordinates settings snapshots, search indexing, and page navigation without persisting settings directly.
/// </summary>
public partial class SettingsWindow
{
    private IReadOnlyList<SettingsSearchResult> _searchResults = [];

    private void Coordinator_OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SyncFromSettings());
            return;
        }

        SyncFromSettings();
    }

    private void SyncFromSettings()
    {
        var settings = _coordinator.Current;
        _scaleSaveTimer.Stop();
        _isSyncing = true;
        StartupCheckBox.IsChecked = settings.StartupEnabled;
        HideWhenNoMediaCheckBox.IsChecked = settings.Window.HideWhenNoMedia;
        AutomaticUpdateChecksCheckBox.IsChecked = _updateService.AutomaticChecksEnabled;

        LowGpuModeCheckBox.IsChecked = settings.Metrics.LowGpuMode;

        TaskbarModeRadioButton.IsChecked = settings.Window.HostMode == WindowHostMode.Taskbar;
        FloatingModeRadioButton.IsChecked = settings.Window.HostMode == WindowHostMode.Floating;
        AutomaticLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Automatic;
        HorizontalLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Horizontal;
        VerticalLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Vertical;
        LengthScaleSlider.Value = settings.Window.LengthScalePercent;
        LengthScaleValueText.Text = $"{settings.Window.LengthScalePercent}%";
        ThicknessScaleSlider.Value = settings.Window.ThicknessScalePercent;
        ThicknessScaleValueText.Text = $"{settings.Window.ThicknessScalePercent}%";
        TaskbarTopOffsetSlider.Value = settings.Placement.TaskbarTopOffsetDip;
        TaskbarTopOffsetValueText.Text = $"{settings.Placement.TaskbarTopOffsetDip:+0;-0;0}";

        AutomaticPlacementCheckBox.IsChecked = settings.Placement.AutomaticPlacement;
        LockPositionCheckBox.IsChecked = settings.Window.HostMode == WindowHostMode.Taskbar &&
            (settings.Window.LayoutMode == PlayerLayoutMode.Vertical
                ? settings.Placement.VerticalPositionLocked
                : settings.Placement.PositionLocked);

        AutomaticForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.Automatic;
        LightForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.LightText;
        DarkForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.DarkText;
        AutomaticMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Automatic;
        LightMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Light;
        DarkMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Dark;
        EnhancedReadabilityCheckBox.IsChecked = settings.Theme.EnhancedReadability;

        FontLatinComboBox.SelectedIndex = (int)settings.Font.Latin;
        FontCjkComboBox.SelectedIndex = (int)settings.Font.Cjk;
        FontWeightSlider.Value = FontSettings.NormalizeWeight(settings.Font.Weight);
        FontWeightValueText.Text = FormatFontWeight(settings.Font.Weight);
        FontPreviewText.FontFamily = new FontFamily(FontSettings.ResolveText(
            settings.Font.Latin,
            settings.Font.Cjk));
        FontPreviewText.FontWeight = WpfFontSettingsAdapter.ResolveTitleWeight(settings.Font.Weight);

        AlwaysOnTopCheckBox.IsChecked = settings.Window.AlwaysOnTop;

        LanguageFollowSystemRadioButton.IsChecked = settings.Language == AppLanguage.FollowSystem;
        LanguageZhCnRadioButton.IsChecked = settings.Language == AppLanguage.ZhCn;
        LanguageZhTwRadioButton.IsChecked = settings.Language == AppLanguage.ZhTw;
        LanguageEnUsRadioButton.IsChecked = settings.Language == AppLanguage.EnUs;
        _isSyncing = false;
        RebuildSearchIndex();
        UpdateDependencies();
        if (!_layoutEditorResizeInProgress)
        {
            SyncLayoutEditor();
        }
    }

    /// <summary>
    /// 按当前语言重建搜索条目；已有查询时用新语言重新过滤并刷新结果页。
    /// </summary>
    private void RebuildSearchIndex()
    {
        _searchResults = BuildSearchResults();
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            ApplySearchQuery(SearchBox.Text.Trim());
        }
    }

    private static IReadOnlyList<SettingsSearchResult> BuildSearchResults() =>
    [
        new(SectionTag.General, Loc.Get("Settings.General.AutoStartTitle"), Loc.Get("Search.Kw.AutoStart")),
        new(SectionTag.General, Loc.Get("Settings.General.HideWhenNoMediaTitle"), Loc.Get("Search.Kw.HideWhenNoMedia")),
        new(SectionTag.General, Loc.Get("Settings.General.AutoCheckUpdateTitle"), Loc.Get("Search.Kw.AutoCheckUpdate")),
        new(SectionTag.General, Loc.Get("Settings.Language.SectionTitle"), Loc.Get("Search.Kw.Language")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.WindowMode"), Loc.Get("Search.Kw.WindowMode")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.Arrangement"), Loc.Get("Search.Kw.Arrangement")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.Size"), Loc.Get("Search.Kw.Scale")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.TopOffset"), Loc.Get("Search.Kw.TopOffset")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorTitle"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorProperties"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorCurrentContext"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorPalette"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorContainers"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorEdgeContainer"), Loc.Get("Search.Kw.EdgeCollapse")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyResetDefault"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyResetContainerDefault"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyAlignment"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyNearAlignment"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyProximity"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.EditorAdvancedBehavior"), Loc.Get("Search.Kw.LayoutEditor")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyMaxLines"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyMaxLinesHint"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.LayoutWidget.ArtworkTitle"), Loc.Get("Search.Kw.Artwork")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyArtworkOpenSource"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.LayoutWidget.MediaTextTitle"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyTextTitleAndArtist"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.LayoutEditor, Loc.Get("Main.Control.Previous"), Loc.Get("Search.Kw.MediaControls")),
        new(SectionTag.LayoutEditor, Loc.Get("Main.Control.Play"), Loc.Get("Search.Kw.MediaControls")),
        new(SectionTag.LayoutEditor, Loc.Get("Main.Control.Next"), Loc.Get("Search.Kw.MediaControls")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.LayoutWidget.MetricsTitle"), Loc.Get("Search.Kw.Metrics")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.Layout.PropertyOpenTaskManager"), Loc.Get("Search.Kw.TaskManager")),
        new(SectionTag.LayoutEditor, Loc.Get("Settings.LayoutWidget.SpectrumTitle"), Loc.Get("Search.Kw.Spectrum")),
        new(SectionTag.LayoutEditor, Loc.Get("Main.Device.Output"), Loc.Get("Search.Kw.OutputSwitch")),
        new(SectionTag.LayoutEditor, Loc.Get("Main.Volume.Current"), Loc.Get("Search.Kw.MediaVolume")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.AvoidTaskbarTitle"), Loc.Get("Search.Kw.AvoidTaskbar")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.LockPositionTitle"), Loc.Get("Search.Kw.LockPosition")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.PlayerText"), Loc.Get("Search.Kw.PlayerText")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.Fonts"), Loc.Get("Search.Kw.Fonts")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.FontWeight"), Loc.Get("Search.Kw.FontWeight")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.ReadabilityTitle"), Loc.Get("Search.Kw.Readability")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.MenuTheme"), Loc.Get("Search.Kw.MenuTheme")),
        new(SectionTag.Interaction, Loc.Get("Settings.Interaction.TopMostTitle"), Loc.Get("Search.Kw.TopMost")),
        new(SectionTag.Performance, Loc.Get("Settings.Performance.LowPerfTitle"), Loc.Get("Search.Kw.LowPerf")),
        new(SectionTag.Performance, Loc.Get("Settings.Performance.Reconnect"), Loc.Get("Search.Kw.Reconnect"))
    ];

    private void UpdateDependencies()
    {
        var settings = _coordinator.Current;
        var taskbarMode = settings.Window.HostMode == WindowHostMode.Taskbar;
        var forcedVertical = settings.Window.LayoutMode == PlayerLayoutMode.Vertical;
        var canUseAutomaticPlacement = taskbarMode && !forcedVertical;
        AutomaticPlacementCheckBox.IsEnabled = canUseAutomaticPlacement;
        TaskbarTopOffsetSlider.IsEnabled = taskbarMode && !forcedVertical;
        AutomaticPlacementDescription.Text = canUseAutomaticPlacement
            ? Loc.Get("Settings.Layout.AvoidTaskbarDockDescription")
            : Loc.Get("Settings.Layout.AvoidTaskbarUnsupportedDescription");
        LockPositionCheckBox.IsEnabled = taskbarMode && !settings.Placement.AutomaticPlacement;
    }

    private void NavigationList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (NavigationList.SelectedItem is ListBoxItem { Tag: string tag })
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Clear();
            }

            ShowPage(tag);
        }
    }

    private void ShowPage(string tag, FrameworkElement? target = null)
    {
        SearchResultsPage.Visibility = Visibility.Collapsed;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        LayoutPage.Visibility = tag == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        LayoutEditorPage.Visibility = tag == "LayoutEditor" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "LayoutEditor")
        {
            // 进入布局编辑器时重建预览，确保此前清理的表面与框线恢复。
            SyncLayoutEditor();
        }
        else
        {
            // 切离布局编辑器时释放预览表面与全部选择/边界 Adorner，避免框线残留在其页面或布局页上。
            DisposeLayoutEditorSurfaces();
            ClearSkinPreview();
        }
        AppearancePage.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        InteractionPage.Visibility = tag == "Interaction" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePage.Visibility = tag == "Performance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.VerticalScrollBarVisibility = tag == "LayoutEditor"
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        UpdateLayoutEditorPageHeight();
        SettingsPageScrollViewer.ScrollToTop();
        if (target is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => target.BringIntoView());
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (string.IsNullOrEmpty(query))
        {
            SearchResultsList.ItemsSource = null;
            SearchResultsList.Visibility = Visibility.Collapsed;
            SearchEmptyText.Visibility = Visibility.Collapsed;
            if (NavigationList.SelectedItem is ListBoxItem { Tag: string tag })
            {
                ShowPage(tag);
            }
            return;
        }

        ApplySearchQuery(query);
    }

    private void ApplySearchQuery(string query)
    {
        var results = _searchResults
            .Where(result => result.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                result.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SearchResultsList.ItemsSource = results;
        SearchResultsSummaryText.Text = results.Length == 0
            ? Loc.Get("Settings.Search.NoMatchesFormat", query)
            : Loc.Get("Settings.Search.MatchesFormat", results.Length, query);
        SearchResultsList.Visibility = results.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SearchEmptyText.Visibility = results.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchResultsPage.Visibility = Visibility.Visible;
        GeneralPage.Visibility = Visibility.Collapsed;
        LayoutPage.Visibility = Visibility.Collapsed;
        LayoutEditorPage.Visibility = Visibility.Collapsed;
        AppearancePage.Visibility = Visibility.Collapsed;
        InteractionPage.Visibility = Visibility.Collapsed;
        PerformancePage.Visibility = Visibility.Collapsed;
        SettingsPageScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SettingsPageScrollViewer.ScrollToTop();
    }

    private void SearchResults_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (SearchResultsList.SelectedItem is not SettingsSearchResult result)
        {
            return;
        }

        var pageTag = result.SectionTag;
        NavigationList.SelectedIndex = pageTag switch
        {
            "General" => 0,
            "Layout" => 1,
            "LayoutEditor" => 2,
            "Appearance" => 3,
            "Interaction" => 4,
            _ => 5
        };
        SearchResultsList.SelectedIndex = -1;
        SearchBox.Clear();
        ShowPage(pageTag);
    }

    /// <summary>
    /// 设置页面的稳定标识；搜索跳转不依赖会随语言变化的页面标题。
    /// Stable page identifiers keep search navigation independent of localized titles.
    /// </summary>
    private static class SectionTag
    {
        internal const string General = "General";
        internal const string Layout = "Layout";
        internal const string LayoutEditor = "LayoutEditor";
        internal const string Appearance = "Appearance";
        internal const string Interaction = "Interaction";
        internal const string Performance = "Performance";
    }

    private void SettingsPageScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLayoutEditorPageHeight();

    private void UpdateLayoutEditorPageHeight()
    {
        if (LayoutEditorPage is null || SettingsPageScrollViewer is null)
        {
            return;
        }

        LayoutEditorPage.Height = Math.Max(420, SettingsPageScrollViewer.ActualHeight - 20);
    }

    private sealed record SettingsSearchResult(
        string SectionTag,
        string Title,
        string Keywords)
    {
        internal string PageTitle => Loc.Get($"Settings.Nav.{SectionTag}");
    }

}
