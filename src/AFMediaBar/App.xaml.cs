using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Pages;
using AFMediaBar.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.DependencyInjection;

namespace AFMediaBar
{
    /// <summary>
    /// 应用程序入口：负责 DI 容器构建、服务注册和应用生命周期管理。
    /// Application entry point: responsible for DI container setup, service registration, and lifecycle management.
    ///
    /// 职责 Responsibilities:
    /// 1. 配置依赖注入容器（Services、ViewModels、Views）
    ///    Configure dependency injection container (Services, ViewModels, Views)
    /// 2. 启动 ApplicationHostService 创建主窗口
    ///    Start ApplicationHostService to create the main window
    /// 3. 处理应用启动和退出事件
    ///    Handle application startup and exit events
    /// </summary>
    public partial class App
    {
        private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(5);

        // .NET Generic Host 提供依赖注入、配置、日志等服务。
        // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                // === WPF-UI 导航服务 WPF-UI Navigation Service ===
                services.AddNavigationViewPageProvider();

                // === 应用生命周期宿主服务 Application Lifecycle Host Service ===
                services.AddHostedService<ApplicationHostService>();

                // === 核心服务层 Core Service Layer ===
                // 主题管理（深浅色主题切换）Theme management (light/dark theme switching)
                services.AddSingleton<IThemeService, ThemeService>();

                // WPF-UI 任务栏状态服务（不创建 Shell 通知区域图标）
                // WPF-UI taskbar-state service (does not create a Shell notification icon)
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // 任务栏停靠引擎（将媒体栏嵌入到资源管理器任务栏）
                // Taskbar docking engine (embeds the media bar into the Explorer taskbar)
                services.AddSingleton<ITaskbarDockService, TaskbarDockService>();
                services.AddSingleton<ITaskbarOccupiedAreaProbe, TaskbarOccupiedAreaProbe>();
                services.AddSingleton<TaskbarOccupiedAreaService>();

                // SMTC 媒体会话监听服务，生成 MediaSnapshot 快照供 UI 消费
                // SMTC media session monitoring service, producing MediaSnapshot for UI consumption
                services.AddSingleton<LyricsService>(_ => new LyricsService(
                    new NetEaseLyricsProvider(),
                    new LrclibLyricsProvider()));
                services.AddSingleton<MediaSessionCatalog>();
                services.AddSingleton<MediaSessionSelectionService>();
                services.AddSingleton<MediaSnapshotBuilder>();
                services.AddSingleton<IMediaSourceProvider, NetEaseMediaProvider>();
                services.AddSingleton<MediaSourceActivationService>();
                services.AddSingleton<MediaSessionService>();
                services.AddSingleton<MediaSourceProcessResolver>();
                services.AddSingleton<AudioProcessInfoService>();
                services.AddSingleton<ApplicationIconService>();
                services.AddSingleton<ApplicationVolumeService>();
                services.AddSingleton<AudioDeviceService>();
                services.AddSingleton<SpatialAudioService>();
                // 自有 Shell 托盘图标与统一鼠标输入监听
                // App-owned Shell tray icon and unified mouse input monitor
                services.AddSingleton<ShellTrayIconService>();
                services.AddSingleton<NativeMouseInputMonitor>();
                services.AddSingleton<NativeWindowBackdropAdapter>();
                services.AddSingleton<WindowAppearanceService>();

                // 导航服务（页面导航，不依赖具体窗口）Navigation service (page navigation, window-independent)
                services.AddSingleton<INavigationService, NavigationService>();

                // === 主窗口（隐藏的宿主窗口）Main Window (invisible host window) ===
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<AudioControlViewModel>();
                services.AddTransient<DynamicIslandWindow>();
                services.AddSingleton<AudioControlFlyoutWindow>();

                // === 设置窗口（从任务栏右键菜单打开）Settings Window (opened from taskbar context menu) ===
                services.AddSingleton<SettingsWindowViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<Func<SettingsWindow>>(sp =>
                    () => sp.GetRequiredService<SettingsWindow>());

                // === 设置页面及其 ViewModel Settings Pages and ViewModels ===
                services.AddSingleton<GeneralPage>();
                services.AddSingleton<GeneralViewModel>();

                services.AddSingleton<AppearancePage>();
                services.AddSingleton<AppearanceViewModel>();

                services.AddSingleton<LayoutPage>();
                services.AddSingleton<LayoutViewModel>();

                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                services.AddSingleton<AboutPage>();
                services.AddSingleton<AboutViewModel>();
            }).Build();

        private ApplicationThemeCoordinator? _themeCoordinator;
#if DEBUG
        private DebugLyricsDiagnostics? _debugLyricsDiagnostics;
#endif

        #region FrameWork

        /// <summary>
        /// 全局服务提供者，供应用各处获取依赖服务。
        /// Global service provider for retrieving dependency services throughout the application.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// 应用启动事件：启动 Host 并订阅调试事件。
        /// Application startup event: starts the Host and subscribes to debug events.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            _themeCoordinator = new ApplicationThemeCoordinator(Dispatcher, UpdateAppearanceResources);
            _themeCoordinator.Start();
            _themeCoordinator.Apply(SettingsManager.Current.Appearance);
            await _host.StartAsync();

#if DEBUG
            _debugLyricsDiagnostics = new DebugLyricsDiagnostics();
            Services.GetRequiredService<MediaSessionService>().SnapshotChanged += _debugLyricsDiagnostics.OnSnapshotChanged;
#endif
        }

        /// <summary>
        /// 应用退出事件：停止 Host 并释放资源。
        /// Application exit event: stops the Host and disposes resources.
        /// </summary>
        private void OnExit(object sender, ExitEventArgs e)
        {
#if DEBUG
            if (_debugLyricsDiagnostics is not null)
                Services.GetRequiredService<MediaSessionService>().SnapshotChanged -= _debugLyricsDiagnostics.OnSnapshotChanged;
            _debugLyricsDiagnostics = null;
#endif
            _themeCoordinator?.Dispose();
            _themeCoordinator = null;

            // WPF 正在关闭 Dispatcher 时不能从 async void Exit 处理器等待后再恢复到 UI 线程，
            // 否则 Host.Dispose 可能永远不执行，媒体与 Shell 服务会让进程残留。
            // Do not await from an async-void Exit handler while WPF is shutting down its
            // Dispatcher; the continuation may never run and leave hosted resources alive.
            using var shutdown = new CancellationTokenSource(HostShutdownTimeout);
            try
            {
                _host.StopAsync(shutdown.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                Debug.WriteLine("[App] Host shutdown timed out; disposing remaining services.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Host shutdown failed: {exception}");
            }
            finally
            {
                _host.Dispose();
            }
        }

        private void UpdateAppearanceResources(AppearanceSettings appearance, ApplicationTheme theme)
        {
            var fontFamily = new FontFamily(appearance.ResolveFontFamilySource(SystemFonts.MessageFontFamily.Source));
            var fontWeight = FontWeight.FromOpenTypeWeight(appearance.FontWeight);
            Resources["AppTextFontFamily"] = fontFamily;
            Resources["ContentControlThemeFontFamily"] = fontFamily;
            Resources["AppTextFontWeight"] = fontWeight;
            Resources["AppTextMediumFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 100, 100, 999));
            Resources["AppTextStrongFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 200, 100, 999));

            var dark = theme == ApplicationTheme.Dark || theme == ApplicationTheme.HighContrast && SystemParameters.HighContrast;
            // Context menus always use an opaque Fluent solid surface. Native
            // Mica/Acrylic on Popup HWNDs leaves transparent hit-test regions
            // that can pass clicks through to the window behind the menu.
            var menuColor = dark ? Color.FromRgb(44, 44, 44) : Color.FromRgb(249, 249, 249);
            var menuBrush = new SolidColorBrush(menuColor);
            menuBrush.Freeze();
            Resources["AppMenuBackgroundBrush"] = menuBrush;
            Resources["ContextMenuBackground"] = menuBrush;
        }

        /// <summary>
        /// 应用未处理异常事件：捕获全局异常以防止应用崩溃。
        /// Application unhandled exception event: catches global exceptions to prevent crashes.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 可在此处添加日志记录或错误上报逻辑
            // Add logging or error reporting logic here
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }

        #endregion
    }
}
