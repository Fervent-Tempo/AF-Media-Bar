using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>呈现内存工具设置，编辑器与服务由既有作用域拥有。</summary>
public partial class MemoryToolsPage : INavigableView<ApplicationViewModel>
    {
        public ApplicationViewModel ViewModel { get; }

        private readonly SettingsPersistenceService _persistence;
        private readonly AppLogService _log;

        /// <summary>
        /// 创建应用页并注入页面状态、设置文件与日志入口。
        /// Creates the application page with its state, settings file and log entry points.
        /// </summary>
        /// <param name="viewModel">本页视图模型。/ This page's view model.</param>
        /// <param name="persistence">设置文件与用户默认值的所有者。/ Owner of the settings file and user defaults.</param>
        /// <param name="log">程序日志；「打开日志文件夹」直接调用它。/ The application log, which the log-folder entry calls.</param>
        public MemoryToolsPage(
            ApplicationViewModel viewModel,
            SettingsPersistenceService persistence,
            AppLogService log)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            _log = log;
            DataContext = this;

            InitializeComponent();
            Loaded += (_, _) => ViewModel.MemoryTools.SetVisible(true);
            Unloaded += (_, _) => ViewModel.MemoryTools.SetVisible(false);
        }

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();

        /// <summary>打开日志目录：日志只有一个文件，出问题时用户把这一份发出来即可。/ Opens the log directory; the log is one file and that one file is what the user sends when something breaks.</summary>
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => _log.OpenFolder();

        private async void ResetAllButton_Click(object sender, RoutedEventArgs e)
        {
            // 本页保留统一的“重置全部”；开发者开关同样遵循用户默认快照。
            if (await SettingsResetDialog.ConfirmAsync("About.ResetScope", resetAll: true)) ViewModel.ResetAll();
        }
    }
}
