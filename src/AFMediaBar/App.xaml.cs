using AFMediaBar.Classes.Services;
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
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using Wpf.Ui;
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

                // 任务栏状态服务（系统托盘图标）Taskbar status service (system tray icon)
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // 任务栏停靠引擎（将媒体栏嵌入到资源管理器任务栏）
                // Taskbar docking engine (embeds the media bar into the Explorer taskbar)
                services.AddSingleton<ITaskbarDockService, TaskbarDockService>();

                // SMTC 媒体会话监听服务，生成 MediaSnapshot 快照供 UI 消费
                // SMTC media session monitoring service, producing MediaSnapshot for UI consumption
                services.AddSingleton<MediaSessionService>();

                // 导航服务（页面导航，不依赖具体窗口）Navigation service (page navigation, window-independent)
                services.AddSingleton<INavigationService, NavigationService>();

                // === 主窗口（隐藏的宿主窗口）Main Window (invisible host window) ===
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                // === 设置窗口（从任务栏右键菜单打开）Settings Window (opened from taskbar context menu) ===
                services.AddSingleton<SettingsWindowViewModel>();
                services.AddTransient<SettingsWindow>();

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
            await _host.StartAsync();

#if DEBUG
            // 实时歌词调试：快照事件已在 UI 线程触发，直接订阅。
            // Real-time lyrics debug: snapshot events are already dispatched to UI thread, subscribe directly.
            Services.GetRequiredService<MediaSessionService>().SnapshotChanged += DebugOutputLyrics;
#endif
        }

        /// <summary>
        /// 应用退出事件：停止 Host 并释放资源。
        /// Application exit event: stops the Host and disposes resources.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();

        }
#if DEBUG
        // === 实时歌词调试状态（仅 Debug 模式）Real-time Lyrics Debug State (Debug Mode Only) ===
        // 仅在歌词行变化时输出，避免 233ms 轮询刷屏。
        // Prints only when the active line changes, to avoid spam from the 233ms poll.
        private string? _debugLyricsLrc;
        private IReadOnlyList<LrcLine> _debugLyricsLines = [];
        private int _debugLyricsLastIndex = -1;

        /// <summary>
        /// 实时歌词调试：根据快照位置解析 LRC 并输出当前行（仅行变化时打印）。
        /// Real-time lyrics debug: resolves the active LRC line from the snapshot position (prints only on line change).
        /// </summary>
        private void DebugOutputLyrics(object? sender, MediaSnapshot snapshot)
        {
            if (snapshot.Lyrics is not { } lyrics || string.IsNullOrWhiteSpace(lyrics.Lrc))
            {
                return;
            }

            if (!string.Equals(_debugLyricsLrc, lyrics.Lrc, StringComparison.Ordinal))
            {
                _debugLyricsLrc = lyrics.Lrc;
                _debugLyricsLines = LrcParser.Parse(lyrics.Lrc);
                _debugLyricsLastIndex = -1;
            }

            var index = LrcParser.FindIndex(
                _debugLyricsLines,
                TimeSpan.FromSeconds(snapshot.Position));
            if (index < 0 || index == _debugLyricsLastIndex)
            {
                return;
            }

            _debugLyricsLastIndex = index;
            Debug.WriteLine(
                $"[Lyrics][{lyrics.Source}] {_debugLyricsLines[index].Time:mm\\:ss} {_debugLyricsLines[index].Text}");
        }
#endif
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