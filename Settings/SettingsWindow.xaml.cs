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

/// <summary>
/// 负责设置窗口生命周期和共享协调状态；具体页面行为由领域 partial 模块处理。
/// Owns settings-window lifecycle and shared coordination state while focused partial modules handle page behavior.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsCoordinator _coordinator;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _scaleSaveTimer;
    private readonly DispatcherTimer _fontSaveTimer;
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
        InitializeLayoutEditor();
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

}
