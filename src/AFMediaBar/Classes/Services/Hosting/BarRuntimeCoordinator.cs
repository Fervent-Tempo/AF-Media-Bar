// 串行切换媒体栏宿主，成功后才发布实际模式；组合根持有，退出时在宿主线程释放。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>创建与激活失败时恢复旧宿主，并拒绝过期切换结果。</summary>
public sealed class BarRuntimeCoordinator : IDisposable
{
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly List<IBarRuntimeHost> _retiring = [];
    private readonly AppLogService? _log;
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _disposed;
    private bool _hasCommitted;

    public BarRuntimeCoordinator(AppLogService? log = null) => _log = log;

    public WindowMode RunningMode { get; private set; } = WindowMode.Taskbar;
    public IReadOnlyList<IBarRuntimeHost> Hosts { get; private set; } = [];
    public bool IsSwitching { get; private set; }
    public string? LastFailure { get; private set; }
    public event EventHandler? Changed;

    public async Task<bool> SwitchAsync(WindowMode mode,
        Func<CancellationToken, Task<IReadOnlyList<IBarRuntimeHost>>> prepare, bool force = false)
    {
        if (_disposed) return false;
        var generation = ++_generation;
        var request = new CancellationTokenSource();
        var previous = _request;
        _request = request;
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        var entered = false;
        IReadOnlyList<IBarRuntimeHost>? candidates = null;
        var old = Hosts;
        var suspended = false;
        var previousMode = RunningMode;
        try
        {
            await _switchGate.WaitAsync(request.Token);
            entered = true;
            old = Hosts;
            previousMode = RunningMode;
            if (_disposed || generation != _generation) return false;
            if (_hasCommitted && mode == RunningMode && !force) return true;
            IsSwitching = true;
            LastFailure = null;
            Publish();
            candidates = await prepare(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_disposed || generation != _generation) return false;
            suspended = true;
            foreach (var host in old) host.Suspend();
            foreach (var host in candidates)
            {
                await host.ActivateAsync(request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (_disposed || generation != _generation) throw new OperationCanceledException();
            }
            Hosts = candidates.ToArray();
            candidates = null;
            RunningMode = mode;
            _log?.Info("DisplayMode", $"媒体栏宿主切换已提交: from={previousMode} to={mode} hosts={Hosts.Count} force={force}");
            _hasCommitted = true;
            suspended = false;
            foreach (var host in old) Retire(host);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation) LastFailure = exception.Message;
            _log?.Error("DisplayMode", "媒体栏宿主切换失败，保留原模式", exception);
            return false;
        }
        finally
        {
            if (candidates is not null)
                foreach (var host in candidates) Retire(host);
            if (suspended && !_disposed)
            {
                var restored = true;
                foreach (var host in old)
                    try { host.Resume(); }
                    catch (Exception exception) { restored = false; _log?.Error("DisplayMode", "恢复原宿主失败", exception); }
                var message = $"媒体栏宿主切换回滚: requested={mode} retained={RunningMode} restored={restored} hosts={old.Count}";
                if (restored) _log?.Info("DisplayMode", message);
                else _log?.Warn("DisplayMode", message);
            }
            if (entered)
            {
                IsSwitching = false;
                Publish();
                _switchGate.Release();
            }
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    private void Retire(IBarRuntimeHost host)
    {
        try { host.Dispose(); }
        catch (Exception exception)
        {
            if (!_retiring.Contains(host)) _retiring.Add(host);
            _log?.Error("DisplayMode", "释放旧媒体栏宿主失败", exception);
        }
    }

    private void Publish()
    {
        if (_disposed) return;
        foreach (var handler in Changed?.GetInvocationList() ?? [])
            try { ((EventHandler)handler)(this, EventArgs.Empty); }
            catch (Exception exception) { _log?.Error("DisplayMode", "发布运行模式状态失败", exception); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _request?.Cancel();
        var hosts = Hosts.Concat(_retiring).Distinct().ToArray();
        Hosts = [];
        _retiring.Clear();
        foreach (var host in hosts) Retire(host);
        Changed = null;
        // 正在等待的切换会在取消后自行收尾；不提前释放其 SemaphoreSlim。
    }
}
