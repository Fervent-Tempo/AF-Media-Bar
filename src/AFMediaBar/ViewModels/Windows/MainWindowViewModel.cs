using System.Windows.Input;
using AFMediaBar.Classes.Services;
using AFMediaBar.Views.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

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

        private SettingsWindow? _settingsWindow;

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        public ICommand SelectMediaSessionCommand { get; }

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        public ICommand ReconnectMediaSessionCommand { get; }

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>退出整个程序。/ Exits the application.</summary>
        public ICommand ExitApplicationCommand { get; }

        public MainWindowViewModel(MediaSessionService mediaSessionService)
        {
            SelectMediaSessionCommand = new RelayCommand<string>(key => mediaSessionService.SelectSession(key ?? string.Empty));
            ReconnectMediaSessionCommand = new AsyncRelayCommand(() => mediaSessionService.ReconnectAsync());
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            ExitApplicationCommand = new RelayCommand(() => Application.Current.Shutdown());
        }

        private void OpenSettings()
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = App.Services.GetRequiredService<SettingsWindow>();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
    }
}
