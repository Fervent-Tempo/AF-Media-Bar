using System.Windows;
using System.Windows.Threading;
using AFMediaBar;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar.Services;

internal sealed class MainWindowCoordinator : IDisposable
{
    private readonly Application _application;
    private readonly ServiceProvider _serviceProvider;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isShutdown;
    private readonly Action<MainWindow> _windowCreated;
    private readonly Action<MainWindow> _windowClosed;
    private readonly MainWindowRecoveryService _recoveryService;
    private int _generation;
    private bool _recreating;
    private bool _disposed;

    internal MainWindowCoordinator(Application application, ServiceProvider serviceProvider, Dispatcher dispatcher, Func<bool> isShutdown, CancellationToken cancellationToken, Action<MainWindow>? windowCreated = null, Action<MainWindow>? windowClosed = null)
    {
        _application = application;
        _serviceProvider = serviceProvider;
        _dispatcher = dispatcher;
        _isShutdown = isShutdown;
        _windowCreated = windowCreated ?? (_ => { });
        _windowClosed = windowClosed ?? (_ => { });
        _recoveryService = new MainWindowRecoveryService(dispatcher, isShutdown, () => _generation, Show, cancellationToken);
    }

    internal void Show()
    {
        if (_disposed || _isShutdown() || _dispatcher.HasShutdownStarted) return;
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Closed += Window_OnClosed;
        _application.MainWindow = window;
        _windowCreated(window);
        window.Show();
    }

    internal void Recreate()
    {
        if (_disposed || _isShutdown() || _recreating) return;
        _recreating = true;
        _generation++;
        _ = _dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_disposed || _isShutdown() || _dispatcher.HasShutdownStarted) return;
                _application.MainWindow?.Close();
                Show();
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("main-window-recreation", exception);
            }
            finally { _recreating = false; }
        });
    }

    internal void InvalidateRecovery() => _generation++;

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window) return;
        window.Closed -= Window_OnClosed;
        _windowClosed(window);
        if (_isShutdown() || _dispatcher.HasShutdownStarted) return;
        if (_recreating)
        {
            _application.MainWindow = null;
            return;
        }
        _application.MainWindow = null;
        _recoveryService.Request(++_generation);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recoveryService.Dispose();
        if (_application.MainWindow is MainWindow window) window.Closed -= Window_OnClosed;
    }
}
