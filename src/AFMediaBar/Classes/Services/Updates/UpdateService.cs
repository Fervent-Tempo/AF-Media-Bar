using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 更新下载器的协调器：唯一的状态机所有者。
///
/// 它把"什么时候检查、从哪里下载、什么时候可以安装"集中在一个地方，并只对外发布不可变的
/// <see cref="UpdateState"/>：设置页与托盘菜单因此永远说同一件事，也不需要各自实现一遍重试与回退。
///
/// 三条硬边界：
/// 1) 检查、下载与安装验包在后台执行，状态事件在 Dispatcher 上发布；退出只有界等待已准备好的交接；
/// 2) 只有通过 SHA-256 校验的文件才会成为"待安装"，校验失败一律删除并且绝不安装；
/// 3) 安装由安装程序在程序退出之后完成——运行中的单文件可执行文件无法被覆盖——因此本类只负责决定
///    启动前或显式重启时的准备与交接，普通退出不会启动安装程序。
/// Coordinator of the update downloader and the single owner of its state machine.
///
/// It concentrates when to check, where to download from, and when installing is allowed, and publishes nothing
/// but an immutable <see cref="UpdateState"/>: that is why the settings page and the tray menu always agree and
/// neither has to reimplement retries or fallbacks.
///
/// Three hard boundaries:
/// 1) checking, downloading and installation verification run in the background; state events use the dispatcher,
///    and exit waits only within a bound for a prepared handoff;
/// 2) only a file that passed its SHA-256 check becomes pending, and a failed check deletes the file and never
///    installs it;
/// 3) installing is performed by the installer after the application exits — a running single-file executable
///    cannot be overwritten — so this class prepares and hands off startup or explicit restart installation.
///    A normal quit does not start the installer.
/// </summary>
public sealed partial class UpdateService : IDisposable
{
    private static readonly TimeSpan ScheduleTickInterval = TimeSpan.FromMinutes(5);

    private readonly UpdateManifestClient _manifestClient;
    private readonly UpdatePackageDownloader _downloader;
    private readonly UpdatePackageStore _store;
    private readonly InstalledApplicationProbe _probe;
    private readonly InstallCoordinatorMutex _installMutex;
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly HashSet<string> _failedSources = new(StringComparer.OrdinalIgnoreCase);

    private UpdateState _state;
    private IReadOnlyList<UpdateDownloadSource> _plan = [];
    private CancellationTokenSource? _operation;
    private DispatcherTimer? _scheduleTimer;
    private InstalledApplicationInfo _installInfo = InstalledApplicationInfo.NotInstalled;
    private bool _started;
    private volatile bool _disposed;
    private bool _installHandoffStarted;
    private bool _installOnExit;
    private int _lastReportedPercent = -1;

    /// <summary>
    /// 创建协调器并捕获 UI Dispatcher。构造函数不执行任何网络、注册表或文件访问。
    /// Creates the coordinator and captures the UI dispatcher. The constructor performs no network, registry or
    /// file access.
    /// </summary>
    /// <param name="manifestClient">清单读取器。/ Manifest reader.</param>
    /// <param name="downloader">安装包下载器。/ Installer downloader.</param>
    /// <param name="store">更新目录的所有者。/ Owner of the update directory.</param>
    /// <param name="probe">安装记录探测器。/ Installation record probe.</param>
    /// <param name="installMutex">安装协调互斥体，启动安装包前必须释放。/ Install-coordination mutex, released before starting the installer.</param>
    public UpdateService(
        UpdateManifestClient manifestClient,
        UpdatePackageDownloader downloader,
        UpdatePackageStore store,
        InstalledApplicationProbe probe,
        InstallCoordinatorMutex installMutex)
    {
        _manifestClient = manifestClient;
        _downloader = downloader;
        _store = store;
        _probe = probe;
        _installMutex = installMutex;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        var formatted = UpdateVersionPolicy.Format(assemblyVersion);
        CurrentVersion = formatted.Length > 0 ? formatted : "0.0";
        _state = UpdateState.Initial(CurrentVersion);
    }

    /// <summary>当前运行的版本，已按显示格式去掉尾部零。/ Running version, formatted without trailing zeros.</summary>
    public string CurrentVersion { get; }

    /// <summary>当前状态。/ Current state.</summary>
    public UpdateState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>状态变化（始终在 UI 线程上发布）。/ State changes, always published on the UI thread.</summary>
    public event Action<UpdateState>? UpdateStateChanged;

    /// <summary>用户要求立即重启并安装；宿主据此开始退出流程。/ The user asked to restart and install now; the host starts its shutdown sequence.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>
    /// 启动排期：清理上次运行留下的残留，并在延迟后按策略检查。必须在 UI 线程上调用。
    /// Starts scheduling: leftovers from the previous run are cleaned up and a policy-driven check is queued after
    /// the initial delay. Must be called on the UI thread.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        // 保留待安装的那一个，删掉其余安装包、未完成的 .part 与过期日志。
        // Keep the pending installer and delete the other packages, unfinished .part files and expired logs.
        var pending = _store.ReadPendingRecord();
        _store.CleanupObsolete(pending?.Path);
        RefreshInstallInfo();
        SettingsManager.SettingsChanged += OnSettingsChanged;

        if (ReadUsablePendingInstaller() is { } readyPending)
        {
            // 上次运行已经下载并校验过：直接进入"就绪"，用户不必重新下载 70 MB。清单本身会在下一次检查时补回来，
            // 因此这里用记录里的版本与哈希构造一个最小的清单，只用于驱动"就绪"状态与安装交接。
            // The previous run already downloaded and verified this file, so it starts out ready instead of making
            // the user download 70 MB again. The full manifest returns with the next check, so the pending record's
            // version and hash build the minimal manifest that drives the ready state and the install hand-off.
            PublishState(CreateReadyState(readyPending));
        }

        _scheduleTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = ScheduleTickInterval
        };
        _scheduleTimer.Tick += OnScheduleTick;
        _scheduleTimer.Start();

        // 首检延迟让媒体会话、任务栏停靠与主题先完成初始化；这段时间里的任何失败都只会写进调试输出。
        // The delay lets media sessions, taskbar docking and the theme finish initializing; any failure in the
        // meantime only reaches the debug output.
        var firstCheck = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = UpdateCheckSchedulePolicy.InitialDelay
        };
        firstCheck.Tick += (_, _) =>
        {
            firstCheck.Stop();
            _ = TryAutoCheckAsync();
        };
        firstCheck.Start();
    }

    /// <summary>
    /// 检查版本清单。<paramref name="manual"/> 为 true 时忽略"每天一次"的间隔，也忽略"已跳过"对提示的抑制。
    /// Checks the version manifest. When <paramref name="manual"/> is true the once-a-day interval is ignored, as
    /// is the way a skipped version suppresses the offer.
    /// </summary>
    /// <param name="manual">是否由用户手动触发。/ Whether the user triggered the check.</param>
    /// <returns>检查完成时结束的任务。/ A task that completes when the check finishes.</returns>
    public async Task CheckAsync(bool manual)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _state.IsBusy)
            {
                return;
            }

            // 已下载并校验完成的更新不会被后续检查覆盖：Ready 状态下再检查只会把"点击重启安装"入口换成
            // Available / UpToDate（清单源慢一拍时尤其如此，例如备用源 jsDelivr 缓存着发布前的旧清单），
            // 用户会以为更新失效。待安装安装包仍然有效时这里直接放过；安装它只需重启应用或点托盘入口。
            // An already downloaded and verified update is never overwritten by a later check: checking again while
            // Ready would replace the "click to restart and install" entry with Available / UpToDate (especially when a
            // manifest source lags, for example the fallback source jsDelivr serving a pre-release cached manifest) and
            // the user would think the update failed. With the pending installer still valid this returns; installing it
            // only needs a restart or the tray entry.
            if (UpdateReadyRetentionPolicy.ShouldRetainReadyState(_state.Phase, IsPendingInstallerUsable()))
            {
                Debug.WriteLine("[Update] Ready update retained; skipping the check while its installer is pending.");
                return;
            }

            _operation?.Dispose();
            _operation = new CancellationTokenSource();
            token = _operation.Token;
        }

        PublishState(CurrentState with
        {
            Phase = UpdatePhase.Checking,
            ProgressPercent = 0d,
            FailureReason = null
        });

        var userAgent = $"AFMediaBar/{CurrentVersion}";
        UpdateManifestFetchResult fetch;
        try
        {
            fetch = await _manifestClient.FetchAsync(userAgent, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _disposed)
        {
            return;
        }

        if (!fetch.Succeeded)
        {
            WriteUpdateSettings(current => current with
            {
                LastCheckUtc = DateTimeOffset.UtcNow,
                LastCheckSucceeded = false
            });
            PublishState(CurrentState with
            {
                Phase = UpdatePhase.Failed,
                FailureReason = fetch.FailureReason,
                ProgressPercent = 0d
            });
            return;
        }

        WriteUpdateSettings(current => current with
        {
            LastCheckUtc = DateTimeOffset.UtcNow,
            LastCheckSucceeded = true
        });
        ApplyManifest(fetch.Manifest!, manual);
    }

    /// <summary>
    /// 在 UI 线程上改写更新设置，改写函数收到的是**当前**值而不是调用方先前读到的副本。
    ///
    /// 设置写入会同步通知所有订阅者，其中包含绑定到界面的集合（`ExtraFeaturesViewModel` 的来源与快捷启动列表、
    /// `LyricsViewModel` 的来源与第二行列表）：从后台线程写会让 WPF 的 `CollectionView` 抛
    /// “该类型的 CollectionView 不支持从调度程序线程以外的线程对其 SourceCollection 进行的更改”，随后异常以
    /// `TaskScheduler.UnobservedTaskException` 的形式落到崩溃日志里。`CheckAsync` 的网络等待带 `ConfigureAwait(false)`，
    /// 它之后的代码默认跑在线程池线程上，因此回到调度器这一步 MUST 显式做在这里。
    ///
    /// 传函数而不是新值还顺带修掉一次覆盖：等待网络的那段时间里用户可能刚在设置页改过开关，
    /// 用旧副本整体写回会把那次修改悄悄抹掉。
    /// Rewrites the update settings on the UI thread, and the mutation receives the **current** value rather than the copy the
    /// caller read earlier.
    ///
    /// A settings write notifies every subscriber synchronously, including collections bound to the interface (the source and
    /// quick-launch lists of `ExtraFeaturesViewModel`, the source and second-line lists of `LyricsViewModel`): writing from a
    /// background thread makes WPF's `CollectionView` throw "this type of CollectionView does not support changes to its
    /// SourceCollection from a thread different from the Dispatcher thread", and the exception then reaches the crash log as a
    /// `TaskScheduler.UnobservedTaskException`. The network wait in `CheckAsync` carries `ConfigureAwait(false)`, so the code after
    /// it runs on a thread-pool thread by default, which is why the return to the dispatcher has to be explicit and has to happen
    /// here.
    ///
    /// Taking a mutation instead of a new value also removes an overwrite: during that network wait the user may have changed a
    /// toggle on the settings page, and writing the earlier copy back would silently discard the change.
    /// </summary>
    /// <param name="mutate">按当前值算出新值的函数。/ Function computing the new value from the current one.</param>
    private void WriteUpdateSettings(Func<UpdateSettings, UpdateSettings> mutate)
    {
        void Apply() => SettingsManager.Current.Update = mutate(SettingsManager.Current.Update);

        // 关闭中的调度器必须先挡住：此时 Invoke 会直接抛，而这次写入已经没有意义。
        // A shutting-down dispatcher is filtered first: Invoke would throw outright and the write no longer matters.
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;

        if (_dispatcher.CheckAccess())
        {
            Apply();
            return;
        }

        // 这里用同步 Invoke 而不是 BeginInvoke：调用方紧接着就要读设置（ApplyManifest、跳过版本），
        // 排队会让"刚写的值"和"马上读到的值"不一致。
        // A synchronous Invoke rather than BeginInvoke: the caller reads the settings immediately afterwards (ApplyManifest,
        // skipping a version), and queueing would make "the value just written" differ from "the value read next".
        _dispatcher.Invoke(Apply, DispatcherPriority.Normal);
    }

    /// <summary>
    /// 开始下载。已有可用副本时直接复用，只有时间戳变了才重新校验；每个来源失败后自动尝试下一个。
    /// Starts downloading. A usable local copy is reused directly and only a changed timestamp forces
    /// re-verification; a failed source moves on to the next one.
    /// </summary>
    public void StartDownload()
    {
        UpdateManifest? manifest;
        lock (_gate)
        {
            if (_disposed || _state.IsBusy || _state.Phase == UpdatePhase.Ready)
            {
                return;
            }

            manifest = _state.Manifest;
        }

        if (manifest is null || _plan.Count == 0)
        {
            return;
        }

        _ = Task.Run(() => DownloadLoopAsync(manifest));
    }

    /// <summary>取消正在进行的下载或校验；已完成的下载不受影响。/ Cancels an in-flight download or verification; a finished download is unaffected.</summary>
    public void CancelDownload()
    {
        lock (_gate)
        {
            _operation?.Cancel();
            _installPreparation?.Cancel();
        }
    }

    /// <summary>跳过清单中的当前版本；必须更新时不做任何事。/ Skips the version offered by the manifest, doing nothing for a required update.</summary>
    public void SkipCurrentVersion()
    {
        var state = CurrentState;
        if (!UpdatePresentationPolicy.CanSkipVersion(state) || state.AvailableVersion is not { } version)
        {
            return;
        }

        WriteUpdateSettings(current => current with { SkippedVersion = version });
        PublishState(state with { Phase = UpdatePhase.Skipped, IsSkipped = true });
    }

    /// <summary>
    /// 打开人工下载页；加速入口不可用时退回 GitHub 直连。
    /// Opens the manual download page, falling back to the plain GitHub page when no accelerator is available.
    /// </summary>
    /// <param name="useAccelerator">是否优先使用加速站点。/ Whether to prefer an accelerator.</param>
    /// <returns>是否成功启动了浏览器。/ Whether a browser was started.</returns>
    public bool OpenManualDownloadPage(bool useAccelerator)
    {
        var manifest = CurrentState.Manifest;
        var page = manifest?.ReleasePageUrl ?? UpdateSourcePlanPolicy.DefaultManualReleasePage;
        if (useAccelerator)
        {
            var accelerated = GitHubAcceleratorPolicy.Expand(page, manifest?.Accelerators);
            if (accelerated.Count > 0)
            {
                page = accelerated[0];
            }
        }

        return TryOpenUrl(page);
    }

    /// <summary>停止排期并取消进行中的操作。/ Stops scheduling and cancels any in-flight operation.</summary>
    public void Dispose()
    {
        PreparedInstall? prepared;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _operation?.Cancel();
            _installPreparation?.Cancel();
            _installGeneration++;
            prepared = _preparedInstall;
            _preparedInstall = null;
        }

        prepared?.Dispose();
        if (prepared is not null)
            FinishInstallPreparation(prepared.Operation);

        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
    }

    private async Task TryAutoCheckAsync()
    {
        if (!UpdateCheckSchedulePolicy.ShouldCheck(SettingsManager.Current.Update, DateTimeOffset.UtcNow))
        {
            return;
        }

        await CheckAsync(manual: false).ConfigureAwait(false);
    }

    private void OnScheduleTick(object? sender, EventArgs e) => _ = TryAutoCheckAsync();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || e.PropertyName is not (null or nameof(AppSettings.Update)))
        {
            return;
        }

        var state = CurrentState;

        // 取消"跳过此版本"后，可用版本要重新出现，否则那一页会一直显示"已跳过"。
        // Clearing the skipped version must bring the offer back, otherwise the page keeps saying "skipped" forever.
        if (state.Phase == UpdatePhase.Skipped &&
            !UpdateCheckSchedulePolicy.IsSkipped(SettingsManager.Current.Update, state.AvailableVersion))
        {
            PublishState(state with { Phase = UpdatePhase.Available, IsSkipped = false });
        }
    }

    private void ApplyManifest(UpdateManifest manifest, bool manual)
    {
        var settings = SettingsManager.Current.Update;
        if (!UpdateVersionPolicy.IsUpdateAvailable(CurrentVersion, manifest.Version))
        {
            PublishState(CurrentState with
            {
                Phase = UpdatePhase.UpToDate,
                Manifest = manifest,
                ProgressPercent = 0d,
                FailureReason = null,
                ActiveSource = null,
                IsMandatory = false,
                IsSkipped = false,
                InstallBlockedReason = null
            });
            return;
        }

        _plan = UpdateSourcePlanPolicy.BuildDownloadPlan(manifest);
        _failedSources.Clear();

        var skipped = UpdateCheckSchedulePolicy.IsSkipped(settings, manifest.Version);
        var state = CurrentState with
        {
            Manifest = manifest,
            ProgressPercent = 0d,
            FailureReason = null,
            ActiveSource = null,
            IsMandatory = UpdateVersionPolicy.IsMandatory(CurrentVersion, manifest),
            IsSkipped = skipped,
            // 安装是否被阻止必须在"发现新版本"时就确定，而不是等到下载完成：便携版用户要在按下"下载并安装"
            // 之前就知道这次更新不会被自动安装，否则他会白等一次 70 MB 下载。
            // Whether installing is blocked has to be decided as soon as a newer version is found rather than when
            // the download finishes: a portable user must learn that this update will not install itself before
            // pressing "download and install", instead of waiting through 70 MB first.
            InstallBlockedReason = ResolveInstallDecision().BlockedReason
        };

        if (!manifest.HasInstallablePackage)
        {
            // 清单有效、版本更新，只是没有带哈希的安装包直链，因此只能人工下载。这不是失败。
            // The manifest is valid and newer, it simply has no hashed installer link, so only manual download is
            // possible. That is not a failure.
            PublishState(state with { Phase = UpdatePhase.ManualOnly });
            return;
        }

        if (skipped && !manual)
        {
            PublishState(state with { Phase = UpdatePhase.Skipped });
            return;
        }

        // 发现新版本只发布状态：宿主据此弹一次系统通知，下载必须由用户在"应用与关于"页显式开始。
        // 默认自动下载会把 70 MB 流量变成用户没要求过的行为，因此这里刻意不做任何下载。
        // Discovering a newer version only publishes state: the host raises one system notification from it, and the
        // download has to be started explicitly on the "application and about" page. Downloading by default would
        // turn 70 MB of traffic into behaviour the user never asked for, so nothing is started here.
        PublishState(state with { Phase = UpdatePhase.Available });
    }

    private async Task DownloadLoopAsync(UpdateManifest manifest)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _operation?.Dispose();
            _operation = new CancellationTokenSource();
            token = _operation.Token;
        }

        // 所有 package 条目描述同一个文件，因此第一条就是这段下载的期望长度与哈希。
        // Every package entry describes the same file, so the first one carries the expected size and hash.
        var asset = manifest.Packages[0];
        var progress = new Progress<double>(ReportProgress);
        SetPhase(UpdatePhase.Downloading, 0d, null);

        string? lastFailure = null;
        UpdatePendingFileRecord? record = null;
        UpdateDownloadSource? usedSource = null;

        while (!token.IsCancellationRequested)
        {
            var source = UpdateSourcePlanPolicy.SelectNext(_plan, _failedSources);
            if (source is null)
            {
                break;
            }

            UpdateDownloadOutcome outcome;
            switch (_store.Evaluate(asset))
            {
                case UpdatePendingFileAction.Reuse:
                    record = _store.ReadPendingRecord();
                    outcome = record is null
                        ? UpdateDownloadOutcome.Failure(
                            Translations.Get("Update.Reason.PendingRecordMissing"),
                            source.HostName)
                        : UpdateDownloadOutcome.Success(record, null);
                    break;

                case UpdatePendingFileAction.VerifyAgain:
                    SetPhase(UpdatePhase.Verifying, 0d, null);
                    var existing = _store.ReadPendingRecord();
                    outcome = existing is null
                        ? UpdateDownloadOutcome.Failure(
                            Translations.Get("Update.Reason.PendingRecordMissing"),
                            source.HostName)
                        : await _downloader
                            .VerifyAsync(existing.Path, asset, manifest.Version, progress, token)
                            .ConfigureAwait(false);
                    break;

                default:
                    outcome = await _downloader
                        .DownloadAsync(source, asset, manifest.Version, progress, token)
                        .ConfigureAwait(false);
                    break;
            }

            if (outcome.Succeeded)
            {
                record = outcome.Record;
                usedSource = outcome.SourceHost is null
                    ? null
                    : new UpdateDownloadSource(source.Url, source.HostName, source.IsAccelerated);
                break;
            }

            if (outcome.Canceled)
            {
                PublishState(CurrentState with
                {
                    Phase = UpdatePhase.Available,
                    ProgressPercent = 0d,
                    ActiveSource = null,
                    FailureReason = null
                });
                return;
            }

            lastFailure = outcome.FailureReason;
            _failedSources.Add(source.Url);
        }

        if (_disposed)
        {
            return;
        }

        if (record is null)
        {
            PublishState(CurrentState with
            {
                Phase = UpdatePhase.Failed,
                ProgressPercent = 0d,
                ActiveSource = null,
                FailureReason = token.IsCancellationRequested
                    ? null
                    : lastFailure ?? Translations.Get("Update.Reason.NoUsableSource")
            });
            return;
        }

        RefreshInstallInfo();
        PublishState(CurrentState with
        {
            Phase = UpdatePhase.Ready,
            ProgressPercent = 100d,
            ActiveSource = usedSource,
            FailureReason = null,
            InstallBlockedReason = ResolveInstallDecision().BlockedReason
        });
    }

    private UpdateState CreateReadyState(UpdatePendingFileRecord record)
    {
        var manifest = new UpdateManifest(
            record.Version,
            null,
            null,
            [],
            null,
            null,
            null,
            false,
            [new UpdatePackageAsset(string.Empty, record.Size, record.Sha256)],
            null,
            null);
        return CurrentState with
        {
            Phase = UpdatePhase.Ready,
            Manifest = manifest,
            ProgressPercent = 100d,
            FailureReason = null,
            IsMandatory = UpdateVersionPolicy.IsMandatory(CurrentVersion, manifest),
            IsSkipped = false,
            InstallBlockedReason = ResolveInstallDecision().BlockedReason
        };
    }

    private void ReportProgress(double percent)
    {
        var rounded = (int)Math.Clamp(Math.Round(percent), 0d, 100d);
        if (rounded == _lastReportedPercent)
        {
            return;
        }

        _lastReportedPercent = rounded;
        var state = CurrentState;
        PublishState(state with { ProgressPercent = percent });
    }

    private void SetPhase(UpdatePhase phase, double progress, string? failureReason)
    {
        _lastReportedPercent = -1;
        PublishState(CurrentState with { Phase = phase, ProgressPercent = progress, FailureReason = failureReason });
    }

    private void RefreshInstallInfo()
    {
        _installInfo = _probe.Probe();
    }

    /// <summary>待安装安装包是否仍然可用：记录在、文件在、版本确实更新且没有被跳过。
    /// Whether the pending installer is still usable: the record exists, the file exists, the version really is newer,
    /// and the user has not skipped it.</summary>
    private bool IsPendingInstallerUsable() => ReadUsablePendingInstaller() is not null;

    /// <summary>读取仍可安装的待安装记录；不可用时返回 null。/ Reads the pending record while it is still usable; null otherwise.</summary>
    private UpdatePendingFileRecord? ReadUsablePendingInstaller()
    {
        var pending = _store.ReadPendingRecord();
        if (pending is null)
        {
            return null;
        }

        return UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: true,
            fileExists: File.Exists(pending.Path),
            versionIsNewer: UpdateVersionPolicy.IsUpdateAvailable(CurrentVersion, pending.Version),
            isSkipped: UpdateCheckSchedulePolicy.IsSkipped(SettingsManager.Current.Update, pending.Version))
            ? pending
            : null;
    }

    private (bool CanInstall, bool UseRunAs, string? BlockedReason) ResolveInstallDecision()
    {
        if (_installDecisionOverride is { } decide)
        {
            var overridden = decide();
            return (overridden.CanInstall, overridden.UseRunAs, overridden.BlockedReason);
        }
        var isInstalledCopy = InstalledApplicationProbe.MatchesRunningProcess(_installInfo, Environment.ProcessPath);
        var others = InstalledApplicationProbe.CountOtherRunningInstances(Environment.ProcessId);
        var decision = UpdateInstallPlanPolicy.Decide(isInstalledCopy, _installInfo.IsMachineWide, IsElevated(), others);
        return (decision.CanInstall, decision.UseRunAs, decision.BlockedReason);
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Update] Elevation could not be determined: {exception.Message}");
            return false;
        }
    }

    private bool TryStartInstaller(string path, string arguments, bool useRunAs)
    {
        try
        {
            if (_installerOverride is { } launch)
                return launch(path, arguments, useRunAs);
            var startInfo = new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = useRunAs,
                CreateNoWindow = !useRunAs,
                WorkingDirectory = _store.DirectoryPath
            };

            if (useRunAs)
            {
                startInfo.Verb = "runas";
            }

            using var process = Process.Start(startInfo);
            Debug.WriteLine($"[Update] Installer started ({useRunAs}): {path} {arguments}");
            return process is not null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Update] Installer could not be started: {exception.Message}");
            return false;
        }
    }

    private static bool TryOpenUrl(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Update] Could not open {url}: {exception.Message}");
            return false;
        }
    }

    private void PublishState(UpdateState state)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _state = state;
        }

        RaiseOnDispatcher(() =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_state, state))
                    return;
            }
            UpdateStateChanged?.Invoke(state);
        });
    }

    private void RaiseOnDispatcher(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed && !_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
                action();
        }), DispatcherPriority.Normal);
    }
}
