// 合并歌词降级提示和确认后的重启请求；拥有通知订阅和取消状态，生命周期由 App 管理。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Notifications;

/// <summary>每次运行最多提交一次歌词故障通知，发送失败仅在 Shell 恢复后补试一次。</summary>
public sealed class LyricsRecoveryNotificationCoordinator : IDisposable
{
    private readonly ISystemNotificationService _notifications;
    private readonly IRestartConfirmationService _confirmation;
    private readonly IApplicationRestartService _restart;
    private readonly AppLogService? _log;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _hasFault;
    private bool _pending;
    private bool _retrySpent;
    private bool _disposed;
    private bool _stopping;
    private int _clickBusy;

    public LyricsRecoveryNotificationCoordinator(ISystemNotificationService notifications, IRestartConfirmationService confirmation,
        IApplicationRestartService restart, AppLogService? log)
    {
        _notifications = notifications;
        _confirmation = confirmation;
        _restart = restart;
        _log = log;
        _notifications.NotificationClicked += OnNotificationClicked;
    }

    public void ReportDegraded()
    {
        if (_disposed || _stopping || _hasFault) return;
        _hasFault = true;
        _pending = !TrySendFault();
    }

    public void OnShellRestored()
    {
        if (_disposed || _stopping || !_pending || _retrySpent) return;
        _retrySpent = true;
        _pending = false;
        _ = TrySendFault();
    }

    private bool TrySendFault()
    {
        try
        {
            return _notifications.TryShowNotification(Translations.Get("Lyrics.Recovery.Notification.Title"),
                Translations.Get("Lyrics.Recovery.Notification.Body"), ShellNotificationTarget.LyricsRecovery);
        }
        catch (Exception ex)
        {
            _log?.Warn("Lyrics", "歌词故障通知发送失败", ex);
            return false;
        }
    }

    private async void OnNotificationClicked(ShellNotificationTarget target)
    {
        if (_disposed || _stopping || target != ShellNotificationTarget.LyricsRecovery || Volatile.Read(ref _clickBusy) != 0) return;
        var result = await RequestConfirmedRestartAsync();
        if (_disposed || _stopping || result is not (ApplicationRestartResult.Busy or ApplicationRestartResult.Failed)) return;
        try
        {
            _notifications.TryShowNotification(Translations.Get("Restart.Dialog.Title"),
                Translations.Get(result == ApplicationRestartResult.Busy ? "Restart.Busy" : "Restart.Failed"));
        }
        catch (Exception ex) { _log?.Warn("Restart", "重启结果提示失败", ex); }
    }

    internal async Task<ApplicationRestartResult> RequestConfirmedRestartAsync()
    {
        if (_disposed || _stopping) return ApplicationRestartResult.Canceled;
        if (Interlocked.CompareExchange(ref _clickBusy, 1, 0) != 0) return ApplicationRestartResult.Busy;
        try
        {
            var token = _lifetime.Token;
            if (!await _confirmation.ConfirmAsync("Lyrics.Recovery.RestartReason", token)) return ApplicationRestartResult.Canceled;
            token.ThrowIfCancellationRequested();
            return await _restart.RequestRestartAsync(token);
        }
        catch (OperationCanceledException) { return ApplicationRestartResult.Canceled; }
        catch (Exception ex)
        {
            _log?.Error("Restart", "歌词故障重启请求失败", ex);
            return ApplicationRestartResult.Failed;
        }
        finally { Interlocked.Exchange(ref _clickBusy, 0); }
    }

    /// <summary>宿主表达应用退出意图时立即取消请求，最终释放仍由 App 负责。</summary>
    public void CancelPendingRequests()
    {
        if (_disposed || _stopping) return;
        _stopping = true;
        _pending = false;
        _lifetime.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelPendingRequests();
        _disposed = true;
        _notifications.NotificationClicked -= OnNotificationClicked;
        _pending = false;
        _lifetime.Dispose();
    }
}
