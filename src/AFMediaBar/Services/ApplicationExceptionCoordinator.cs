using System.Windows;
using System.Windows.Threading;

namespace AFMediaBar.Services;

/// <summary>
/// Owns process-level exception subscriptions and routes recoverable dispatcher
/// failures back to the current main-window coordinator.
/// </summary>
internal sealed class ApplicationExceptionCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isShutdown;
    private readonly Action<string> _requestEnvironmentRecovery;
    private bool _registered;

    internal ApplicationExceptionCoordinator(
        Dispatcher dispatcher,
        Func<bool> isShutdown,
        Action<string> requestEnvironmentRecovery)
    {
        _dispatcher = dispatcher;
        _isShutdown = isShutdown;
        _requestEnvironmentRecovery = requestEnvironmentRecovery;
    }

    internal void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        _dispatcher.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(
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
        if (!_isShutdown())
        {
            _requestEnvironmentRecovery("dispatcher-unhandled");
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticsLogService.Write("task-unobserved", e.Exception);
        e.SetObserved();
    }

    private static void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        DiagnosticsLogService.Write(
            "appdomain-unhandled",
            e.ExceptionObject as Exception,
            $"Terminating={e.IsTerminating}");
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        _dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
    }
}
