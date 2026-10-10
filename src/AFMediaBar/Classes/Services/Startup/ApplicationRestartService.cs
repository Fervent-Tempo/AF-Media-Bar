// 拥有一次重启预约和取消生命周期；进程交接由组合根注入，退出由 App 接收事件执行。
using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services.Startup;

/// <summary>合并重启请求，与安装互斥，只有交接准备成功才请求退出。</summary>
internal sealed class ApplicationRestartService(
    Func<IDisposable?> reserve,
    Func<CancellationToken, Task<RestartPreparation?>> prepare,
    AppLogService? log) : IApplicationRestartService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _reservation;
    private int _busy;
    private bool _requested;
    private bool _disposed;
    public event EventHandler? RestartRequested;

    public async Task<ApplicationRestartResult> RequestRestartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested) return ApplicationRestartResult.Canceled;
        if (_requested || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return ApplicationRestartResult.Busy;
        IDisposable? reservation = null;
        RestartPreparation? preparation = null;
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (RestartRequested is null) return ApplicationRestartResult.Failed;
            reservation = reserve();
            if (reservation is null) return ApplicationRestartResult.Busy;
            preparation = await prepare(request.Token);
            if (preparation is null) return ApplicationRestartResult.Failed;
            request.Token.ThrowIfCancellationRequested();
            _requested = true;
            log?.Info("Restart", "重启交接已确认，请求正常退出");
            RestartRequested.Invoke(this, EventArgs.Empty);
            preparation.Commit();
            _reservation = reservation;
            reservation = null;
            return ApplicationRestartResult.Requested;
        }
        catch (OperationCanceledException) { return ApplicationRestartResult.Canceled; }
        catch (Exception ex)
        {
            log?.Error("Restart", "普通重启交接失败，保留当前实例", ex);
            return ApplicationRestartResult.Failed;
        }
        finally
        {
            if (preparation is not null) await preparation.DisposeAsync();
            if (reservation is not null) _requested = false;
            reservation?.Dispose();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _reservation?.Dispose();
        _reservation = null;
        RestartRequested = null;
    }
}
