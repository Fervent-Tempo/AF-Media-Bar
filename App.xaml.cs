using System.Threading;
using System.Windows;
using AFMediaBar.Interop;
using AFMediaBar.Services;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _shutdownCancellation;
    private int _windowGeneration;
    private bool _shutdownRequested;

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
        try
        {
            StartupService.Migrate();
        }
        catch
        {
            // A locked Run key must not prevent the application from starting.
        }

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

    private void ShowMainWindow()
    {
        var window = new MainWindow();
        window.Closed += MainWindow_OnClosed;
        MainWindow = window;
        window.Show();
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
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
