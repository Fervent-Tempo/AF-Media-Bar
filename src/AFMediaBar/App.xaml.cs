using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Abstractions;
using AFMediaBar.Composition;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.Services.Lyrics;
using AFMediaBar.Services.Players;
using AFMediaBar.Services.Win32Api;
using AFMediaBar.Settings;
// System.Windows.Localization（枚举）与本地化帮助类同名，用别名消歧。
using Loc = AFMediaBar.Services.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _shutdownCancellation;
    private SystemThemeService? _systemThemeService;
    private UpdateService? _updateService;
    private ServiceProvider? _services;
    private SettingsWindow? _settingsWindow;
    private Version? _notifiedUpdateVersion;
#if DEBUG
    // 实时歌词调试状态：仅在歌词行变化时输出，避免 233ms 轮询刷屏。
    // Debug lyric state: prints only when the active line changes, to avoid spam from the 233ms poll.
    private string? _debugLyricsLrc;
    private IReadOnlyList<LrcLine> _debugLyricsLines = [];
    private int _debugLyricsLastIndex = -1;
#endif

    internal SystemThemeService? ThemeService => _systemThemeService;
    internal SettingsCoordinator SettingsCoordinator { get; private set; } = null!;
    private int _windowGeneration;
    private bool _shutdownRequested;
    private bool _recreatingMainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterExceptionHandlers();
        _singleInstanceMutex = new Mutex(true, "AFMediaBar.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _shutdownRequested = true;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _systemThemeService = new SystemThemeService(this);
        try
        {
            StartupService.Migrate();
        }
        catch (Exception exception)
        {
            // A locked Run key must not prevent the application from starting.
            DiagnosticsLogService.Write("startup-registration-migration", exception);
        }

        SettingsCoordinator = new SettingsCoordinator();
        SettingsCoordinator.Changed += SettingsCoordinator_OnChanged;
        ApplyLanguageSettings();
        ApplyFontSettings();
        _updateService = new UpdateService(WpfStringLocalizer.Instance);
        _services = ServiceRegistration.Build(SettingsCoordinator, _updateService);
        _shutdownCancellation = new CancellationTokenSource();
        ShowMainWindow();
        _ = CheckForUpdatesAfterStartupAsync();
    }

    internal void RequestShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _windowGeneration++;
        _shutdownCancellation?.Cancel();
        Shutdown();
    }

    internal void RecreateMainWindow()
    {
        if (_shutdownRequested || _recreatingMainWindow)
        {
            return;
        }

        _recreatingMainWindow = true;
        _windowGeneration++;
        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_shutdownRequested || Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                MainWindow?.Close();
                ShowMainWindow();
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("main-window-recreation", exception);
            }
            finally
            {
                _recreatingMainWindow = false;
            }
        });
    }

    internal void ShowSettingsWindow()
    {
        if (_shutdownRequested || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(
                    SettingsCoordinator,
                    _updateService ?? throw new InvalidOperationException(Loc.Get("Msg.UpdateNotInitialized")),
                    _services?.GetRequiredService<SettingsWindowViewModel>()
                        ?? throw new InvalidOperationException("Settings services are not initialized."));
                _settingsWindow.Closed += SettingsWindow_OnClosed;
                _settingsWindow.Show();
            }
            else
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-settings-window", exception);
            if (_settingsWindow is not null)
            {
                _settingsWindow.Closed -= SettingsWindow_OnClosed;
            }

            _settingsWindow = null;
            MessageBox.Show(
                Loc.Get("Msg.OpenSettingsFailedBody", exception.Message),
                Loc.Get("Msg.OpenSettingsFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal void RequestMediaReconnect()
    {
        if (MainWindow is MainWindow window)
        {
            window.RequestMediaReconnect();
        }
    }

    /// <summary>
    /// 延迟执行静默自动检查，避免更新网络请求阻塞播放器启动。
    /// Runs a delayed silent check so update network requests never block player startup.
    /// </summary>
    private async Task CheckForUpdatesAfterStartupAsync()
    {
        var cancellationToken = _shutdownCancellation?.Token ?? CancellationToken.None;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            if (_shutdownRequested || _updateService is null)
            {
                return;
            }

            var result = await _updateService.CheckForUpdatesAsync(
                force: false,
                cancellationToken);
            if (result is not { Status: UpdateCheckStatus.UpdateAvailable, Update: { } update } ||
                (!update.Mandatory && _updateService.IsVersionSkipped(update.Version)) ||
                _notifiedUpdateVersion == update.Version)
            {
                return;
            }

            _notifiedUpdateVersion = update.Version;
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            ShowUpdateNotification(update);
        }
        catch (OperationCanceledException)
        {
            // 应用退出时取消延迟检查。 / Application shutdown cancels a delayed update check.
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("automatic-update-check", exception);
            // 自动检查异常必须保持静默，不能影响启动或退出。 / Automatic-check failures must never affect application startup or shutdown.
        }
    }

    /// <summary>
    /// 提示用户有新版本，并仅打开下载页面，不直接修改本地程序文件。
    /// Notifies the user and only opens a download page without modifying local program files.
    /// </summary>
    private static void ShowUpdateNotification(UpdateInfo update)
    {
        var changelog = update.Changelog.Count == 0
            ? Loc.Get("Msg.UpdateOpenPageHint")
            : string.Join(
                Environment.NewLine,
                update.Changelog.Take(5).Select(item => $"• {item}"));
        var result = MessageBox.Show(
            Loc.Get("Msg.UpdateFoundBody", update.VersionText, changelog),
            update.Mandatory ? Loc.Get("Msg.UpdateMajor") : Loc.Get("Msg.UpdateNew"),
            MessageBoxButton.YesNo,
            update.Mandatory ? MessageBoxImage.Warning : MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var uri = UpdateService.GetPreferredDownloadUri(update);
        if (uri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-update-download", exception, uri.AbsoluteUri);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenDownloadFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowMainWindow()
    {
        var window = new MainWindow();
        window.Closed += MainWindow_OnClosed;
#if DEBUG
        window.MediaSessionService.SnapshotChanged += DebugOutputLyrics;
#endif
        MainWindow = window;
        window.Show();
    }

#if DEBUG
    // 实时歌词调试：根据快照位置解析 LRC 并输出当前行（仅行变化时打印）。
    // Debug handler: resolves the active LRC line from the snapshot position.
    private void DebugOutputLyrics(object? sender, MediaSnapshot snapshot)
    {
        if (snapshot.Lyrics is not { } lyrics || string.IsNullOrWhiteSpace(lyrics.Lrc))
        {
            return;
        }

        if (!string.Equals(_debugLyricsLrc, lyrics.Lrc, StringComparison.Ordinal))
        {
            _debugLyricsLrc = lyrics.Lrc;
            _debugLyricsLines = LrcParser.Parse(lyrics.Lrc);
            _debugLyricsLastIndex = -1;
        }

        var index = LrcParser.FindIndex(
            _debugLyricsLines,
            TimeSpan.FromSeconds(snapshot.Position));
        if (index < 0 || index == _debugLyricsLastIndex)
        {
            return;
        }

        _debugLyricsLastIndex = index;
        Debug.WriteLine(
            $"[Lyrics][{lyrics.Source}] {_debugLyricsLines[index].Time:mm\\:ss} {_debugLyricsLines[index].Text}");
    }
#endif

    /// <summary>
    /// 将已持久化的字体预设写入应用级资源，替换 XAML 中的默认字体。
    /// 各控件通过 DynamicResource 引用 AppTextFontFamily / AppDisplayFontFamily，
    /// 资源替换后立即热更新，无需重启。图标字体 AppIconFontFamily 保持不变。
    /// </summary>
    private void ApplyFontSettings()
    {
        var font = SettingsCoordinator.Current.Font;
        var textFamily = new FontFamily(FontSettings.ResolveText(font.Latin, font.Cjk));
        Resources["AppTextFontFamily"] = textFamily;
        Resources["AppDisplayFontFamily"] = textFamily;
        Resources["PlayerTitleFontWeight"] = WpfFontSettingsAdapter.ResolveTitleWeight(font.Weight);
        Resources["PlayerTextFontWeight"] = WpfFontSettingsAdapter.ResolveBodyWeight(font.Weight);
    }

    private void SettingsCoordinator_OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (e.Sections.HasFlag(SettingsSection.Font))
        {
            ApplyFontSettings();
        }

        if (e.Sections.HasFlag(SettingsSection.Appearance))
        {
            _systemThemeService?.Refresh(e.Settings.Theme);
        }

        if (e.Sections.HasFlag(SettingsSection.Language))
        {
            ApplyLanguageSettings();
            if (MainWindow is MainWindow window)
            {
                window.RefreshLocalizedText();
            }
        }
    }

    /// <summary>
    /// 将当前语言词典载入应用资源（约定为 MergedDictionaries 首个字典）。
    /// 替换字典后所有 DynamicResource 文本引用即时刷新；动态文本由各窗口监听
    /// SettingsSection.Language 自行刷新。
    /// </summary>
    private void ApplyLanguageSettings()
    {
        var dictionaryName = LanguageSettingsService.ResolveDictionaryName(
            SettingsCoordinator.Current.Language);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Resources/Languages/{dictionaryName}.xaml", UriKind.Relative)
        };
        if (Resources.MergedDictionaries.Count > 0)
        {
            Resources.MergedDictionaries[0] = dictionary;
        }
        else
        {
            Resources.MergedDictionaries.Add(dictionary);
        }
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window)
        {
            window.Closed -= SettingsWindow_OnClosed;
        }
        _settingsWindow = null;
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= MainWindow_OnClosed;
#if DEBUG
            if (window is MainWindow mainWindow)
            {
                mainWindow.MediaSessionService.SnapshotChanged -= DebugOutputLyrics;
            }
#endif
        }

        if (_shutdownRequested || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (_recreatingMainWindow)
        {
            MainWindow = null;
            return;
        }

        MainWindow = null;
        var generation = ++_windowGeneration;
        _ = RecoverMainWindowAsync(generation);
    }

    private async Task RecoverMainWindowAsync(int generation)
    {
        var cancellationToken = _shutdownCancellation?.Token ?? CancellationToken.None;
        try
        {
            while (!_shutdownRequested && generation == _windowGeneration)
            {
                var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (taskbar != nint.Zero &&
                    NativeMethods.IsWindow(taskbar) &&
                    NativeMethods.GetClientRect(taskbar, out var bounds) &&
                    bounds.Width > 0 &&
                    bounds.Height > 0)
                {
                    await Task.Delay(300, cancellationToken);
                    if (taskbar == NativeMethods.FindWindow("Shell_TrayWnd", null) &&
                        !_shutdownRequested &&
                        generation == _windowGeneration)
                    {
                        ShowMainWindow();
                        return;
                    }
                }

                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown cancels a pending Explorer recovery.
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("main-window-recovery", exception);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += App_OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_OnUnhandledException;
    }

    private void UnregisterExceptionHandlers()
    {
        DispatcherUnhandledException -= App_OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= App_OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= AppDomain_OnUnhandledException;
    }

    private void App_OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticsLogService.Write("dispatcher-unhandled", e.Exception);
        if (e.Exception is OutOfMemoryException or
            StackOverflowException or
            AccessViolationException)
        {
            return;
        }

        e.Handled = true;
        if (!_shutdownRequested && MainWindow is MainWindow window)
        {
            window.RequestEnvironmentRecovery("dispatcher-unhandled");
        }
    }

    private static void App_OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticsLogService.Write("task-unobserved", e.Exception);
        e.SetObserved();
    }

    private static void AppDomain_OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        DiagnosticsLogService.Write(
            "appdomain-unhandled",
            e.ExceptionObject as Exception,
            $"Terminating={e.IsTerminating}");
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _shutdownRequested = true;
        _windowGeneration++;
        _shutdownCancellation?.Cancel();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdownRequested = true;
        _shutdownCancellation?.Cancel();
        _shutdownCancellation?.Dispose();
        _updateService?.Dispose();
        _services?.Dispose();
        _systemThemeService?.Dispose();
        _singleInstanceMutex?.Dispose();
        UnregisterExceptionHandlers();
        base.OnExit(e);
    }
}
