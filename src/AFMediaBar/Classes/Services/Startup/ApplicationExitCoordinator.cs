// 组合根拥有退出请求和独立看门狗；不依赖 UI 消息循环或线程池。
// 正常退出不撤销保护，后台线程不阻止进程结束；Dispose 供放弃协调器或测试清理。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services.Startup;

/// <summary>合并退出请求，从首次关窗前开始计时，超时才请求原生终止。</summary>
public sealed class ApplicationExitCoordinator : IApplicationExitService, IDisposable
{
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _stopped = new(false);
    private readonly Func<int, bool> _terminate;
    private readonly TimeSpan _timeout;
    private Thread? _watchdog;
    private long _started;
    private bool _requested;
    private bool _disposed;
    private int _stopDisposed;

    public event Action<int>? ExitRequested;

    public ApplicationExitCoordinator(Func<int, bool> terminate, TimeSpan? timeout = null)
    {
        _terminate = terminate;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public void RequestExit(int exitCode = 0)
    {
        ArmExitProtection();
        Action<int>? requested;
        lock (_gate)
        {
            if (_disposed || _requested) return;
            _requested = true;
            requested = ExitRequested;
        }
        requested?.Invoke(exitCode);
    }

    public void ArmExitProtection()
    {
        lock (_gate)
        {
            if (_disposed || _watchdog is not null) return;
            _started = Stopwatch.GetTimestamp();
            _watchdog = new Thread(WatchExit) { IsBackground = true, Name = "AFMediaBar.ExitWatchdog" };
            _watchdog.Start();
        }
    }

    private void WatchExit()
    {
        try
        {
            var remaining = _timeout - Stopwatch.GetElapsedTime(_started);
            if (_stopped.Wait(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)) return;
            // 不进入可能已卡住的托管退出流程；原生失败时保留后备并重试。
            while (!_stopped.IsSet)
            {
                try
                {
                    if (_terminate(1)) return;
                }
                catch (Exception exception) { Debug.WriteLine($"[App] 强制退出失败: {exception}"); }
                if (_stopped.Wait(TimeSpan.FromSeconds(1))) return;
            }
        }
        finally { if (Volatile.Read(ref _disposed)) DisposeStop(); }
    }

    private void DisposeStop()
    {
        if (Interlocked.Exchange(ref _stopDisposed, 1) == 0) _stopped.Dispose();
    }

    public void Dispose()
    {
        Thread? watchdog;
        lock (_gate)
        {
            if (_disposed) return;
            ExitRequested = null;
            _stopped.Set();
            _disposed = true;
            watchdog = _watchdog;
        }
        // 不释放仍被异常原生调用占用的等待对象；正常后台等待会立即被 Set 唤醒。
        if (watchdog is null || watchdog != Thread.CurrentThread && watchdog.Join(TimeSpan.FromSeconds(1)))
            DisposeStop();
    }
}
