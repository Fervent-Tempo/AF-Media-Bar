using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 协调应用主题、系统主题变化和外观资源重应用。
/// Coordinates application themes, system-theme changes, and appearance-resource reapplication.
/// </summary>
public sealed class ApplicationThemeCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<AppearanceSettings, ApplicationTheme> _updateResources;
    private DispatcherTimer? _systemThemeRefreshTimer;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 创建主题协调器。
    /// Creates the application theme coordinator.
    /// </summary>
    /// <param name="dispatcher">WPF UI 调度器 / WPF UI dispatcher.</param>
    /// <param name="updateResources">资源更新回调 / Resource update callback.</param>
    public ApplicationThemeCoordinator(
        Dispatcher dispatcher,
        Action<AppearanceSettings, ApplicationTheme> updateResources)
    {
        _dispatcher = dispatcher;
        _updateResources = updateResources;
    }

    /// <summary>
    /// 订阅设置、WPF 主题和系统偏好事件。
    /// Subscribes to settings, WPF theme, and system preference events.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        SettingsManager.AppearanceSettingsChanged += OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 立即应用指定外观设置。
    /// Applies the specified appearance settings immediately.
    /// </summary>
    public void Apply(AppearanceSettings appearance)
    {
        if (_disposed)
            return;

        var theme = ResolveApplicationTheme(appearance.ApplicationThemeMode);
        if (ApplicationThemeManager.GetAppTheme() != theme)
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        }

        _updateResources(appearance, theme);
    }

    /// <summary>
    /// 将应用主题模式转换为当前有效的 WPF 主题。
    /// Resolves an application theme mode to the current effective WPF theme.
    /// </summary>
    public static ApplicationTheme ResolveApplicationTheme(ApplicationThemeMode mode)
    {
        if (SystemParameters.HighContrast)
            return ApplicationTheme.HighContrast;

        if (mode == ApplicationThemeMode.Automatic)
        {
            WindowsThemeDetector.GetWindowsTheme(out var appTheme, out _);
            return appTheme == WindowsThemeDetector.ThemeMode.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }

        return mode == ApplicationThemeMode.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }

    private void OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            Apply(e.Appearance);
            return;
        }

        _dispatcher.BeginInvoke(() => Apply(e.Appearance));
    }

    private void OnApplicationThemeChanged(ApplicationTheme theme, Color accent) =>
        _updateResources(SettingsManager.Current.Appearance, theme);

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (SettingsManager.Current.Appearance.ApplicationThemeMode == ApplicationThemeMode.Automatic)
            QueueSystemThemeRefresh();
    }

    private void QueueSystemThemeRefresh()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(QueueSystemThemeRefresh, DispatcherPriority.DataBind);
            return;
        }

        _systemThemeRefreshTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.DataBind,
            (_, _) =>
            {
                _systemThemeRefreshTimer!.Stop();
                if (SettingsManager.Current.Appearance.ApplicationThemeMode == ApplicationThemeMode.Automatic)
                    Apply(SettingsManager.Current.Appearance);
            },
            _dispatcher);

        _systemThemeRefreshTimer.Stop();
        _systemThemeRefreshTimer.Start();
    }

    /// <summary>
    /// 取消主题事件订阅并停止系统主题刷新计时器。
    /// Unsubscribes theme events and stops the system-theme refresh timer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SettingsManager.AppearanceSettingsChanged -= OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemThemeRefreshTimer?.Stop();
        _systemThemeRefreshTimer = null;
    }
}
