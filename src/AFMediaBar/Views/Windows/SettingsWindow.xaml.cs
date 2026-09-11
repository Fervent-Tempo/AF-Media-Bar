using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Classes.Services;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using AFMediaBar.Views.Pages;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 显示应用设置页面并承载 WPF-UI 导航。
    /// Displays application settings pages and hosts WPF-UI navigation.
    /// </summary>
    public partial class SettingsWindow : INavigationWindow
    {
        public SettingsWindowViewModel ViewModel { get; }

        /// <summary>
        /// 创建设置窗口并连接导航和外观服务。
        /// Creates the settings window and connects navigation and appearance services.
        /// </summary>
        public SettingsWindow(
            SettingsWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            WindowAppearanceService appearanceService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            SettingsResetDialog.SetHost(RootContentDialog);
            appearanceService.Attach(this);
            SetPageService(navigationViewPageProvider);

            navigationService.SetNavigationControl(RootNavigation);
        }

        #region INavigationWindow methods

        /// <summary>返回设置窗口导航控件。/ Returns the settings navigation control.</summary>
        public INavigationView GetNavigation() => RootNavigation;

        /// <summary>导航到指定页面类型。/ Navigates to the specified page type.</summary>
        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        /// <summary>设置导航页面提供器。/ Sets the navigation page provider.</summary>
        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            RootNavigation.SetPageProviderService(navigationViewPageProvider);

        /// <summary>显示设置窗口。/ Shows the settings window.</summary>
        public void ShowWindow() => Show();

        /// <summary>关闭设置窗口。/ Closes the settings window.</summary>
        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 调用 SetServiceProvider，提供 API。
        /// Provides the public SetServiceProvider entry point required by this component.
        /// </summary>
        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }
    }
}
