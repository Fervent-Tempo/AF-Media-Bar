using AFMediaBar.Abstractions;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace AFMediaBar.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly App _app;
    private readonly SettingsCoordinator _settingsCoordinator;
    private readonly WinUiStringLocalizer _localizer;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly WinUiDispatcher _dispatcher;
    private readonly AccessibilitySettings? _accessibilitySettings;
    private DispatcherQueueTimer? _highContrastTimer;
    private bool _highContrastEventSubscribed;
    private bool _highContrastReadFailureLogged;
    private bool _closing;
    private bool _updatingControls;
    private nint _windowHandle;

    public MainWindow(App app)
    {
        _app = app;
        _settingsCoordinator = app.SettingsCoordinator;
        _localizer = app.Localizer;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _dispatcher = new WinUiDispatcher(_dispatcherQueue);
        try
        {
            _accessibilitySettings = new AccessibilitySettings();
        }
        catch (Exception exception)
        {
            _accessibilitySettings = null;
            DiagnosticsLogService.Write("winui-high-contrast-service-unavailable", exception);
        }
        ViewModel = new ShellViewModel(ShowSettings, _app.RequestShutdown);

        InitializeComponent();
        InitializeHighContrastMonitoring();
        Activated += MainWindow_OnActivated;
        Closed += MainWindow_OnClosed;
        RefreshLocalizedText();
        ApplyTheme(_settingsCoordinator.Current.Theme);
    }

    public ShellViewModel ViewModel { get; }

    internal void ApplySettings(
        ApplicationSettings settings,
        SettingsSection sections)
    {
        if (_closing)
        {
            return;
        }

        if (sections.HasFlag(SettingsSection.Language))
        {
            RefreshLocalizedText();
        }

        if (sections.HasFlag(SettingsSection.Appearance))
        {
            ApplyTheme(settings.Theme);
        }
    }

    internal void ApplyTheme(ThemeSettings settings)
    {
        Root.RequestedTheme = settings.MenuThemeMode switch
        {
            MenuThemeMode.Light => ElementTheme.Light,
            MenuThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateHighContrastStatus();
    }

    internal void DisposeShellResources()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (_highContrastEventSubscribed && _accessibilitySettings is not null)
        {
            try
            {
                _accessibilitySettings.HighContrastChanged -= AccessibilitySettings_OnHighContrastChanged;
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("winui-high-contrast-event-unsubscribe", exception);
            }
        }

        if (_highContrastTimer is not null)
        {
            _highContrastTimer.Stop();
            _highContrastTimer.Tick -= HighContrastTimer_OnTick;
            _highContrastTimer = null;
        }

        Activated -= MainWindow_OnActivated;
        Closed -= MainWindow_OnClosed;
        _dispatcher.Shutdown();
    }

    private void MainWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowHandle != nint.Zero)
        {
            return;
        }

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureFloatingWindow();
    }

    private void ConfigureFloatingWindow()
    {
        var appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(_windowHandle));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new nint(extendedStyle));
        appWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 360));
    }

    private void ShowSettings()
    {
        if (_closing)
        {
            return;
        }

        ShellView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        RefreshLocalizedText();
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs args)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        ShellView.Visibility = Visibility.Visible;
        RefreshLocalizedText();
    }

    private void ThemeComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_updatingControls || ThemeComboBox.SelectedIndex < 0)
        {
            return;
        }

        var mode = ThemeComboBox.SelectedIndex switch
        {
            1 => MenuThemeMode.Light,
            2 => MenuThemeMode.Dark,
            _ => MenuThemeMode.Automatic
        };
        _settingsCoordinator.UpdateTheme(
            _settingsCoordinator.Current.Theme with { MenuThemeMode = mode });
    }

    private void LanguageComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_updatingControls || LanguageComboBox.SelectedIndex < 0)
        {
            return;
        }

        var language = LanguageComboBox.SelectedIndex switch
        {
            1 => AppLanguage.ZhCn,
            2 => AppLanguage.ZhTw,
            3 => AppLanguage.EnUs,
            _ => AppLanguage.FollowSystem
        };
        _settingsCoordinator.UpdateLanguage(language);
    }

    private void AccessibilitySettings_OnHighContrastChanged(
        AccessibilitySettings sender,
        object args)
    {
        if (_closing)
        {
            return;
        }

        _dispatcher.Post(UpdateHighContrastStatus, UiDispatchPriority.Input);
    }

    private void InitializeHighContrastMonitoring()
    {
        if (_accessibilitySettings is null)
        {
            return;
        }

        try
        {
            _accessibilitySettings.HighContrastChanged += AccessibilitySettings_OnHighContrastChanged;
            _highContrastEventSubscribed = true;
        }
        catch (Exception exception)
        {
            // Some Windows configurations expose the property but reject the WinRT
            // event registration with ERROR_NOT_FOUND. Polling keeps startup and
            // high-contrast state changes functional without making the event a gate.
            DiagnosticsLogService.Write("winui-high-contrast-event-unavailable", exception);
            StartHighContrastPolling();
        }
    }

    private void StartHighContrastPolling()
    {
        if (_highContrastTimer is not null || _closing)
        {
            return;
        }

        try
        {
            _highContrastTimer = _dispatcherQueue.CreateTimer();
            _highContrastTimer.Interval = TimeSpan.FromSeconds(1);
            _highContrastTimer.IsRepeating = true;
            _highContrastTimer.Tick += HighContrastTimer_OnTick;
            _highContrastTimer.Start();
        }
        catch (Exception exception)
        {
            _highContrastTimer = null;
            DiagnosticsLogService.Write("winui-high-contrast-polling-unavailable", exception);
        }
    }

    private void HighContrastTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_closing)
        {
            UpdateHighContrastStatus();
        }
    }

    private void UpdateHighContrastStatus()
    {
        HighContrastStatusText.Text = _localizer.Get("Shell.StatusHighContrast");
        HighContrastStatusText.Visibility = TryReadHighContrast(out var highContrast) && highContrast
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool TryReadHighContrast(out bool highContrast)
    {
        highContrast = false;
        if (_accessibilitySettings is null)
        {
            return false;
        }

        try
        {
            highContrast = _accessibilitySettings.HighContrast;
            return true;
        }
        catch (Exception exception)
        {
            if (!_highContrastReadFailureLogged)
            {
                _highContrastReadFailureLogged = true;
                DiagnosticsLogService.Write("winui-high-contrast-read", exception);
            }

            return false;
        }
    }

    private void RefreshLocalizedText()
    {
        if (_closing)
        {
            return;
        }

        _updatingControls = true;
        try
        {
            TitleText.Text = _localizer.Get("Shell.Title");
            TaglineText.Text = _localizer.Get("Shell.Tagline");
            SettingsButtonText.Text = _localizer.Get("Shell.OpenSettings");
            ExitButtonText.Text = _localizer.Get("Shell.Exit");
            SettingsTitleText.Text = _localizer.Get("Shell.SettingsTitle");
            SettingsDescriptionText.Text = _localizer.Get("Shell.SettingsDescription");
            ThemeLabelText.Text = _localizer.Get("Shell.Theme");
            ThemeAutomaticItem.Content = _localizer.Get("Shell.ThemeAutomatic");
            ThemeLightItem.Content = _localizer.Get("Shell.ThemeLight");
            ThemeDarkItem.Content = _localizer.Get("Shell.ThemeDark");
            LanguageLabelText.Text = _localizer.Get("Shell.Language");
            LanguageFollowSystemItem.Content = _localizer.Get("Shell.LanguageFollowSystem");
            LanguageZhCnItem.Content = _localizer.Get("Shell.LanguageZhCn");
            LanguageZhTwItem.Content = _localizer.Get("Shell.LanguageZhTw");
            LanguageEnUsItem.Content = _localizer.Get("Shell.LanguageEnUs");
            BackButtonText.Text = _localizer.Get("Shell.Back");
            CloseButtonText.Text = _localizer.Get("Shell.Close");
            ViewModel.Status = _localizer.Get("Shell.StatusReady");

            var settings = _settingsCoordinator.Current;
            ThemeComboBox.SelectedIndex = settings.Theme.MenuThemeMode switch
            {
                MenuThemeMode.Light => 1,
                MenuThemeMode.Dark => 2,
                _ => 0
            };
            LanguageComboBox.SelectedIndex = settings.Language switch
            {
                AppLanguage.ZhCn => 1,
                AppLanguage.ZhTw => 2,
                AppLanguage.EnUs => 3,
                _ => 0
            };
            UpdateHighContrastStatus();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void MainWindow_OnClosed(object sender, WindowEventArgs args)
    {
        DisposeShellResources();
    }
}
