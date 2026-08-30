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
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
        // https://docs.microsoft.com/dotnet/core/extensions/configuration
        // https://docs.microsoft.com/dotnet/core/extensions/logging
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
            .ConfigureServices((context, services) =>
            {
                services.AddNavigationViewPageProvider();

                services.AddHostedService<ApplicationHostService>();

                // Theme manipulation
                services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // Taskbar docking engine (media bar embedded into the Explorer taskbar)
                services.AddSingleton<ITaskbarDockService, TaskbarDockService>();

                // SMTC media session monitoring producing MediaSnapshot for the UI chain
                services.AddSingleton<MediaSessionService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                // Settings window (opened from the taskbar context menu)
                services.AddSingleton<SettingsWindowViewModel>();
                services.AddTransient<SettingsWindow>();

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
        /// Gets services.
        /// </summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            await _host.StartAsync();

            // 实时歌词调试：快照事件已在 UI 线程触发，直接订阅。
            Services.GetRequiredService<MediaSessionService>().SnapshotChanged += DebugOutputLyrics;
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();

        }
#if DEBUG
        // 实时歌词调试状态：仅在歌词行变化时输出，避免 233ms 轮询刷屏。
        // Debug lyric state: prints only when the active line changes, to avoid spam from the 233ms poll.
        private string? _debugLyricsLrc;
        private IReadOnlyList<LrcLine> _debugLyricsLines = [];
        private int _debugLyricsLastIndex = -1;

        // 实时歌词调试：根据快照位置解析 LRC 并输出当前行（仅行变化时打印）。
        // Debug handler: resolves the active LRC line from the snapshot position.
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
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }
    }

    #endregion
}