using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// 应用页：版本与更新、开机自启、界面语言、默认设置、设置文件与诊断日志。
    ///
    /// 它由原「应用与关于」页拆出。拆分的目的不是把一页变小，而是把两类内容分开：这一页全是"改了会影响程序怎么跑"的设置，
    /// 而「关于」只承载人、赞助与许可。导航顺序上它紧跟在「外观」后面，两者都是应用自身的设置。
    /// Application page: version and updates, run-at-startup, interface language, user defaults, the settings file, and
    /// diagnostics.
    ///
    /// It was split out of the former "application and about" page. The point was not to make one page smaller but to separate
    /// two kinds of content: everything here changes how the program behaves, while "about" carries only people, sponsors, and
    /// licenses. In the navigation it sits right after "appearance", since both are the application's own settings.
    /// </summary>
    public partial class ApplicationPage : INavigableView<ApplicationViewModel>
    {
        public ApplicationViewModel ViewModel { get; }

        private readonly SettingsPersistenceService _persistence;
        private readonly AppLogService _log;
        private readonly MemoryPruneCoordinator _pruneCoordinator;
        /// <summary>Bubbles a semantic navigation request to the owning settings window.</summary>
        public static readonly RoutedEvent OpenHighlightsEvent = EventManager.RegisterRoutedEvent(nameof(OpenHighlights), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ApplicationPage));
        /// <summary>Requests the common highlights destination without retaining a global navigation control.</summary>
        public event RoutedEventHandler OpenHighlights { add => AddHandler(OpenHighlightsEvent, value); remove => RemoveHandler(OpenHighlightsEvent, value); }

        /// <summary>
        /// 创建应用页并注入它需要的三个入口服务：设置文件、日志与内存回收。
        /// Creates the application page with its three entry services: settings file, log, and memory reclaim.
        /// </summary>
        /// <param name="viewModel">本页视图模型。/ This page's view model.</param>
        /// <param name="persistence">设置文件与用户默认值的所有者。/ Owner of the settings file and user defaults.</param>
        /// <param name="log">程序日志；「打开日志文件夹」直接调用它。/ The application log, which the log-folder entry calls.</param>
        /// <param name="pruneCoordinator">内存剪枝协调器；「压缩内存占用」请求它回收一次。/ The prune coordinator, which the compress-memory entry asks for one reclaim.</param>
        public ApplicationPage(
            ApplicationViewModel viewModel,
            SettingsPersistenceService persistence,
            AppLogService log,
            MemoryPruneCoordinator pruneCoordinator)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            _log = log;
            _pruneCoordinator = pruneCoordinator;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

        private void OnOpenHighlights(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenHighlightsEvent));

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();

        /// <summary>打开日志目录：日志只有一个文件，出问题时用户把这一份发出来即可。/ Opens the log directory; the log is one file and that one file is what the user sends when something breaks.</summary>
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => _log.OpenFolder();

        /// <summary>
        /// 手动把工作集交还给系统。
        /// Returns the working set to the system on request.
        ///
        /// 这里刻意不做任何反馈动画或提示：回收在后台线程执行，而且它只改变"物理内存占用"这一个读数，提交量不变——按钮旁边的说明已经把
        /// 这一点写清楚了，再补一个"已完成"的气泡只会让人以为释放了更多东西。
        /// No feedback animation or toast is shown on purpose: the reclaim runs on a background thread and only changes one reading — physical memory in
        /// use — while the commit size stays the same, which the description next to the button already says, and a "done" balloon would only suggest
        /// that more had been released.
        /// </summary>
        private void TrimMemory_Click(object sender, RoutedEventArgs e) =>
            _pruneCoordinator.RequestTrim(MemoryTrimTrigger.ManualRequest);

        private async void ResetAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 本页保留统一的“重置全部”；开发者开关同样遵循用户默认快照。
            if (await SettingsResetDialog.ConfirmAsync("About.ResetScope", resetAll: true)) ViewModel.ResetAll();
        }
    }
}
