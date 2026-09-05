using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 布局页面 ViewModel：管理窗口模式、排列方式和尺寸设置。
    /// Layout page ViewModel: manages window mode, layout orientation and size settings.
    ///
    /// 职责 Responsibilities:
    /// 1. 暴露布局设置选项供 UI 绑定
    ///    Expose layout setting options for UI binding
    /// 2. 处理窗口模式和排列方式的切换命令
    ///    Handle window mode and layout orientation change commands
    /// 3. 与 SettingsManager 交互保存设置
    ///    Interact with SettingsManager to save settings
    /// </summary>
    public partial class LayoutViewModel : ObservableObject
    {
        [ObservableProperty]
        private WindowMode _currentWindowMode = WindowMode.Taskbar;

        [ObservableProperty]
        private LayoutOrientationMode _currentLayoutOrientationMode = LayoutOrientationMode.Auto;

        public LayoutViewModel()
        {
            // 从设置管理器加载当前设置
            // Load current settings from settings manager
            CurrentWindowMode = SettingsManager.Current.WindowMode;
            CurrentLayoutOrientationMode = SettingsManager.Current.LayoutOrientationMode;
        }

        /// <summary>
        /// 切换到任务栏模式命令。
        /// Switch to taskbar mode command.
        /// </summary>
        [RelayCommand]
        private void OnSwitchToTaskbarMode()
        {
            if (CurrentWindowMode == WindowMode.Taskbar)
                return;

            CurrentWindowMode = WindowMode.Taskbar;
            SettingsManager.Current.WindowMode = WindowMode.Taskbar;

            // 触发布局设置变更事件
            // Trigger layout settings changed event
            SettingsManager.RaiseLayoutSettingsChanged(
                SettingsManager.Current.WindowMode,
                SettingsManager.Current.LayoutOrientationMode);
        }

        /// <summary>
        /// 切换到灵动岛模式命令。
        /// Switches to dynamic island mode.
        /// </summary>
        [RelayCommand]
        private void OnSwitchToDynamicIslandMode()
        {
            if (CurrentWindowMode == WindowMode.DynamicIsland)
                return;

            CurrentWindowMode = WindowMode.DynamicIsland;
            SettingsManager.Current.WindowMode = WindowMode.DynamicIsland;
            SettingsManager.RaiseLayoutSettingsChanged(
                SettingsManager.Current.WindowMode,
                SettingsManager.Current.LayoutOrientationMode);
        }

        /// <summary>
        /// 切换到自动排列模式命令。
        /// Switch to auto layout orientation command.
        /// </summary>
        [RelayCommand]
        private void OnSwitchToAutoLayout()
        {
            if (CurrentLayoutOrientationMode == LayoutOrientationMode.Auto)
                return;

            CurrentLayoutOrientationMode = LayoutOrientationMode.Auto;
            SettingsManager.Current.LayoutOrientationMode = LayoutOrientationMode.Auto;

            // 触发布局设置变更事件
            // Trigger layout settings changed event
            SettingsManager.RaiseLayoutSettingsChanged(
                SettingsManager.Current.WindowMode,
                SettingsManager.Current.LayoutOrientationMode);
        }

        /// <summary>
        /// 切换到横向排列模式命令。
        /// Switch to horizontal layout orientation command.
        /// </summary>
        [RelayCommand]
        private void OnSwitchToHorizontalLayout()
        {
            if (CurrentLayoutOrientationMode == LayoutOrientationMode.Horizontal)
                return;

            CurrentLayoutOrientationMode = LayoutOrientationMode.Horizontal;
            SettingsManager.Current.LayoutOrientationMode = LayoutOrientationMode.Horizontal;

            // 触发布局设置变更事件
            // Trigger layout settings changed event
            SettingsManager.RaiseLayoutSettingsChanged(
                SettingsManager.Current.WindowMode,
                SettingsManager.Current.LayoutOrientationMode);
        }

        /// <summary>
        /// 切换到纵向排列模式命令。
        /// Switch to vertical layout orientation command.
        /// </summary>
        [RelayCommand]
        private void OnSwitchToVerticalLayout()
        {
            if (CurrentLayoutOrientationMode == LayoutOrientationMode.Vertical)
                return;

            CurrentLayoutOrientationMode = LayoutOrientationMode.Vertical;
            SettingsManager.Current.LayoutOrientationMode = LayoutOrientationMode.Vertical;

            // 触发布局设置变更事件
            // Trigger layout settings changed event
            SettingsManager.RaiseLayoutSettingsChanged(
                SettingsManager.Current.WindowMode,
                SettingsManager.Current.LayoutOrientationMode);
        }
    }
}
