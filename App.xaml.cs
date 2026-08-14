using System.Threading;
using System.Windows;
using AFMediaBar.Interop;
using AFMediaBar.Services;
using AFMediaBar.Settings;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _shutdownCancellation;
    private SystemThemeService? _systemThemeService;
    private SettingsWindow? _settingsWindow;

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
        _shutdownCancellation = new CancellationTokenSource();
        ShowMainWindow();
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
                _settingsWindow = new SettingsWindow(SettingsCoordinator);
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
        _systemThemeService?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
