// App 拥有入口订阅和工具窗口；延迟创建避免与隐藏主宿主形成 DI 环。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

/// <summary>复用一个工具窗口，关闭模式或退出时释放窗口会话。</summary>
internal sealed class DeveloperToolsWindowHost : IDisposable
{
    private readonly IDeveloperModeService _mode;
    private readonly Func<DeveloperToolsWindow> _factory;
    private readonly AppLogService _log;
    private DeveloperToolsWindow? _window;
    private bool _disposed;

    public DeveloperToolsWindowHost(IDeveloperModeService mode, Func<DeveloperToolsWindow> factory, AppLogService log)
    {
        _mode = mode; _factory = factory; _log = log;
        _mode.OpenRequested += OnOpenRequested;
        _mode.EnabledChanged += OnModeChanged;
    }

    private void OnOpenRequested(object? sender, EventArgs e)
    {
        if (_disposed || !_mode.IsEnabled) return;
        try
        {
            if (_window is null)
            {
                _window = _factory();
                _window.Closed += OnWindowClosed;
                _window.Show();
            }
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
        catch (Exception ex)
        {
            _window?.Close(); _window = null;
            _log.Warn("Developer", "打开开发者工具失败", ex);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is DeveloperToolsWindow window) window.Closed -= OnWindowClosed;
        if (ReferenceEquals(_window, sender)) _window = null;
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (!_mode.IsEnabled) _window?.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mode.OpenRequested -= OnOpenRequested;
        _mode.EnabledChanged -= OnModeChanged;
        _window?.Close(); _window = null;
    }
}
