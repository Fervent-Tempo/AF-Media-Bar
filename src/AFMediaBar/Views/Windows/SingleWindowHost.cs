// 拥有单个窗口及排队的打开、导航请求；释放时取消请求并关闭窗口。
using System.Windows;
using System.Windows.Threading;

namespace AFMediaBar.Views.Windows;

/// <summary>合并窗口打开请求，防止窗口工厂和首次 Show 的嵌套消息循环重入。</summary>
public sealed class SingleWindowHost<TWindow>(Dispatcher dispatcher, Func<TWindow> createWindow,
    Action<Exception> reportError) : IDisposable where TWindow : Window
{
    private TWindow? _window;
    private DispatcherOperation? _openOperation;
    private DispatcherOperation? _navigationOperation;
    private Action<TWindow>? _navigation;
    private bool _opening;
    private volatile bool _disposed;
    private int _windowVersion;

    public event EventHandler? WindowClosed;

    public void RequestOpen(Action<TWindow>? navigation = null)
    {
        if (_disposed || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            var version = Volatile.Read(ref _windowVersion);
            dispatcher.BeginInvoke(() =>
            {
                if (version == _windowVersion) RequestOpen(navigation);
            });
            return;
        }
        if (navigation is not null) _navigation = navigation;
        if (_opening || _openOperation is not null) return;
        _openOperation = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(OpenWindow));
    }

    private void OpenWindow()
    {
        _openOperation = null;
        if (_disposed || _opening) return;
        _opening = true;
        try
        {
            if (_window is null)
            {
                var created = createWindow();
                if (_disposed) { created.Close(); return; }
                _window = created;
                created.Closed += OnWindowClosed;
            }
            var window = _window;
            if (!window.IsVisible) window.Show();
            if (_disposed || !ReferenceEquals(_window, window)) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            if (_navigation is not null && _navigationOperation is null)
                _navigationOperation = dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    _navigationOperation = null;
                    if (_disposed || !ReferenceEquals(_window, window)) return;
                    var navigate = _navigation;
                    _navigation = null;
                    navigate?.Invoke(window);
                }));
        }
        catch (Exception exception)
        {
            reportError(exception);
            Interlocked.Increment(ref _windowVersion);
            var failed = _window;
            _window = null;
            _navigation = null;
            _navigationOperation?.Abort();
            _navigationOperation = null;
            if (failed is not null)
            {
                failed.Closed -= OnWindowClosed;
                try { failed.Close(); }
                catch (Exception cleanupException) { reportError(cleanupException); }
            }
        }
        finally { _opening = false; }
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        if (sender is TWindow window) window.Closed -= OnWindowClosed;
        if (!ReferenceEquals(sender, _window)) return;
        Interlocked.Increment(ref _windowVersion);
        _window = null;
        _navigation = null;
        _navigationOperation?.Abort();
        _navigationOperation = null;
        _openOperation?.Abort();
        _openOperation = null;
        if (!_disposed) WindowClosed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        dispatcher.VerifyAccess();
        _disposed = true;
        Interlocked.Increment(ref _windowVersion);
        _openOperation?.Abort();
        _navigationOperation?.Abort();
        _navigation = null;
        _window?.Close();
    }
}
