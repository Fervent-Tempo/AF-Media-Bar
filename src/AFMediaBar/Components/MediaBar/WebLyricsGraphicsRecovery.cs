// 进程内图形恢复广播和会话停用状态；不持有窗口或 WebView2 资源。
using AFMediaBar.Classes.Services.Lyrics;
using System.Windows.Threading;

namespace AFMediaBar.Components;

internal static class WebLyricsGraphicsRecovery
{
    internal static WebLyricsRecoverySession Session { get; } = new();
}

/// <summary>会话状态跨任务栏宿主重建保留，资源由接受请求的控件释放。</summary>
internal sealed class WebLyricsRecoverySession
{
    public bool IsDisabled { get; private set; }
    public bool HasDegraded { get; private set; }
    public event Func<WebLyricsRecoveryAction, bool>? RecoveryRequested;
    public event Action? Degraded;

    public bool Request(WebLyricsRecoveryAction action)
    {
        if (action == WebLyricsRecoveryAction.None || RecoveryRequested is null) return false;
        var accepted = false;
        foreach (Func<WebLyricsRecoveryAction, bool> receiver in RecoveryRequested.GetInvocationList())
            accepted |= receiver(action);
        if (accepted && action == WebLyricsRecoveryAction.DisableForSession) IsDisabled = true;
        return accepted;
    }

    public void ReportDegraded()
    {
        if (HasDegraded) return;
        HasDegraded = true;
        Degraded?.Invoke();
    }
}

/// <summary>拥有一个可取消的 Dispatcher 恢复操作，合并重复请求并优先执行停用。</summary>
internal sealed class WebLyricsRecoveryQueue(Dispatcher dispatcher)
{
    private DispatcherOperation? _operation;
    private WebLyricsRecoveryAction _pending;

    public bool TryQueue(WebLyricsRecoveryAction action, Func<bool> isCurrent, Action<WebLyricsRecoveryAction> recover)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return false;
        if (action > _pending) _pending = action;
        if (_operation is not null) return true;
        DispatcherOperation? operation = null;
        operation = dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_operation, operation)) return;
            _operation = null;
            var pending = _pending;
            _pending = WebLyricsRecoveryAction.None;
            if (!dispatcher.HasShutdownStarted && isCurrent()) recover(pending);
        });
        _operation = operation;
        return true;
    }

    public void Cancel()
    {
        _operation?.Abort();
        _operation = null;
        _pending = WebLyricsRecoveryAction.None;
    }
}
