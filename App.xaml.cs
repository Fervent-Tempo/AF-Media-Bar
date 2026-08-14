using System.Diagnostics;
using System.Threading;
using System.Windows;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.Settings;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _shutdownCancellation;
    private SystemThemeService? _systemThemeService;
    private UpdateService? _updateService;
    private SettingsWindow? _settingsWindow;
    private Version? _notifiedUpdateVersion;

    internal SystemThemeService? ThemeService => _systemThemeService;
    internal SettingsCoordinator SettingsCoordinator { get; private set; } = null!;
    private int _windowGeneration;
    private bool _shutdownRequested;
    private bool _recreatingMainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        catch
        {
            // A locked Run key must not prevent the application from starting.
        }

        SettingsCoordinator = new SettingsCoordinator();
        _updateService = new UpdateService();
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
                    _updateService ?? throw new InvalidOperationException("更新服务尚未初始化。"));
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
            if (_settingsWindow is not null)
            {
                _settingsWindow.Closed -= SettingsWindow_OnClosed;
            }

            _settingsWindow = null;
            MessageBox.Show(
                $"设置窗口初始化失败。\n\n{exception.Message}",
                "无法打开设置",
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
        catch
        {
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
            ? "请打开发布页面查看更新内容。"
            : string.Join(
                Environment.NewLine,
                update.Changelog.Take(5).Select(item => $"• {item}"));
        var result = MessageBox.Show(
            $"发现 AF Media Bar {update.VersionText}。\n\n{changelog}\n\n是否打开下载页面？",
            update.Mandatory ? "发现重要更新" : "发现新版本",
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
            MessageBox.Show(
                exception.Message,
                "无法打开下载页面",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowMainWindow()
    {
        var window = new MainWindow();
        window.Closed += MainWindow_OnClosed;
        MainWindow = window;
        window.Show();
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
        _systemThemeService?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
