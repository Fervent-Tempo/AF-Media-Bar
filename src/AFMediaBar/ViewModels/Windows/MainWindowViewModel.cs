using System.Windows.Input;
using AFMediaBar.Classes.Services;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// MainWindow 的视图模型：宿主窗口元数据 + 任务栏右键菜单的媒体控制/设置/退出命令。
    /// View model for MainWindow: host window metadata plus the taskbar context menu commands
    /// (media control, settings, exit).
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "AFMediaBar";

        /// <summary>
        /// 请求宿主窗口打开设置页。
        /// Requests that the host window open the settings page.
        /// </summary>
        public event EventHandler? OpenSettingsRequested;

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        public ICommand SelectMediaSessionCommand { get; }

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        public ICommand ReconnectMediaSessionCommand { get; }

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>退出整个程序。/ Exits the application.</summary>
        public ICommand ExitApplicationCommand { get; }

        /// <summary>切换当前媒体播放状态。/ Toggles playback for the selected media session.</summary>
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>播放上一首媒体。/ Skips to the previous item in the selected media session.</summary>
        public ICommand SkipPreviousCommand { get; }

        /// <summary>播放下一首媒体。/ Skips to the next item in the selected media session.</summary>
        public ICommand SkipNextCommand { get; }

        /// <summary>跳转当前媒体进度。 / Seeks the selected media item.</summary>
        public ICommand SeekCommand { get; }

        /// <summary>切换循环模式。 / Cycles the repeat mode.</summary>
        public ICommand CycleRepeatCommand { get; }

        /// <summary>激活当前媒体来源应用。/ Activates the application that owns the selected media session.</summary>
        public ICommand ActivateMediaSourceCommand { get; }

        /// <summary>
        /// 调用 MainWindowViewModel，提供 API。
        /// Provides the public MainWindowViewModel entry point required by this component.
        /// </summary>
        public MainWindowViewModel(MediaSessionService mediaSessionService)
        {
            SelectMediaSessionCommand = new RelayCommand<string>(key => mediaSessionService.SelectSession(key ?? string.Empty));
            ReconnectMediaSessionCommand = new AsyncRelayCommand(() => mediaSessionService.ReconnectAsync());
            OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
            ExitApplicationCommand = new RelayCommand(() => Application.Current.Shutdown());
            TogglePlayPauseCommand = new AsyncRelayCommand(mediaSessionService.TogglePlayPauseAsync);
            SkipPreviousCommand = new AsyncRelayCommand(mediaSessionService.SkipPreviousAsync);
            SkipNextCommand = new AsyncRelayCommand(mediaSessionService.SkipNextAsync);
            SeekCommand = new AsyncRelayCommand<double>(mediaSessionService.SeekAsync);
            CycleRepeatCommand = new AsyncRelayCommand(mediaSessionService.CycleRepeatModeAsync);
            ActivateMediaSourceCommand = new RelayCommand(mediaSessionService.ActivateSelectedSource);
        }

    }
}
