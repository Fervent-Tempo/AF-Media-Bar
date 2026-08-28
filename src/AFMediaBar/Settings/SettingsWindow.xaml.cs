using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
// System.Windows.Localization（枚举）与本地化帮助类同名，用别名消歧。
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;

/// <summary>
/// 负责设置窗口生命周期和共享协调状态；具体页面行为由领域 partial 模块处理。
/// Owns settings-window lifecycle and shared coordination state while focused partial modules handle page behavior.
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsCoordinator _coordinator;
    private readonly UpdateService _updateService;
    private readonly SettingsWindowViewModel _viewModel;
    private readonly DispatcherTimer _scaleSaveTimer;
    private readonly DispatcherTimer _fontSaveTimer;
    private readonly SystemThemeService? _systemThemeService;
    private ApplicationTheme? _appliedWpfUiTheme;
    private bool _isInitialized;
    private bool _isSyncing = true;

    internal SettingsWindow(
        SettingsCoordinator coordinator,
        UpdateService updateService,
        SettingsWindowViewModel viewModel)
    {
        _coordinator = coordinator;
        _updateService = updateService;
        _viewModel = viewModel;
        DataContext = _viewModel;
        _systemThemeService = (Application.Current as App)?.ThemeService;
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
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        InitializeLayoutEditor();
        _searchResults = BuildSearchResults();
        _isInitialized = true;
        VersionText.Text = Loc.Get(
            "Settings.VersionFormat",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Loc.Get("Settings.VersionDev"));
        _coordinator.Changed += Coordinator_OnChanged;
        _updateService.UpdateAvailable += UpdateService_OnUpdateAvailable;
        if (_systemThemeService is not null)
        {
            _systemThemeService.ThemeApplied += SystemThemeService_OnThemeApplied;
        }
        Closed += SettingsWindow_OnClosed;
        ApplyWpfUiTheme();
        SyncFromSettings();
        if (_updateService.LatestRelease is { } release)
        {
            ShowRelease(release, release.Version > _updateService.CurrentVersion);
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.IsNavigationPaneExpanded))
        {
            NavigationPaneIcon.Symbol = _viewModel.IsNavigationPaneExpanded
                ? SymbolRegular.PanelLeftContract20
                : SymbolRegular.PanelLeftExpand20;
        }
    }

    private void SystemThemeService_OnThemeApplied(object? sender, EventArgs e) =>
        ApplyWpfUiTheme();

    private void ApplyWpfUiTheme()
    {
        if (_systemThemeService is null)
        {
            return;
        }

        var dictionary = Resources.MergedDictionaries
            .OfType<ThemesDictionary>()
            .FirstOrDefault();
        if (dictionary is null)
        {
            return;
        }

        var targetTheme = _systemThemeService.IsHighContrast
            ? ApplicationTheme.HighContrast
            : _systemThemeService.MenuUsesLightTheme
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        if (_appliedWpfUiTheme == targetTheme)
        {
            return;
        }

        var left = Left;
        var top = Top;
        var preservePosition = IsLoaded &&
            WindowState == WindowState.Normal &&
            double.IsFinite(left) &&
            double.IsFinite(top);
        dictionary.Theme = targetTheme;
        _appliedWpfUiTheme = targetTheme;
        if (preservePosition)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                Left = left;
                Top = top;
            });
        }
    }

}
