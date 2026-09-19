using System.Diagnostics;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// MainWindow 的视图模型：宿主窗口元数据 + 托盘菜单的会话切换/设置/退出命令，以及更新提示。
    /// View model for MainWindow: host window metadata, tray menu session switching/settings/exit commands,
    /// and the update entry.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;
        private readonly LocalizationService _localization;

        [ObservableProperty] private string _applicationTitle = "AFMediaBar";

        /// <summary>
        /// 右键菜单里更新那一行的标题：它随状态变化，因此下载百分比与"点击重启安装"都能在菜单里看到。
        /// Title of the update entry in the context menu. It follows the state, which is how the download percentage
        /// and the "click to restart and install" offer become visible inside the menu.
        /// </summary>
        [ObservableProperty] private string _updateMenuHeader = Translations.Get("Update.Tray.Check");

        /// <summary>更新那一行当前是否可点击：检查、下载与校验期间不可点击。/ Whether the update entry is clickable: it is not during a check, download or verification.</summary>
        [ObservableProperty] private bool _isUpdateMenuEnabled = true;

        /// <summary>
        /// 创建主窗口状态适配器，并接通托盘菜单所需的会话操作与更新状态。
        /// Creates the main-window state adapter and connects the session actions and update state required by
        /// the tray menu.
        /// </summary>
        /// <param name="updateService">更新下载器协调器。/ Update downloader coordinator.</param>
        /// <param name="localization">界面语言：更新那一行的标题由代码拼出，必须在语言变化后重取。/ Interface language: the update entry's title is composed in code and has to be fetched again when the language changes.</param>
        public MainWindowViewModel(
            UpdateService updateService,
            LocalizationService localization)
        {
            _updateService = updateService;
            _localization = localization;


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
    }
}
