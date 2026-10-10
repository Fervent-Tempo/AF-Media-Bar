// 管理安装前的后台校验与一次性交接；更新服务持有只读安装包租约，交接、取消或释放时关闭。
using System.IO;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>更新协调器的安装准备与启动、退出交接边界。</summary>
public sealed partial class UpdateService
{
    private bool _ordinaryRestartReserved;

    internal IDisposable? TryReserveOrdinaryRestart()
    {
        lock (_gate)
        {
            if (_disposed || _ordinaryRestartReserved || _installPreparation is not null || _installOnExit || _installHandoffStarted)
                return null;
            _ordinaryRestartReserved = true;
            return new OrdinaryRestartReservation(this);
        }
    }

    private sealed class OrdinaryRestartReservation(UpdateService owner) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (owner._gate) owner._ordinaryRestartReserved = false;
        }
    }
    private CancellationTokenSource? _installPreparation;
    private PreparedInstall? _preparedInstall;
    private long _installGeneration;
    private readonly Func<UpdateInstallDecision>? _installDecisionOverride;
    private readonly Func<string, string, bool, bool>? _installerOverride;

    // 测试只替换安装环境探测和进程启动，仍执行真实文件校验、租约及服务状态机。
    internal UpdateService(UpdateManifestClient client, UpdatePackageDownloader downloader, UpdatePackageStore store,
        InstalledApplicationProbe probe, InstallCoordinatorMutex mutex, Dispatcher dispatcher,
        Func<UpdateInstallDecision> decision, Func<string, string, bool, bool> launch)
        : this(client, downloader, store, probe, mutex)
    {
        _dispatcher = dispatcher;
        _installDecisionOverride = decision;
        _installerOverride = launch;
    }

    /// <summary>宿主启动前在后台校验待安装包；只有安装程序实际启动时返回 true。</summary>
    public async Task<bool> TryLaunchPendingInstallOnStartupAsync(CancellationToken cancellationToken = default)
    {
        using var prepared = await PrepareInstallAsync(startup: true, cancellationToken).ConfigureAwait(false);
        if (prepared is null)
            return false;
        try
        {
            return await Task.Run(() => TryStartPreparedInstall(prepared, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Update", "启动安装交接失败，继续正常启动", exception);
            return false;
        }
        finally
        {
            FinishInstallPreparation(prepared.Operation);
        }
    }

    /// <summary>先完成后台验包，再在 UI 线程请求退出；取消或失败时不关闭应用。</summary>
    public async Task<bool> RequestInstallAndExitAsync(CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareInstallAsync(startup: false, cancellationToken).ConfigureAwait(false);
        if (prepared is null)
            return false;
        var armed = false;
        try
        {
            lock (_gate)
            {
                if (!IsInstallPreparationCurrent(prepared))
                    return false;
                _preparedInstall = prepared;
                _installOnExit = true;
            }

            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return false;
            armed = await _dispatcher.InvokeAsync(() =>
            {
                lock (_gate)
                {
                    if (!IsInstallPreparationCurrent(prepared) || _preparedInstall != prepared)
                        return false;
                }
                var handler = RestartRequested;
                if (handler is null)
                    return false;
                handler(this, EventArgs.Empty);
                return true;
            }, DispatcherPriority.Normal, prepared.Operation.Token).Task.ConfigureAwait(false);
            return armed;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Update", "请求安装退出失败", exception);
            return false;
        }
        finally
        {
            if (!armed)
            {
                lock (_gate)
                {
                    if (_preparedInstall == prepared)
                    {
                        _preparedInstall = null;
                        _installOnExit = false;
                    }
                }
                prepared.Dispose();
                FinishInstallPreparation(prepared.Operation);
                RestoreReadyAfterCanceledInstall();
            }
        }
    }

    /// <summary>退出时只交接已验证的安装包，不再验哈希；外部检查与进程启动在后台有界等待。</summary>
    public bool TryLaunchPendingInstallOnExit()
    {
        PreparedInstall? prepared;
        lock (_gate)
        {
            if (_disposed || _installHandoffStarted || !_installOnExit)
                return false;
            prepared = _preparedInstall;
            _preparedInstall = null;
            _installOnExit = false;
        }
        if (prepared is null)
            return false;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handoffToken = timeout.Token;
        var handoff = Task.Run(() =>
        {
            using (prepared)
            {
                try { return TryStartPreparedInstall(prepared, handoffToken); }
                finally { FinishInstallPreparation(prepared.Operation); }
            }
        });
        try
        {
            // OnExit 不能依赖 Dispatcher 续体；这里仅有界等待不涉及 UI 的交接，不读取整个安装包。
            return handoff.WaitAsync(handoffToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            timeout.Cancel();
            _ = handoff.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
    }

    private async Task<PreparedInstall?> PrepareInstallAsync(bool startup, CancellationToken cancellationToken)
    {
        CancellationTokenSource operation;
        long generation;
        UpdateState previous;
        lock (_gate)
        {
            if (_disposed || _ordinaryRestartReserved || _installHandoffStarted || _installPreparation is not null || _state.IsBusy ||
                (!startup && !UpdatePresentationPolicy.CanInstallNow(_state)))
                return null;
            previous = _state;
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _installPreparation = operation;
            generation = ++_installGeneration;
        }

        if (!startup)
            SetPhase(UpdatePhase.Verifying, 0d, null);
        PreparedInstall? result = null;
        try
        {
            result = await Task.Run(() => PrepareInstallCoreAsync(startup ? null : previous.Manifest,
                generation, operation), operation.Token).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!startup)
                RestoreReadyAfterCanceledInstall();
            return null;
        }
        catch (Exception exception)
        {
            AppLogService.Current?.Warn("Update", "安装准备失败", exception);
            if (!startup)
                SetPhase(UpdatePhase.Failed, 0d, Translations.Get("Update.Reason.VerifyUnreadable"));
            return null;
        }
        finally
        {
            if (result is null)
            {
                FinishInstallPreparation(operation);
                if (!startup && CurrentState.Phase == UpdatePhase.Verifying)
                {
                    if (operation.IsCancellationRequested || CurrentState.IsInstallBlocked)
                        RestoreReadyAfterCanceledInstall();
                    else
                        SetPhase(UpdatePhase.Failed, 0d, Translations.Get("Update.Reason.VerifyUnreadable"));
                }
            }
        }
    }

    private async Task<PreparedInstall?> PrepareInstallCoreAsync(UpdateManifest? manifest, long generation,
        CancellationTokenSource operation)
    {
        var token = operation.Token;
        token.ThrowIfCancellationRequested();
        var record = _store.ReadPendingRecord();
        if (record is null)
            return null;
        if (!_store.IsTrustedInstallerPath(record.Path))
        {
            _store.ClearPendingRecord();
            return null;
        }
        if (!UpdateVersionPolicy.IsUpdateAvailable(CurrentVersion, record.Version))
        {
            _store.RemoveInstaller(record.Path);
            _store.ClearPendingRecord();
            return null;
        }
        if (manifest is not null && (manifest.Version != record.Version || manifest.Packages.Count == 0))
            return null;
        var asset = manifest?.Packages[0] ?? new UpdatePackageAsset(string.Empty, record.Size, record.Sha256);
        RefreshInstallInfo();
        var decision = ResolveInstallDecision();
        if (!decision.CanInstall)
        {
            PublishState(CurrentState with { InstallBlockedReason = decision.BlockedReason });
            return null;
        }

        FileStream? guard = null;
        var invalid = false;
        try
        {
            token.ThrowIfCancellationRequested();
            // FileShare.Read 不允许写入或替换；校验与启动之间始终保留这份文件的身份。
            guard = new FileStream(record.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var action = UpdatePendingFilePolicy.Decide(record, asset, true, guard.Length,
                new DateTimeOffset(File.GetLastWriteTimeUtc(record.Path), TimeSpan.Zero));
            if (action == UpdatePendingFileAction.Discard)
            {
                invalid = true;
                SetPhase(UpdatePhase.Failed, 0d, Translations.Get("Update.Reason.VerifyHashMismatch"));
                return null;
            }

            var progress = new InstallProgress(this, generation, token);
            var outcome = await _downloader.VerifyAsync(record.Path, asset, record.Version, progress, token,
                persistRecord: false).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!outcome.Succeeded)
            {
                invalid = true;
                SetPhase(UpdatePhase.Failed, 0d, outcome.FailureReason);
                return null;
            }
            if (_store.ReadPendingRecord() != record)
                return null;
            RefreshInstallInfo();
            decision = ResolveInstallDecision();
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed || generation != _installGeneration)
                    return null;
            }
            if (!decision.CanInstall)
            {
                PublishState(CurrentState with { InstallBlockedReason = decision.BlockedReason });
                return null;
            }
            var result = new PreparedInstall(record, guard, generation, operation);
            guard = null;
            return result;
        }
        finally
        {
            guard?.Dispose();
            if (invalid && _store.ReadPendingRecord() == record)
            {
                _store.RemoveInstaller(record.Path);
                _store.ClearPendingRecord();
            }
        }
    }

    private bool TryStartPreparedInstall(PreparedInstall prepared, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        prepared.Operation.Token.ThrowIfCancellationRequested();
        if (_store.ReadPendingRecord() != prepared.Record)
            return false;
        RefreshInstallInfo();
        var decision = ResolveInstallDecision();
        if (!decision.CanInstall)
            return false;
        lock (_gate)
        {
            if (!IsInstallPreparationCurrent(prepared) || cancellationToken.IsCancellationRequested || _installHandoffStarted)
                return false;
            _installHandoffStarted = true;
        }
        // 一次性交接已提交：清记录后不能再因取消中断，否则会丢掉尚未启动安装的待安装记录。
        _store.ClearPendingRecord();
        _installMutex.Release();
        return TryStartInstaller(prepared.Record.Path,
            UpdateInstallPlanPolicy.BuildArguments(true, _store.ResolveLogPath(prepared.Record.Version)), decision.UseRunAs);
    }

    private bool IsInstallPreparationCurrent(PreparedInstall prepared) =>
        !_disposed && prepared.Generation == _installGeneration && !prepared.Operation.IsCancellationRequested;

    private void FinishInstallPreparation(CancellationTokenSource operation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_installPreparation, operation))
                _installPreparation = null;
        }
        operation.Dispose();
    }

    // 普通退出取消正在验包的操作；显式重启已经准备好的交接由退出边界消费。
    internal void CancelInstallPreparation()
    {
        lock (_gate)
        {
            if (_preparedInstall is null)
                _installPreparation?.Cancel();
        }
    }

    private void RestoreReadyAfterCanceledInstall() =>
        PublishState(CurrentState with { Phase = UpdatePhase.Ready, ProgressPercent = 100d, FailureReason = null });

    private sealed class InstallProgress(UpdateService owner, long generation, CancellationToken token) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (owner._gate)
            {
                if (owner._disposed || token.IsCancellationRequested || generation != owner._installGeneration)
                    return;
            }
            owner.ReportProgress(value);
        }
    }

    private sealed class PreparedInstall(UpdatePendingFileRecord record, FileStream guard, long generation,
        CancellationTokenSource operation) : IDisposable
    {
        private FileStream? _guard = guard;
        public UpdatePendingFileRecord Record { get; } = record;
        public long Generation { get; } = generation;
        public CancellationTokenSource Operation { get; } = operation;
        public void Dispose() => Interlocked.Exchange(ref _guard, null)?.Dispose();
    }
}
