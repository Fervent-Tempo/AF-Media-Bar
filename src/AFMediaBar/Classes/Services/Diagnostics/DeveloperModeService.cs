// 同步开发者设置并拥有取消源；App 负责最终释放，不清除生产故障预算。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>开发者模式的设置入口和进程内请求代际。</summary>
public sealed class DeveloperModeService : IDeveloperModeService, IDisposable
{
    private CancellationTokenSource _session = new();
    private bool _enabled;
    private bool _disposed;

    public DeveloperModeService()
    {
        _enabled = SettingsManager.Current.DeveloperModeEnabled;
        if (!_enabled) _session.Cancel();
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public bool IsEnabled => !_disposed && _enabled;
    public int Generation { get; private set; }
    public CancellationToken SessionToken => _disposed ? new CancellationToken(true) : _session.Token;
    public event EventHandler? EnabledChanged;
    public event EventHandler? OpenRequested;

    public void SetEnabled(bool enabled)
    {
        if (!_disposed) SettingsManager.Current.DeveloperModeEnabled = enabled;
    }

    public void OpenTools()
    {
        if (IsEnabled) OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || _enabled == SettingsManager.Current.DeveloperModeEnabled) return;
        _enabled = SettingsManager.Current.DeveloperModeEnabled;
        Generation++;
        _session.Cancel();
        _session.Dispose();
        _session = new();
        if (!_enabled) _session.Cancel();
        EnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Generation++;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _session.Cancel();
        EnabledChanged?.Invoke(this, EventArgs.Empty);
        _session.Dispose();
    }
}
