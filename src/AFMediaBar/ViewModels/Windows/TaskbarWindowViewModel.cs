using System.Diagnostics;
using System.Windows.Input;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// TaskbarWindow 的视图模型：任务栏媒体栏的会话与播放控制，以及任务栏右键菜单命令。
    /// View model for TaskbarWindow: taskbar media-bar session and playback controls, plus its context-menu commands.
    /// </summary>
    public partial class TaskbarWindowViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;
        private readonly LocalizationService _localization;

        /// <summary>
        /// 右键菜单里更新那一行的标题：它随状态变化，因此下载百分比与"点击重启安装"都能在菜单里看到。
        /// Title of the update entry in the context menu. It follows the state, which is how the download percentage
        /// and the "click to restart and install" offer become visible inside the menu.
        /// </summary>
        [ObservableProperty]
        private string _updateMenuHeader = Translations.Get("Update.Tray.Check");

        /// <summary>更新那一行当前是否可点击：检查、下载与校验期间不可点击。/ Whether the update entry is clickable: it is not during a check, download or verification.</summary>
        [ObservableProperty]
        private bool _isUpdateMenuEnabled = true;

        /// <summary>
        /// 请求宿主窗口打开设置页。
        /// Requests that the host window open the settings page.
        /// </summary>
        public event EventHandler? OpenSettingsRequested;

        /// <summary>
        /// 请求宿主窗口打开设置页的「应用与关于」：更新提示被点击时跳到那一页。
        /// Requests that the host window open "application and about", which is where the update notice takes the
        /// user when it is clicked.
        /// </summary>
        public event EventHandler? OpenUpdateSettingsRequested;

        internal void RaiseOpenSettingsRequested() =>
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        internal void RaiseOpenUpdateSettingsRequested() =>
            OpenUpdateSettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        public ICommand SelectMediaSessionCommand { get; }

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        public ICommand ReconnectMediaSessionCommand { get; }

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>退出整个程序。/ Exits the application.</summary>
        public ICommand ExitApplicationCommand { get; }

        /// <summary>右键菜单的更新入口：按当前状态检查、取消下载、打开更新页或立即重启安装。/ The context menu's update entry: check, cancel the download, open the update page or restart and install, depending on the state.</summary>
        public ICommand UpdateMenuCommand { get; }

        /// <summary>切换当前媒体播放状态。/ Toggles playback for the selected media session.</summary>
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>播放上一首媒体。/ Skips to the previous item in the selected media session.</summary>
        public ICommand SkipPreviousCommand { get; }

        /// <summary>播放下一首媒体。/ Skips to the next item in the selected media session.</summary>
        public ICommand SkipNextCommand { get; }

        /// <summary>激活当前媒体来源应用。/ Activates the application that owns the selected media session.</summary>
        public ICommand ActivateMediaSourceCommand { get; }

        /// <summary>
        /// 创建任务栏状态适配器，并接通媒体操作、更新状态与右键菜单命令。
        /// Creates the taskbar state adapter and connects media actions, update state, and context-menu commands.
        /// </summary>
        /// <param name="mediaSessionService">媒体会话协调器。/ Media session coordinator.</param>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        /// <param name="localization">界面语言：更新那一行的标题由代码拼出，必须在语言变化后重取。/ Interface language: the update entry's title is composed in code and has to be fetched again when the language changes.</param>
        public TaskbarWindowViewModel(
            MediaSessionService mediaSessionService,
            UpdateService updateService,
            LocalizationService localization)
        {
            _updateService = updateService;
            _localization = localization;
            SelectMediaSessionCommand = new RelayCommand<string>(key => mediaSessionService.SelectSession(key ?? string.Empty));
            ReconnectMediaSessionCommand = new AsyncRelayCommand(() => mediaSessionService.ReconnectAsync());
            OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
            ExitApplicationCommand = new RelayCommand(() => Application.Current.Shutdown());
            UpdateMenuCommand = new RelayCommand(ExecuteUpdateAction);
            TogglePlayPauseCommand = new AsyncRelayCommand(mediaSessionService.TogglePlayPauseAsync);
            SkipPreviousCommand = new AsyncRelayCommand(mediaSessionService.SkipPreviousAsync);
            SkipNextCommand = new AsyncRelayCommand(mediaSessionService.SkipNextAsync);
            ActivateMediaSourceCommand = new RelayCommand(mediaSessionService.ActivateSelectedSource);

            // 视图模型与更新服务都是单例，订阅与进程同寿命；状态事件始终在 UI 线程上发布。
            // Both the view model and the update service are singletons, so the subscription lives as long as the
            // process; state events are always published on the UI thread.
            _updateService.UpdateStateChanged += ApplyUpdateState;

            // 托盘菜单那一行的标题是拼出来的文案，不是 XAML 里的动态资源，因此语言变化后要按新语言重新求值：
            // 只发 PropertyChanged 而不重算，属性值仍然是旧语言的那一句。
            // The tray entry's title is composed in code rather than being a dynamic resource in XAML, so it has to be
            // recomputed in the new language: raising PropertyChanged without recomputing would keep the old wording.
            _localization.LanguageChanged += OnLanguageChanged;

            ApplyUpdateState(_updateService.CurrentState);
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            ApplyUpdateState(_updateService.CurrentState);

            // 空的属性名让 WPF 重读全部绑定（CommunityToolkit 的 ObservableObject 支持）。
            // The empty property name makes WPF re-read every binding, which CommunityToolkit's ObservableObject
            // supports.
            OnPropertyChanged(string.Empty);
        }

        private void ApplyUpdateState(UpdateState state)
        {
            UpdateMenuHeader = UpdatePresentationPolicy.ResolveTrayHeader(state);
            IsUpdateMenuEnabled = UpdatePresentationPolicy.IsTrayHeaderEnabled(state);
        }

        private void ExecuteUpdateAction()
        {
            switch (UpdatePresentationPolicy.ResolveTrayAction(_updateService.CurrentState))
            {
                case UpdateTrayAction.Check:
                    _ = CheckForUpdatesAsync();
                    break;

                case UpdateTrayAction.Cancel:
                    _updateService.CancelDownload();
                    break;

                case UpdateTrayAction.InstallAndRestart:
                    _updateService.RequestInstallAndExit();
                    break;

                case UpdateTrayAction.OpenUpdatePage:
                    OpenUpdateSettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        /// <summary>
        /// 手动检查更新；异常只写进调试输出，因为一次失败的更新绝不能让右键菜单命令崩溃。
        /// Checks for updates manually; failures only reach the debug output, because a failed update must never
        /// crash a context-menu command.
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await _updateService.CheckAsync(manual: true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[Update] Manual check failed: {exception}");
            }
        }
    }
}
