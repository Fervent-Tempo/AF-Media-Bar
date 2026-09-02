using AFMediaBar.Views.Pages;
using AFMediaBar.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;

namespace AFMediaBar.Classes.Services
{
    /// <summary>
    /// 应用托管服务：负责在应用启动时创建主窗口。
    /// Application hosted service: responsible for creating the main window on app startup.
    ///
    /// 职责 Responsibilities:
    /// 1. 在 StartAsync 中创建并显示 MainWindow
    ///    Create and show MainWindow in StartAsync
    /// 2. 管理应用生命周期（启动和关闭）
    ///    Manage application lifecycle (startup and shutdown)
    ///
    /// ⚠️ 注意 Note:
    /// 此类由 .NET Generic Host 自动调用，在 App.xaml.cs 中注册为 HostedService。
    /// This class is automatically called by .NET Generic Host, registered as HostedService in App.xaml.cs.
    /// </summary>
    public class ApplicationHostService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        private INavigationWindow _navigationWindow;

        public ApplicationHostService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 应用宿主准备启动服务时触发：创建主窗口。
        /// Triggered when the application host is ready to start: creates the main window.
        /// </summary>
        /// <param name="cancellationToken">指示启动进程已被中止 Indicates that the start process has been aborted.</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await HandleActivationAsync();
        }

        /// <summary>
        /// 应用宿主执行优雅关闭时触发。
        /// Triggered when the application host is performing a graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">指示关闭进程不应再优雅 Indicates that the shutdown process should no longer be graceful.</param>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// 激活期间创建主窗口：检查是否已存在，不存在则创建。
        /// Creates main window during activation: checks if it exists, creates if not.
        /// </summary>
        private async Task HandleActivationAsync()
        {
            if (!Application.Current.Windows.OfType<MainWindow>().Any())
            {
                _navigationWindow = (
                    _serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow
                )!;
                _navigationWindow!.ShowWindow();

                // 导航到默认页面（通用设置页）
                // Navigate to the default page (General settings page)
                _navigationWindow.Navigate(typeof(Views.Pages.GeneralPage));
            }

            await Task.CompletedTask;
        }
    }
}