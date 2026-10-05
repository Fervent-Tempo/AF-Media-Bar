using System.Collections.ObjectModel;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 应用页的视图模型：应用自身的设置——版本与更新、开机自启、界面语言、设置文件与诊断日志。
    ///
    /// 它是原「应用与关于」页视图模型的前半部分：拆分之后"关于"只承载人（开发人员、赞助者）、赞助入口与开源许可，
    /// 而凡是"改了会影响程序怎么跑"的设置都留在这一页。
    /// View model for the application page: the application's own settings — version and updates, run-at-startup, interface
    /// language, the settings file, and diagnostics.
    ///
    /// It is the first half of the former "application and about" view model: after the split, "about" only carries people
    /// (developers, sponsors), the support entry, and open-source licenses, while everything that changes how the program
    /// behaves stays here.
    /// </summary>
    public partial class ApplicationViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;
        private readonly SettingsPersistenceService _settingsPersistence;
        private readonly StartupRegistrationService _startupRegistration;
        private readonly LocalizationService _localization;

        /// <summary>设置启动自动启动之后的状态说明；失败原因必须可见，而不是让开关静默弹回。/ Status text after applying run-at-startup; a failure reason has to be visible instead of the switch silently bouncing back.</summary>
        [ObservableProperty]
        private string _startupStatusText = string.Empty;

        [ObservableProperty]
        private string _currentVersion = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _channelText = string.Empty;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private bool _isProgressVisible;

        [ObservableProperty]
        private bool _hasUpdate;

        [ObservableProperty]
        private bool _hasHighlights;

        /// <summary>
        /// 是否在更新区之外单独显示一条状态说明（检查本身失败、没有清单可展示时）。
        /// Whether a standalone status callout is shown outside the update block, which happens when the check itself
        /// failed and there is no manifest to display.
        /// </summary>
        [ObservableProperty]
        private bool _showStatusNotice;

        [ObservableProperty]
        private string _highlightTitle = string.Empty;

        [ObservableProperty]
        private string _highlightDate = string.Empty;

        [ObservableProperty]
        private bool _canCheck = true;

        [ObservableProperty]
        private bool _canDownload;

        [ObservableProperty]
        private bool _canCancel;

        [ObservableProperty]
        private bool _canInstallNow;

        [ObservableProperty]
        private bool _canSkip;

        [ObservableProperty]
        private bool _canOpenDownloadPage;

        [ObservableProperty]
        private bool _isPortable;

        /// <summary>当前是否确实记录着"已跳过某个版本"，用于决定"取消跳过"是否可点击。/ Whether a skipped version is actually recorded, which decides if "clear skip" is clickable.</summary>
        [ObservableProperty]
        private bool _hasSkippedVersion;

        /// <summary>
        /// 更新亮点条目（清单的 <c>changelog</c>）。
        /// Update highlight entries, taken from the manifest's <c>changelog</c>.
        /// </summary>
        public ObservableCollection<string> HighlightEntries { get; } = [];

        /// <summary>
        /// 创建应用页视图模型并订阅更新状态。两者都是单例，因此订阅与进程同寿命。
        /// Creates the application view model and subscribes to update state. Both are singletons, so the subscription
        /// lives as long as the process.
        /// </summary>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        /// <param name="settingsPersistence">设置文件与「我的默认设置」快照的所有者。/ Owner of the settings file and the user-defaults snapshot.</param>
        /// <param name="startupRegistration">开机自动启动的注册表登记。/ Registry registration for run-at-startup.</param>
        /// <param name="localization">界面语言：本页既修改它，也要在它变化后刷新自己产出的文案。/ Interface language: this page changes it and refreshes its own text when it changes.</param>
        public ApplicationViewModel(
            UpdateService updateService,
            SettingsPersistenceService settingsPersistence,
            StartupRegistrationService startupRegistration,
            LocalizationService localization)
        {
            _updateService = updateService;
            _settingsPersistence = settingsPersistence;
            _startupRegistration = startupRegistration;
            _startupRegistration.StateChanged += OnStartupStateChanged;
            OnStartupStateChanged(null, EventArgs.Empty);
            _localization = localization;
            CurrentVersion = updateService.CurrentVersion;
            _updateService.UpdateStateChanged += ApplyState;

            // 本页有三处文案是代码拼出来的（启动项失败原因、默认设置状态、更新状态行），它们的取值在语言变化时
            // 必须重取；空的属性名让 WPF 重新读取全部绑定，而不是逐个列出属性名——漏掉一个就会留下半页旧语言。
            // Three strings on this page are composed in code (the startup failure reason, the defaults status, and the
            // update status line) and have to be re-read when the language changes; the empty property name makes WPF
            // re-read every binding instead of listing properties one by one, where missing one leaves half a page in the
            // old language.
            _localization.LanguageChanged += OnLanguageChanged;

            ApplyState(_updateService.CurrentState);
        }

        /// <summary>
        /// 语言变化后按当前状态重算本页文案。
        ///
        /// 只发通知是不够的：更新状态行、亮点标题与渠道说明是**上一次状态发布时**按当时的语言拼好并存在属性里的，
        /// 重新读取绑定拿到的仍是那句旧语言。因此先按当前状态重算，再让 WPF 重读全部绑定。
        /// Recomputes this page's text from the current state after a language change.
        ///
        /// Raising notifications alone is not enough: the update status line, the highlight title, and the channel description were
        /// composed in the language of the *last state publication* and stored in properties, so re-reading the bindings would
        /// still yield that old sentence. The state is therefore applied again first, and WPF then re-reads every binding.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            ApplyState(_updateService.CurrentState);
            if (_startupRegistration.RegistrationStateUnknown && _startupRegistration.LastFailure is null)
                StartupStatusText = Translations.Get("About.Status.StartupUnknown");
            OnPropertyChanged(string.Empty);
        }

        /// <summary>自动检查更新开关；与设置文件双向同步。/ Automatic update check toggle, synchronized with the settings file.</summary>
        public bool AutoCheckEnabled
        {
            get => SettingsManager.Current.Update.AutoCheckEnabled;
            set
            {
                if (SettingsManager.Current.Update.AutoCheckEnabled == value)
                {
                    return;
                }

                SettingsManager.Current.Update = SettingsManager.Current.Update with { AutoCheckEnabled = value };
                OnPropertyChanged();
            }
        }

        /// <summary>立即检查更新（忽略"每天一次"的间隔）。/ Checks for updates immediately, ignoring the once-a-day interval.</summary>
        [RelayCommand]
        private void CheckForUpdates() => _ = _updateService.CheckAsync(manual: true);

        /// <summary>开始下载已发现的更新。/ Starts downloading the discovered update.</summary>
        [RelayCommand]
        private void DownloadUpdate() => _updateService.StartDownload();

        /// <summary>取消正在进行的下载或校验。/ Cancels the in-flight download or verification.</summary>
        [RelayCommand]
        private void CancelDownload() => _updateService.CancelDownload();

        /// <summary>立即重启并安装：退出后由安装程序静默安装并自动启动新版本。/ Restarts and installs now: the installer runs silently after exit and starts the new version.</summary>
        [RelayCommand]
        private async Task InstallAndRestart(CancellationToken cancellationToken) =>
            await _updateService.RequestInstallAndExitAsync(cancellationToken);

        /// <summary>跳过清单中的当前版本。/ Skips the version offered by the manifest.</summary>
        [RelayCommand]
        private void SkipVersion() => _updateService.SkipCurrentVersion();

        /// <summary>打开 GitHub 的下载页。/ Opens the GitHub download page.</summary>
        [RelayCommand]
        private void OpenDownloadPage() => _updateService.OpenManualDownloadPage(useAccelerator: false);

        /// <summary>通过加速站点打开下载页。/ Opens the download page through an accelerator.</summary>
        [RelayCommand]
        private void OpenAcceleratedDownloadPage() => _updateService.OpenManualDownloadPage(useAccelerator: true);

        /// <summary>清除"已跳过"的版本，使可用版本重新出现。/ Clears the skipped version so the offer comes back.</summary>
        [RelayCommand]
        private void ClearSkippedVersion() =>
            SettingsManager.Current.Update = SettingsManager.Current.Update with { SkippedVersion = null };

        /// <summary>
        /// 随 Windows 登录自动启动。启动时服务先将实际 Run 登记同步到设置；后续修改写入注册表，失败时回退并通知本页。
        /// </summary>
        public bool LaunchAtStartup
        {
            get => SettingsManager.Current.LaunchAtStartup;
            set
            {
                if (SettingsManager.Current.LaunchAtStartup == value)
                    return;

                _startupRegistration.SetLaunchAtStartup(value);
                OnPropertyChanged();
            }
        }

        private void OnStartupStateChanged(object? sender, EventArgs e)
        {
            StartupStatusText = _startupRegistration.LastFailure is { } failure
                ? Translations.Format("About.Status.StartupFailed", failure)
                : _startupRegistration.RegistrationStateUnknown
                    ? Translations.Get("About.Status.StartupUnknown")
                    : string.Empty;
            OnPropertyChanged(nameof(LaunchAtStartup));
        }

        /// <summary>当前是否已保存「我的默认设置」快照。/ Whether a user-defaults snapshot is currently saved.</summary>
        public bool HasUserDefaults => SettingsManager.UserDefaults is not null;

        /// <summary>「我的默认设置」的状态说明。/ Status text for the user-defaults snapshot.</summary>
        public string UserDefaultsStatusText => HasUserDefaults
            ? Translations.Get("About.Row.SaveDefaults.Saved")
            : Translations.Get("About.Row.SaveDefaults.NotSaved");

        /// <summary>
        /// 界面语言选项。写入设置后由 <see cref="LocalizationService"/> 解析并生效：本页只负责保存选项，
        /// 不直接修改任何界面文案，因此"选择语言"和"界面换语言"永远只有一条路径。
        /// The interface-language option. Writing the setting is all this page does; <see cref="LocalizationService"/>
        /// resolves and applies it, so "choosing a language" and "the interface changing language" always follow one path.
        /// </summary>
        public InterfaceLanguage InterfaceLanguage
        {
            get => SettingsManager.Current.InterfaceLanguage;
            set
            {
                if (SettingsManager.Current.InterfaceLanguage == value)
                {
                    return;
                }

                SettingsManager.Current.InterfaceLanguage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>把当前设置保存为「我的默认设置」，此后所有重置入口都以它为准。/ Saves the current settings as the user's defaults; every reset entry then uses them.</summary>
        [RelayCommand]
        private void SaveUserDefaults()
        {
            var failure = _settingsPersistence.SaveCurrentAsUserDefaults();
            StartupStatusText = failure is null ? string.Empty : Translations.Format("About.Status.SaveDefaultsFailed", failure);
            OnPropertyChanged(nameof(HasUserDefaults));
            OnPropertyChanged(nameof(UserDefaultsStatusText));
        }

        /// <summary>删除「我的默认设置」快照，让重置入口回到程序内置默认值。/ Deletes the snapshot so resets fall back to the built-in defaults.</summary>
        [RelayCommand]
        private void ClearUserDefaults()
        {
            var failure = _settingsPersistence.ClearUserDefaults();
            StartupStatusText = failure is null ? string.Empty : Translations.Format("About.Status.ClearDefaultsFailed", failure);
            OnPropertyChanged(nameof(HasUserDefaults));
            OnPropertyChanged(nameof(UserDefaultsStatusText));
        }

        /// <summary>
        /// 恢复全部默认值，并刷新本页显示的两个更新开关。
        /// Restores every default and refreshes the two update toggles shown on this page.
        /// </summary>
        public void ResetAll()
        {
            SettingsManager.ResetAll();
            RefreshSettings();
        }

        /// <summary>设置被重置后刷新本页开关的显示值。/ Refreshes the toggle shown on this page after the settings were reset.</summary>
        public void RefreshSettings()
        {
            OnPropertyChanged(nameof(AutoCheckEnabled));
            OnPropertyChanged(nameof(LaunchAtStartup));
            OnPropertyChanged(nameof(InterfaceLanguage));
            OnPropertyChanged(nameof(HasUserDefaults));
            OnPropertyChanged(nameof(UserDefaultsStatusText));
            ApplyState(_updateService.CurrentState);
        }

        private void ApplyState(UpdateState state)
        {
            CurrentVersion = state.CurrentVersion;

            // 判断依据是"清单里有没有一个可用版本"，而不是阶段枚举：下载失败时阶段是 Failed，
            // 早期版本把它排除在外，于是整块更新区连同状态说明一起消失，用户看到的正是"回到发现新版本之前"，
            // 而且没有任何提示。只要还有清单，就必须留在这一页上，并让状态说明条说明失败原因。
            // The criterion is "does the manifest offer a usable version" rather than the phase enum: a failed
            // download has the Failed phase, and an earlier version excluded it, so the whole update block and its
            // status callout disappeared. The user saw exactly "back to before the update was found" with no
            // explanation at all. As long as a manifest exists the page has to keep showing it, with the callout
            // stating the failure reason.
            HasUpdate = state.Manifest is not null &&
                        state.Phase is not (UpdatePhase.Idle or UpdatePhase.Checking or UpdatePhase.UpToDate);

            // 检查本身就失败时没有清单可显示，因此失败原因需要一条独立的状态行，否则同样是"什么都没有发生"。
            // When the check itself failed there is no manifest to show, so the reason needs a status row of its own;
            // otherwise that failure also looks like "nothing happened".
            ShowStatusNotice = state.Phase == UpdatePhase.Failed && !HasUpdate;

            StatusText = UpdatePresentationPolicy.ResolveStatusText(
                state,
                SettingsManager.Current.Update.LastCheckUtc,
                DateTimeOffset.Now);
            ChannelText = UpdatePresentationPolicy.ResolveChannelText(state.ActiveSource);
            ProgressPercent = state.ProgressPercent;
            IsProgressVisible = UpdatePresentationPolicy.IsProgressVisible(state);
            CanCheck = !state.IsBusy;
            CanDownload = UpdatePresentationPolicy.ResolvePrimaryAction(state) == UpdatePrimaryAction.Download;
            CanCancel = UpdatePresentationPolicy.ResolvePrimaryAction(state) == UpdatePrimaryAction.Cancel;
            CanInstallNow = UpdatePresentationPolicy.CanInstallNow(state);
            CanSkip = UpdatePresentationPolicy.CanSkipVersion(state);
            CanOpenDownloadPage = state.Phase is UpdatePhase.ManualOnly or UpdatePhase.Failed ||
                                  state.Phase == UpdatePhase.Ready && state.IsInstallBlocked;
            // 判断依据是"这条原因是不是便携版那一条"，而不是"它是否等于当前语言的便携版文案"：状态里的文本是
            // 做出判断那一刻的语言，切换语言后两次取值属于不同语言，直接比较会让便携版提示消失。
            // The criterion is "is this reason the portable one" rather than "does it equal the portable wording in the active
            // language": the text inside the state belongs to the language of the decision, so a direct comparison would make
            // the portable notice vanish after a language switch.
            IsPortable = UpdateInstallPlanPolicy.IsPortableBlockedReason(state.InstallBlockedReason);
            HasSkippedVersion = !string.IsNullOrEmpty(SettingsManager.Current.Update.SkippedVersion);

            HighlightTitle = state.Manifest?.Title ?? (state.AvailableVersion is { } version ? $"AF Media Bar {version}" : string.Empty);
            HighlightDate = state.Manifest?.ReleaseDate is { } date ? date.ToString("yyyy-MM-dd") : string.Empty;

            HighlightEntries.Clear();
            foreach (var entry in state.Manifest?.Changelog ?? [])
            {
                HighlightEntries.Add(entry);
            }

            HasHighlights = HighlightEntries.Count > 0;
        }
    }
}
