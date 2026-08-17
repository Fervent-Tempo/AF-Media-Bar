using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;
// System.Windows.Localization（枚举）与本地化帮助类同名，用别名消歧。
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsCoordinator _coordinator;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _scaleSaveTimer;
    private readonly DispatcherTimer _fontSaveTimer;
    private IReadOnlyList<SettingsSearchResult> _searchResults = [];
    private CancellationTokenSource? _updateCheckCancellation;
    private UpdateInfo? _displayedRelease;
    private bool _isInitialized;
    private bool _isSyncing = true;

    internal SettingsWindow(SettingsCoordinator coordinator, UpdateService updateService)
    {
        _coordinator = coordinator;
        _updateService = updateService;
        _scaleSaveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(360),
            DispatcherPriority.Background,
            ScaleSaveTimer_OnTick,
            Dispatcher);
        _scaleSaveTimer.Stop();
        _fontSaveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(240),
            DispatcherPriority.Background,
            FontWeightSaveTimer_OnTick,
            Dispatcher);
        _fontSaveTimer.Stop();
        InitializeComponent();
        _searchResults = BuildSearchResults();
        _isInitialized = true;
        VersionText.Text = Loc.Get(
            "Settings.VersionFormat",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Loc.Get("Settings.VersionDev"));
        _coordinator.Changed += Coordinator_OnChanged;
        _updateService.UpdateAvailable += UpdateService_OnUpdateAvailable;
        Closed += SettingsWindow_OnClosed;
        SyncFromSettings();
        if (_updateService.LatestRelease is { } release)
        {
            ShowRelease(release, release.Version > _updateService.CurrentVersion);
        }
    }

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

        MetricsEnabledCheckBox.IsChecked = settings.Metrics.Enabled;
        SystemMemoryCheckBox.IsChecked = settings.Metrics.ShowSystemMemory;
        SystemCpuCheckBox.IsChecked = settings.Metrics.ShowSystemCpu;
        SystemGpuCheckBox.IsChecked = settings.Metrics.ShowSystemGpu;
        ProcessMemoryCheckBox.IsChecked = settings.Metrics.ShowProcessMemory;
        AudioMonitorCheckBox.IsChecked = settings.Metrics.AudioMonitorEnabled;
        OutputDeviceCheckBox.IsChecked = settings.Metrics.OutputDeviceSwitcherEnabled;
        VolumeControlCheckBox.IsChecked = settings.Metrics.VolumeControlEnabled;
        OpenTaskManagerOnMetricsClickCheckBox.IsChecked =
            settings.Metrics.OpenTaskManagerOnMetricsClick;
        LowGpuModeCheckBox.IsChecked = settings.Metrics.LowGpuMode;
        ShowArtworkCheckBox.IsChecked = settings.Window.ShowArtwork;
        RoundedArtworkCheckBox.IsChecked = settings.Window.RoundedArtwork;
        ShowMediaInfoCheckBox.IsChecked = settings.Window.ShowMediaInfo;

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
        FontPreviewText.FontWeight = FontSettings.ResolveTitleWeight(settings.Font.Weight);

        AutoCollapseCheckBox.IsChecked = settings.Window.AutoCollapse;
        EdgeAutoCollapseCheckBox.IsChecked = settings.Window.EdgeAutoCollapse;
        AlwaysOnTopCheckBox.IsChecked = settings.Window.AlwaysOnTop;

        LanguageFollowSystemRadioButton.IsChecked = settings.Language == AppLanguage.FollowSystem;
        LanguageZhCnRadioButton.IsChecked = settings.Language == AppLanguage.ZhCn;
        LanguageZhTwRadioButton.IsChecked = settings.Language == AppLanguage.ZhTw;
        LanguageEnUsRadioButton.IsChecked = settings.Language == AppLanguage.EnUs;
        _isSyncing = false;
        RebuildSearchIndex();
        UpdateDependencies();
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
        new(SectionTag.Components, Loc.Get("Settings.Components.ShowArtworkTitle"), Loc.Get("Search.Kw.Artwork")),
        new(SectionTag.Components, Loc.Get("Settings.Components.RoundedArtworkTitle"), Loc.Get("Search.Kw.Artwork")),
        new(SectionTag.Components, Loc.Get("Settings.Components.ShowMediaInfoTitle"), Loc.Get("Search.Kw.MediaInfo")),
        new(SectionTag.Components, Loc.Get("Settings.Components.MetricsTitle"), Loc.Get("Search.Kw.Metrics")),
        new(SectionTag.Components, Loc.Get("Settings.Components.OpenTaskManagerTitle"), Loc.Get("Search.Kw.TaskManager")),
        new(SectionTag.Components, Loc.Get("Settings.Components.SpectrumTitle"), Loc.Get("Search.Kw.Spectrum")),
        new(SectionTag.Components, Loc.Get("Settings.Components.OutputSwitchTitle"), Loc.Get("Search.Kw.OutputSwitch")),
        new(SectionTag.Components, Loc.Get("Settings.Components.MediaVolumeTitle"), Loc.Get("Search.Kw.MediaVolume")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.WindowMode"), Loc.Get("Search.Kw.WindowMode")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.Arrangement"), Loc.Get("Search.Kw.Arrangement")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.Size"), Loc.Get("Search.Kw.Scale")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.TopOffset"), Loc.Get("Search.Kw.TopOffset")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.AvoidTaskbarTitle"), Loc.Get("Search.Kw.AvoidTaskbar")),
        new(SectionTag.Layout, Loc.Get("Settings.Layout.LockPositionTitle"), Loc.Get("Search.Kw.LockPosition")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.PlayerText"), Loc.Get("Search.Kw.PlayerText")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.Fonts"), Loc.Get("Search.Kw.Fonts")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.FontWeight"), Loc.Get("Search.Kw.FontWeight")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.ReadabilityTitle"), Loc.Get("Search.Kw.Readability")),
        new(SectionTag.Appearance, Loc.Get("Settings.Appearance.MenuTheme"), Loc.Get("Search.Kw.MenuTheme")),
        new(SectionTag.Interaction, Loc.Get("Settings.Interaction.AutoCollapseTitle"), Loc.Get("Search.Kw.AutoCollapse")),
        new(SectionTag.Interaction, Loc.Get("Settings.Interaction.EdgeCollapseTitle"), Loc.Get("Search.Kw.EdgeCollapse")),
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
        MetricsEnabledCheckBox.IsEnabled = true;
        SystemMemoryCheckBox.IsEnabled = settings.Metrics.Enabled;
        SystemCpuCheckBox.IsEnabled = settings.Metrics.Enabled;
        SystemGpuCheckBox.IsEnabled = settings.Metrics.Enabled;
        ProcessMemoryCheckBox.IsEnabled = settings.Metrics.Enabled;
        OpenTaskManagerOnMetricsClickCheckBox.IsEnabled = settings.Metrics.SelectedCount > 0;
        RoundedArtworkCheckBox.IsEnabled = settings.Window.ShowArtwork;
        AutomaticPlacementCheckBox.IsEnabled = canUseAutomaticPlacement;
        TaskbarTopOffsetSlider.IsEnabled = taskbarMode && !forcedVertical;
        AutomaticPlacementDescription.Text = canUseAutomaticPlacement
            ? Loc.Get("Settings.Layout.AvoidTaskbarDockDescription")
            : Loc.Get("Settings.Layout.AvoidTaskbarUnsupportedDescription");
        LockPositionCheckBox.IsEnabled = taskbarMode && !settings.Placement.AutomaticPlacement;
        EdgeAutoCollapseCheckBox.IsEnabled = !taskbarMode;
        EdgeAutoCollapseDescription.Text = taskbarMode
            ? Loc.Get("Settings.Interaction.EdgeCollapseFloatingDescription")
            : Loc.Get("Settings.Interaction.EdgeCollapseNormalDescription");
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
        ComponentsPage.Visibility = tag == "Components" ? Visibility.Visible : Visibility.Collapsed;
        LayoutPage.Visibility = tag == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        InteractionPage.Visibility = tag == "Interaction" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePage.Visibility = tag == "Performance" ? Visibility.Visible : Visibility.Collapsed;
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
        ComponentsPage.Visibility = Visibility.Collapsed;
        LayoutPage.Visibility = Visibility.Collapsed;
        AppearancePage.Visibility = Visibility.Collapsed;
        InteractionPage.Visibility = Visibility.Collapsed;
        PerformancePage.Visibility = Visibility.Collapsed;
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
            "Components" => 1,
            "Layout" => 2,
            "Appearance" => 3,
            "Interaction" => 4,
            _ => 5
        };
        SearchResultsList.SelectedIndex = -1;
        SearchBox.Clear();
        ShowPage(pageTag);
    }

    private void GeneralCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        TryUpdate(() => _coordinator.UpdateStartup(StartupCheckBox.IsChecked == true));
    }

    private void LanguageRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var language = LanguageFollowSystemRadioButton.IsChecked == true
            ? AppLanguage.FollowSystem
            : LanguageZhCnRadioButton.IsChecked == true
                ? AppLanguage.ZhCn
                : LanguageZhTwRadioButton.IsChecked == true
                    ? AppLanguage.ZhTw
                    : AppLanguage.EnUs;
        TryUpdate(() => _coordinator.UpdateLanguage(language));
    }

    private void MetricCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        TryUpdate(() => _coordinator.UpdateMetrics(new MetricSettings(
            MetricsEnabledCheckBox.IsChecked == true,
            SystemMemoryCheckBox.IsChecked == true,
            SystemCpuCheckBox.IsChecked == true,
            SystemGpuCheckBox.IsChecked == true,
            ProcessMemoryCheckBox.IsChecked == true,
            LowGpuModeCheckBox.IsChecked == true,
            AudioMonitorCheckBox.IsChecked == true,
            OutputDeviceCheckBox.IsChecked == true,
            VolumeControlCheckBox.IsChecked == true,
            OpenTaskManagerOnMetricsClickCheckBox.IsChecked == true)));
    }

    private void WindowCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        UpdateWindowSettings();
    }

    private void WindowRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        UpdateWindowSettings();
    }

    private void UpdateWindowSettings()
    {
        var current = _coordinator.Current.Window;
        var hostMode = FloatingModeRadioButton.IsChecked == true
            ? WindowHostMode.Floating
            : WindowHostMode.Taskbar;
        var layoutMode = VerticalLayoutRadioButton.IsChecked == true
            ? PlayerLayoutMode.Vertical
            : HorizontalLayoutRadioButton.IsChecked == true
                ? PlayerLayoutMode.Horizontal
                : PlayerLayoutMode.Automatic;
        var settings = current with
        {
            HideWhenNoMedia = HideWhenNoMediaCheckBox.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true,
            HostMode = hostMode,
            LayoutMode = layoutMode,
            LengthScalePercent = (int)Math.Round(LengthScaleSlider.Value),
            ThicknessScalePercent = (int)Math.Round(ThicknessScaleSlider.Value),
            AutoCollapse = AutoCollapseCheckBox.IsChecked == true,
            EdgeAutoCollapse = EdgeAutoCollapseCheckBox.IsChecked == true,
            ShowArtwork = ShowArtworkCheckBox.IsChecked == true,
            RoundedArtwork = RoundedArtworkCheckBox.IsChecked == true,
            ShowMediaInfo = ShowMediaInfoCheckBox.IsChecked == true
        };
        TryUpdate(() => _coordinator.UpdateWindow(settings));
    }

    private void ScaleSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        LengthScaleValueText.Text = $"{Math.Round(LengthScaleSlider.Value):0}%";
        ThicknessScaleValueText.Text = $"{Math.Round(ThicknessScaleSlider.Value):0}%";
        TaskbarTopOffsetValueText.Text =
            $"{Math.Round(TaskbarTopOffsetSlider.Value):+0;-0;0}";
        if (_isSyncing)
        {
            return;
        }

        _scaleSaveTimer.Stop();
        _scaleSaveTimer.Start();
    }

    private void ScaleSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _scaleSaveTimer.Stop();
        UpdateWindowSettings();
        var currentPlacement = _coordinator.Current.Placement;
        TryUpdate(() => _coordinator.UpdatePlacement(currentPlacement with
        {
            TaskbarTopOffsetDip = (int)Math.Round(TaskbarTopOffsetSlider.Value)
        }));
    }

    private void PlacementCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Placement;
        var automatic = AutomaticPlacementCheckBox.IsChecked == true;
        var locked = LockPositionCheckBox.IsChecked == true || automatic;
        if (automatic)
        {
            _isSyncing = true;
            LockPositionCheckBox.IsChecked = true;
            _isSyncing = false;
        }

        TryUpdate(() => _coordinator.UpdatePlacement(current with
        {
            AutomaticPlacement = automatic,
            PositionLocked = locked,
            VerticalPositionLocked = locked
        }));
    }

    private void ResetPosition_OnClick(object sender, RoutedEventArgs e)
    {
        var currentWindow = _coordinator.Current.Window with
        {
            FloatingLeft = null,
            FloatingTop = null
        };
        TryUpdate(() =>
        {
            _coordinator.UpdatePlacement(PlacementSettings.Default);
            _coordinator.UpdateWindow(currentWindow);
        });
    }

    private void ThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightForegroundRadioButton.IsChecked == true
            ? TaskbarForegroundMode.LightText
            : DarkForegroundRadioButton.IsChecked == true
                ? TaskbarForegroundMode.DarkText
                : TaskbarForegroundMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            TaskbarForegroundMode = mode
        }));
    }

    private void MenuThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightMenuThemeRadioButton.IsChecked == true
            ? MenuThemeMode.Light
            : DarkMenuThemeRadioButton.IsChecked == true
                ? MenuThemeMode.Dark
                : MenuThemeMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            MenuThemeMode = mode
        }));
    }

    private void ThemeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            EnhancedReadability = EnhancedReadabilityCheckBox.IsChecked == true
        }));
    }

    private void FontComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        if (FontLatinComboBox.SelectedIndex < 0 ||
            FontCjkComboBox.SelectedIndex < 0 ||
            !Enum.IsDefined(typeof(LatinFontPreset), FontLatinComboBox.SelectedIndex) ||
            !Enum.IsDefined(typeof(CjkFontPreset), FontCjkComboBox.SelectedIndex))
        {
            return;
        }

        var latin = (LatinFontPreset)FontLatinComboBox.SelectedIndex;
        var cjk = (CjkFontPreset)FontCjkComboBox.SelectedIndex;
        var weight = _coordinator.Current.Font.Weight;
        TryUpdate(() => _coordinator.UpdateFont(new FontSettings(latin, cjk, weight)));
        FontPreviewText.FontFamily = new FontFamily(FontSettings.ResolveText(latin, cjk));
        FontPreviewText.FontWeight = FontSettings.ResolveTitleWeight(weight);
    }

    private void FontWeightSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var weight = FontSettings.NormalizeWeight((int)Math.Round(e.NewValue));
        FontWeightValueText.Text = FormatFontWeight(weight);
        FontPreviewText.FontWeight = FontSettings.ResolveTitleWeight(weight);
        if (_isSyncing)
        {
            return;
        }

        _fontSaveTimer.Stop();
        _fontSaveTimer.Start();
    }

    private void FontWeightSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _fontSaveTimer.Stop();
        var current = _coordinator.Current.Font;
        var weight = FontSettings.NormalizeWeight((int)Math.Round(FontWeightSlider.Value));
        TryUpdate(() => _coordinator.UpdateFont(current with { Weight = weight }));
    }

    private static string FormatFontWeight(int weight)
    {
        var normalizedWeight = FontSettings.NormalizeWeight(weight);
        var nameKey = normalizedWeight switch
        {
            < 350 => "Settings.Appearance.FontWeightThin",
            < 450 => "Settings.Appearance.FontWeightLight",
            < 550 => "Settings.Appearance.FontWeightStandard",
            < 650 => "Settings.Appearance.FontWeightMedium",
            < 750 => "Settings.Appearance.FontWeightSemiBold",
            < 850 => "Settings.Appearance.FontWeightBold",
            _ => "Settings.Appearance.FontWeightBlack"
        };
        return Loc.Get(
            "Settings.Appearance.FontWeightValueFormat",
            normalizedWeight,
            Loc.Get(nameKey));
    }

    private void UpdateCheckSetting_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        _updateService.SetAutomaticChecksEnabled(
            AutomaticUpdateChecksCheckBox.IsChecked == true);
        UpdateStatusText.Text = AutomaticUpdateChecksCheckBox.IsChecked == true
            ? Loc.Get("Settings.Update.AutoCheckEnabled")
            : Loc.Get("Settings.Update.AutoCheckDisabled");
    }

    private async void CheckForUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        // 手动检查使用独立超时，并在窗口关闭时取消，避免异步回调访问已关闭的界面。 / Manual checks use an independent timeout and are canceled on close so callbacks do not touch a closed window.
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _updateCheckCancellation = cancellation;
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = Loc.Get("Settings.Update.Checking");

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(
                force: true,
                cancellation.Token);
            ApplyUpdateCheckResult(result);
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested || IsVisible)
            {
                UpdateStatusText.Text = Loc.Get("Settings.Update.Timeout");
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("manual-update-check", exception);
            if (IsVisible)
            {
                UpdateStatusText.Text = Loc.Get("Settings.Update.FailedFormat", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_updateCheckCancellation, cancellation))
            {
                _updateCheckCancellation = null;
                cancellation.Dispose();
                CheckForUpdatesButton.IsEnabled = true;
            }
        }
    }

    private void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                ShowRelease(update, updateAvailable: true);
                UpdateDetailsPanel.BringIntoView();
                break;
            case UpdateCheckStatus.NoUpdate when result.Update is { } release:
                ShowRelease(release, updateAvailable: false);
                break;
            case UpdateCheckStatus.AlreadyChecking:
                UpdateStatusText.Text = Loc.Get("Settings.Update.InProgress");
                break;
            case UpdateCheckStatus.NotDue:
                UpdateStatusText.Text = Loc.Get("Settings.Update.CheckedToday");
                break;
            default:
                UpdateStatusText.Text = result.ErrorMessage ?? Loc.Get("Settings.Update.Failed");
                break;
        }
    }

    private void UpdateService_OnUpdateAvailable(object? sender, UpdateInfo update)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateService_OnUpdateAvailable(sender, update));
            return;
        }

        ShowRelease(update, updateAvailable: true);
    }

    private void ShowRelease(UpdateInfo release, bool updateAvailable)
    {
        _displayedRelease = release;
        UpdateDetailsPanel.Visibility = Visibility.Visible;
        UpdateTitleText.Text = release.Title;
        var releaseDate = release.ReleaseDate is { } date
            ? $" · {date:yyyy-MM-dd}"
            : string.Empty;
        UpdateVersionText.Text = Loc.Get("Settings.Update.VersionFormat", release.VersionText, releaseDate);
        UpdateChangelogText.Text = release.Changelog.Count == 0
            ? Loc.Get("Settings.Update.NoReleaseNotes")
            : string.Join(
                Environment.NewLine,
                release.Changelog.Select(item => $"• {item}"));

        SetUpdateLink(GitHubDownloadButton, release, "github");
        SetUpdateLink(QuarkDownloadButton, release, "quark");
        SetUpdateLink(BaiduDownloadButton, release, "baidu");
        SetUpdateLink(LanzouDownloadButton, release, "lanzou");
        SetUpdateLink(ReleaseNotesButton, release.ReleaseNotesUri);

        var skipped = updateAvailable &&
            !release.Mandatory &&
            _updateService.IsVersionSkipped(release.Version);
        UpdateStatusText.Text = updateAvailable
            ? skipped
                ? Loc.Get("Settings.Update.IgnoredFormat", release.VersionText)
                : release.Mandatory
                    ? Loc.Get("Settings.Update.MajorAvailable", release.VersionText)
                    : Loc.Get("Settings.Update.NewAvailable", release.VersionText)
            : Loc.Get("Settings.Update.UpToDate", release.VersionText);
        SkipUpdateButton.Visibility = updateAvailable && !release.Mandatory && !skipped
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void SetUpdateLink(Button button, UpdateInfo release, string channel)
    {
        SetUpdateLink(
            button,
            release.DownloadUris.TryGetValue(channel, out var uri) ? uri : null);
    }

    private static void SetUpdateLink(Button button, Uri? uri)
    {
        button.Tag = uri?.AbsoluteUri;
        button.Visibility = uri is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SkipUpdate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_displayedRelease is not { Mandatory: false } release ||
            release.Version <= _updateService.CurrentVersion)
        {
            return;
        }

        _updateService.SkipVersion(release.Version);
        SkipUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = Loc.Get("Settings.Update.IgnoredNowFormat", release.VersionText);
    }

    private void ResetDefaults_OnClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            Loc.Get("Msg.ResetBody"),
            Loc.Get("Msg.ResetTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        TryUpdate(() =>
        {
            _updateService.SetAutomaticChecksEnabled(true);
            _updateService.ClearSkippedVersion();
            _coordinator.ResetAll();
            if (_displayedRelease is { } release)
            {
                ShowRelease(release, release.Version > _updateService.CurrentVersion);
            }
        });
    }

    private void OpenLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-external-link", exception, url);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenLinkFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenLogFile_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = DiagnosticsLogService.EnsureLogFile();
            DiagnosticsLogService.Write("log-file-opened");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-log-file", exception);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenLogFileFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.RequestMediaReconnect();
    }

    private void TryUpdate(Action update)
    {
        try
        {
            update();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("save-settings", exception);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.SaveSettingsFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SyncFromSettings();
        }
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        _updateCheckCancellation = null;
        _scaleSaveTimer.Stop();
        _fontSaveTimer.Stop();
        _coordinator.Changed -= Coordinator_OnChanged;
        _updateService.UpdateAvailable -= UpdateService_OnUpdateAvailable;
        Closed -= SettingsWindow_OnClosed;
    }

    /// <summary>
    /// 设置页面的稳定标识；搜索跳转不再依赖本地化页面名。
    /// </summary>
    private static class SectionTag
    {
        internal const string General = "General";
        internal const string Components = "Components";
        internal const string Layout = "Layout";
        internal const string Appearance = "Appearance";
        internal const string Interaction = "Interaction";
        internal const string Performance = "Performance";
    }

    private sealed record SettingsSearchResult(
        string SectionTag,
        string Title,
        string Keywords)
    {
        internal string PageTitle => Loc.Get($"Settings.Nav.{SectionTag}");
    }
}
