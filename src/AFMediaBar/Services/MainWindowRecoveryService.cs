using System.Windows.Threading;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

/// <summary>
/// Owns delayed main-window recovery after Explorer/taskbar interruption.
/// A newer generation invalidates all older recovery attempts.
/// </summary>
internal sealed class MainWindowRecoveryService : IDisposable
{
    private const int RecoveryDelayMilliseconds = 300;
    private const int RetryDelayMilliseconds = 250;

    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isShutdown;
    private readonly Func<int> _getGeneration;
    private readonly Action _showMainWindow;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    internal MainWindowRecoveryService(
        Dispatcher dispatcher,
        Func<bool> isShutdown,
        Func<int> getGeneration,
        Action showMainWindow,
        CancellationToken cancellationToken)
    {
        _dispatcher = dispatcher;
        _isShutdown = isShutdown;
        _getGeneration = getGeneration;
        _showMainWindow = showMainWindow;
        _cancellationToken = cancellationToken;
    }

    internal void Request(int generation)
    {
        if (_disposed || _isShutdown() || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = RecoverAsync(generation);
    }

    private async Task RecoverAsync(int generation)
    {
        try
        {
            while (!_disposed && !_isShutdown() && generation == _getGeneration())
            {
                var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (taskbar != nint.Zero &&
                    NativeMethods.IsWindow(taskbar) &&
                    NativeMethods.GetClientRect(taskbar, out var bounds) &&
                    bounds.Width > 0 &&
                    bounds.Height > 0)
                {
                    await Task.Delay(RecoveryDelayMilliseconds, _cancellationToken);
                    if (taskbar == NativeMethods.FindWindow("Shell_TrayWnd", null) &&
                        !_disposed &&
                        !_isShutdown() &&
                        generation == _getGeneration())
                    {
                        _showMainWindow();
                        return;
                    }
                }

                await Task.Delay(RetryDelayMilliseconds, _cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown cancels pending recovery.
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("main-window-recovery", exception);
        }
    }

    public void Dispose() => _disposed = true;
}
