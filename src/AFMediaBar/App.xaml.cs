using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Credits;
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
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Components;
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
        private int _exitHandled;

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

                // 程序日志：其它服务的构造都会经过它（设置加载、媒体切换、音频应用、更新链路、退出）。
                // The application log: every other service writes into it (settings loads, media switches, audio applies, the update chain, exit).
                services.AddSingleton(_ => new AppLogService());

                // 后台内存与休眠剪枝：电源状态监听 → 参与者各自回收自己的资源 → 进程级回收。
                // Background memory and suspend pruning: the power state monitor, then each participant reclaiming its own resources, then the
                // process-level reclaim.
                services.AddSingleton<PowerStateMonitor>();
                services.AddSingleton<ProcessMemoryTrimmer>();
                services.AddSingleton<MemoryPruneCoordinator>();

                // === 核心服务层 Core Service Layer ===
                // 主题管理（深浅色主题切换）Theme management (light/dark theme switching)
                services.AddSingleton<IThemeService, ThemeService>();

                // WPF-UI 任务栏状态服务（不创建 Shell 通知区域图标）
                // WPF-UI taskbar-state service (does not create a Shell notification icon)
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // 显示器目录为通知、设置和任务栏停靠提供统一的设备标识解析。
                // The display catalog provides shared device-identity resolution for notifications,
                // settings, and taskbar docking.
                services.AddSingleton<IDisplayMonitorService, DisplayMonitorService>();

                // 任务栏停靠引擎（将媒体栏嵌入到资源管理器任务栏）
                // Taskbar docking engine (embeds the media bar into the Explorer taskbar)
                services.AddSingleton<ITaskbarDockService, TaskbarDockService>();
                services.AddSingleton<ITaskbarOccupiedAreaProbe, TaskbarOccupiedAreaProbe>();
                services.AddSingleton<TaskbarOccupiedAreaService>();
                services.AddSingleton<TaskbarLengthConstraintsService>();

                // SMTC 媒体会话监听服务，生成 MediaSnapshot 快照供 UI 消费
                // SMTC media session monitoring service, producing MediaSnapshot for UI consumption
                //
                // 歌词提供器的顺序就是优先级：精确来源在前，模糊搜索在后；每多一个来源只增加一次未命中时的尝试。
                // The provider order is the priority: exact sources first, fuzzy searches last. Every extra source only adds
                // one attempt after the earlier ones miss.
                services.AddSingleton<LyricsService>(_ => LyricsProviderFactory.CreateDefaultService());
                services.AddSingleton<MediaSessionCatalog>();
                services.AddSingleton<MediaSessionSelectionService>();
                services.AddSingleton<MediaSnapshotBuilder>();
                services.AddSingleton<IMediaSourceProvider, NetEaseMediaProvider>();

                // 可剪枝的参与者：每个资源的所有者自己实现回收，协调器只按档位发通知。
                // The prunable participants: each resource owner implements its own reclaim while the coordinator only publishes a level.
                services.AddSingleton<IMemoryPrunable>(sp => sp.GetRequiredService<MediaSnapshotBuilder>());
                services.AddSingleton<IMemoryPrunable>(sp => sp.GetRequiredService<MediaSessionSelectionService>());
                services.AddSingleton<IMemoryPrunable>(sp => (IMemoryPrunable)sp.GetRequiredService<IMediaSourceProvider>());
                services.AddSingleton<MediaSourceActivationService>();
                services.AddSingleton<MediaSessionService>();
                services.AddSingleton<TrackChangeNotificationCoordinator>();
                services.AddSingleton<MediaSourceProcessResolver>();
                services.AddSingleton<AudioProcessInfoService>();
                services.AddSingleton<ApplicationIconService>();
                services.AddSingleton<ApplicationVolumeService>();
                services.AddSingleton<AudioDeviceService>();
                services.AddSingleton<AudioInteractionService>();
                services.AddSingleton<GlobalInteractionRouter>();
                services.AddSingleton<AudioMonitorService>();
                services.AddSingleton<IMemoryPrunable>(sp => sp.GetRequiredService<AudioMonitorService>());
                services.AddSingleton<SystemMetricsService>();
                services.AddSingleton<SystemMetricsMonitorService>();
                services.AddSingleton<IMemoryPrunable>(sp => sp.GetRequiredService<SystemMetricsMonitorService>());
                services.AddSingleton<SpatialAudioService>();
                // 自有 Shell 托盘图标与统一鼠标输入监听
                // App-owned Shell tray icon and unified mouse input monitor
                services.AddSingleton<ShellTrayIconService>();
                services.AddSingleton<NativeMouseInputMonitor>();
                services.AddSingleton<NativeWindowBackdropAdapter>();
                services.AddSingleton<AppIconService>();
                services.AddSingleton<WindowAppearanceService>();
                services.AddSingleton<ScreenBackgroundSampler>();
                services.AddSingleton<SettingsPersistenceService>();
                services.AddSingleton<StartupRegistrationService>();

                // 界面语言：把设置里的选项解析成生效语言，并重发 XAML 引用的文案资源。
                // Interface language: resolves the settings option into the language in effect and republishes the text
                // resources XAML references.
                services.AddSingleton<LocalizationService>();

                // 安装协调互斥体：只让安装程序能识别"程序正在运行"，不改变单实例行为。
                // Install-coordination mutex: lets the installer notice a running instance without changing single-instance behaviour.
                services.AddSingleton<InstallCoordinatorMutex>();

                // 更新下载器：清单读取、安装包下载与校验、退出时的安装交接。
                // Update downloader: manifest reading, installer download and verification, and the install hand-off on exit.
                services.AddSingleton<UpdateManifestClient>();
                services.AddSingleton<UpdatePackageStore>();
                services.AddSingleton<UpdatePackageDownloader>();
                services.AddSingleton<InstalledApplicationProbe>();
                services.AddSingleton<UpdateService>();

                // 导航服务（页面导航，不依赖具体窗口）Navigation service (page navigation, window-independent)
                services.AddSingleton<INavigationService, NavigationService>();

                // === 主窗口（隐藏的宿主窗口）Main Window (invisible host window) ===
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<TaskbarWindowViewModel>();
                services.AddSingleton<AudioControlViewModel>();
                services.AddTransient<DynamicIslandWindow>();
                services.AddSingleton<AudioControlFlyoutWindow>();

                // === 设置窗口（从任务栏右键菜单打开）Settings Window (opened from taskbar context menu) ===
                services.AddSingleton<SettingsWindowViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<Func<SettingsWindow>>(sp =>
                    () => sp.GetRequiredService<SettingsWindow>());
                services.AddTransient<TaskbarFullPanelWindow>();
                services.AddSingleton<Func<TaskbarFullPanelWindow>>(sp =>
                    () => sp.GetRequiredService<TaskbarFullPanelWindow>());
                services.AddTransient<TrackChangeNotificationWindow>();
                services.AddSingleton<Func<TrackChangeNotificationWindow>>(sp =>
                    () => sp.GetRequiredService<TrackChangeNotificationWindow>());

                // === 设置页面及其 ViewModel Settings Pages and ViewModels ===
                services.AddSingleton<AppearancePage>();
                services.AddSingleton<AppearanceViewModel>();

                services.AddSingleton<LayoutPage>();
                services.AddSingleton<LayoutViewModel>();

                services.AddSingleton<DisplayModesPage>();
                services.AddSingleton<DisplayModesViewModel>();
                services.AddSingleton<ExtraFeaturesPage>();
                services.AddSingleton<ExtraFeaturesViewModel>();

                services.AddSingleton<InteractionPage>();
                services.AddSingleton<InteractionViewModel>();

                services.AddSingleton<LyricsPage>();
                services.AddSingleton<LyricsViewModel>();

                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                // 应用页与关于页由原「应用与关于」拆分而来：设置留在应用页，人与许可移到关于页。
                // The application and about pages come from splitting the former "application and about": settings stay on the
                // application page while people and licenses moved to about.
                services.AddSingleton<ApplicationPage>();
                services.AddSingleton<ApplicationViewModel>();

                // 关于页的名单服务：贡献者与赞助者名单（缓存 + 仓库快照回退），只被关于页使用。
                // The credits service behind the about page: contributor and sponsor lists with caching and a repository-snapshot fallback, used only
                // by that page.
                services.AddSingleton<CreditsService>();
                services.AddSingleton<AvatarImageLoader>();

                services.AddSingleton<AboutPage>();
                services.AddSingleton<AboutViewModel>();

                // 组件相关VM
                services.AddSingleton<AFContextMenuViewModel>();
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
            // 日志最先建立：它之后的每一步（设置读写、语言、宿主、托盘、更新）都往里写，出问题时整份目录即可上报。
            // The log comes first: everything after it — settings I/O, language, host, tray, updates — writes into it, so a bug report is just
            // that directory.
            var log = Services.GetRequiredService<AppLogService>();
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            log.LogSessionStart();

            var settingsPersistenceService = Services.GetRequiredService<SettingsPersistenceService>();
            settingsPersistenceService.Initialize();

            // 「我的默认设置」快照必须在宿主启动前装载：设置页上的「恢复默认设置」在用户点下去的那一刻就要
            // 回到用户自己的默认值，而不是等下一次启动才生效。
            // The user-defaults snapshot has to be loaded before the host starts: a "restore defaults" click must land on the
            // user's own defaults immediately, not only after the next start.
            SettingsManager.SetUserDefaults(settingsPersistenceService.LoadUserDefaults());

            // 界面语言必须在宿主启动之前应用：托盘图标、任务栏媒体栏与设置页的文案都是在构造时取出来的，
            // 先建后刷会让第一帧短暂停在另一种语言上。这里排在设置加载之后，因此读到的是用户文件里的选项。
            // The interface language has to be applied before the host starts: the tray icon, the taskbar media bar, and
            // the settings pages take their text while they are constructed, and building first and refreshing afterwards
            // would leave the first frame in another language. It runs after the settings are loaded, so the option comes
            // from the user's own file.
            Services.GetRequiredService<LocalizationService>().Start();

            // 开机自动启动：设置是意图，注册表 Run 项是它的执行结果，因此启动时按设置核对一次。
            // 只写 HKCU，不提权；写失败只记录原因，不影响启动链。
            // Run-at-startup: the setting is the intent and the registry Run entry is its effect, so startup reconciles them once.
            // Only HKCU is written and no elevation is requested; a failure is only logged and never disturbs startup.
            var startupRegistration = Services.GetRequiredService<StartupRegistrationService>();
            var startupFailure = startupRegistration.Apply(SettingsManager.Current.LaunchAtStartup);
            if (startupFailure is not null)
                Debug.WriteLine($"[App] Run-at-startup registration failed: {startupFailure}");
            // 目标显示器直接按设备标识解析：设置文件只读取当前 schema，旧的"排序索引"不会再出现在内存里，
            // 因此这里不需要（也没有）等待显示器拓扑就绪再迁移一次的启动路径。
            // The target display resolves straight from its device identifier: the settings file only reads the current schema, so a
            // legacy sorted index never reaches memory and there is no startup-time migration waiting for the monitor topology.
            var displayMonitorService = Services.GetRequiredService<IDisplayMonitorService>();
            displayMonitorService.Refresh();

            // 更新在"这一次启动之前"安装。
            //
            // 退出时安装会把安装程序窗口留在用户刚关掉程序之后，看起来像程序自己又起来了一次；放在这里，
            // 用户看到的顺序是「打开程序 → 安装进度 → 新版本启动」。必须在设置加载之后、宿主启动之前：
            // 设置没加载时 OnExit 的 Flush 会把默认设置写回用户文件，而宿主启动后又会先建出托盘图标与任务栏媒体栏。
            // The update is installed *before* this start.
            //
            // Installing on exit leaves an installer window right after the user closed the application, which looks
            // like the application starting itself again; here the order the user sees is "open the app → install
            // progress → the new version starts". It has to happen after the settings are loaded and before the host
            // starts: without loaded settings, the flush in OnExit would write defaults over the user's file, and once
            // the host has started the tray icon and taskbar bar would already exist.
            var updateService = Services.GetRequiredService<UpdateService>();
            updateService.RestartRequested += (_, _) => Shutdown();
            if (updateService.TryLaunchPendingInstallOnStartup())
            {
                Debug.WriteLine("[App] A pending update is being installed before this start; exiting now.");
                Shutdown();
                return;
            }
            _themeCoordinator = new ApplicationThemeCoordinator(Dispatcher, UpdateAppearanceResources);
            _themeCoordinator.Start();
            _themeCoordinator.Apply(SettingsManager.Current.Appearance);

            // DWM 的强调色变化消息由窗口外观服务统一接收；转交协调器后整套应用级画刷会一起更新。
            // The window appearance service receives DWM's accent-change message; forwarding it makes the coordinator refresh
            // the whole set of application-level brushes at once.
            Services.GetRequiredService<WindowAppearanceService>().SystemColorizationChanged +=
                () => _themeCoordinator?.Apply(SettingsManager.Current.Appearance);
            await _host.StartAsync();

            // 安装协调互斥体必须在 Host 启动后创建：更新链路会在启动安装包之前释放它（见 InstallCoordinatorMutex）。
            // The install-coordination mutex is created after the host starts; the update path releases it before
            // starting the installer (see InstallCoordinatorMutex). A failure here must never affect startup.
            Services.GetRequiredService<InstallCoordinatorMutex>().Acquire();

            // 更新排期在宿主就绪之后启动：它自己带首检延迟，因此不会和媒体会话、任务栏停靠抢启动资源。
            // Update scheduling starts once the host is ready; it carries its own initial delay, so it never competes
            // with media sessions or taskbar docking during startup.
            updateService.Start();

            // 后台剪枝最后启动：它要等媒体会话目录已经就绪才判得准"现在有没有在播"，而且电源消息窗口必须在 UI 线程上建立
            // （SystemEvents 的回调会从自己的线程进来，本类统一把它们搬回这里）。
            // Background pruning starts last: it can only judge "is anything playing" once the media session catalog is up, and its power message
            // window has to be created on the UI thread, which is also the thread every SystemEvents callback is marshalled back to.
            Services.GetRequiredService<MemoryPruneCoordinator>().Start();

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
            // Exit can be requested by both a ContextMenu command and MainWindow.OnClosed.
            // Process the cleanup boundary only once so a re-entrant Shutdown call cannot
            // interrupt disposal halfway through.
            if (Interlocked.Exchange(ref _exitHandled, 1) != 0)
            {
                return;
            }

            // 这两个服务必须在 Host 释放之前取出来。
            // Host.Dispose 同时释放 DI 容器，之后再从 App.Services 解析任何东西都会抛 ObjectDisposedException；
            // 那既会跳过退出时的安装交接，也会把一次正常退出变成一次崩溃（WER 里是 e0434352）。
            // These two services must be resolved before the host is disposed.
            // Host.Dispose also disposes the DI container, and resolving anything from App.Services afterwards
            // throws ObjectDisposedException, which would both skip the install hand-off on exit and turn a normal
            // exit into a crash recorded as e0434352.
            var updateService = Services.GetRequiredService<UpdateService>();
            var installCoordinatorMutex = Services.GetRequiredService<InstallCoordinatorMutex>();

#if DEBUG
            if (_debugLyricsDiagnostics is not null)
                Services.GetRequiredService<MediaSessionService>().SnapshotChanged -= _debugLyricsDiagnostics.OnSnapshotChanged;
            _debugLyricsDiagnostics = null;
#endif
            _themeCoordinator?.Dispose();
            _themeCoordinator = null;

            try
            {
                Services.GetRequiredService<SettingsPersistenceService>().Flush();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Settings flush failed: {exception}");
            }

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

            // 只有用户明确点过"立即重启并安装"时才在退出边界启动安装程序。
            //
            // 自动更新已经不在退出时发生：它在**下一次启动之前**执行（见 TryLaunchPendingInstallOnStartup），
            // 否则安装程序窗口会出现在用户刚关掉程序之后，看起来像程序自己又起来了一次。仍要放在 Dispose 看门狗
            // 之前：看门狗会在 5 秒后直接结束进程，排在它之后就会与之赛跑。
            // The installer is only started on the exit boundary when the user explicitly clicked "restart and install
            // now".
            //
            // Automatic updates no longer happen on exit: they run *before the next start* (see
            // TryLaunchPendingInstallOnStartup), because otherwise the installer window appears right after the user
            // closed the application and looks like the application starting itself again. It still has to happen
            // before the disposal watchdog, which ends the process after five seconds and would otherwise win the race.
            try
            {
                updateService.TryLaunchPendingInstallOnExit();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[App] Update install hand-off failed: {exception}");
            }

            // 安装程序启动前，交接路径已经释放了协调互斥体；这里只需保证其余情形下它也随进程一起消失。
            // The hand-off already released the coordination mutex before starting the installer; this only makes sure
            // it disappears with the process in every other case.
            installCoordinatorMutex.Dispose();

            // A media-session/native component can occasionally block while disposing
            // after Explorer or a tray Popup has already been torn down. Keep a bounded
            // watchdog so an explicit user exit can never leave AFMediaBar alive forever.
            var disposalCompleted = 0;
            _ = Task.Run(async () =>
            {
                await Task.Delay(HostShutdownTimeout).ConfigureAwait(false);
                if (Volatile.Read(ref disposalCompleted) == 0)
                {
                    Environment.Exit(e.ApplicationExitCode);
                }
            });

            try
            {
                _host.Dispose();
            }
            catch (Exception exception)
            {
                // Cleanup must not cancel the final process-termination step.
                Debug.WriteLine($"[App] Host disposal failed: {exception}");
            }
            finally
            {
                Volatile.Write(ref disposalCompleted, 1);
            }

            // WPF has completed its Exit event, but third-party native media components
            // may own non-background threads. Explicitly terminate after all synchronous
            // cleanup has completed so the process cannot remain in the background.
            Environment.Exit(e.ApplicationExitCode);
        }

        private void UpdateAppearanceResources(AppearanceSettings appearance, ApplicationTheme theme, AccentPalette accent)
        {
            var fontFamily = new FontFamily(appearance.ResolveFontFamilySource(SystemFonts.MessageFontFamily.Source));
            var fontWeight = FontWeight.FromOpenTypeWeight(appearance.FontWeight);
            Resources["AppTextFontFamily"] = fontFamily;
            Resources["ContentControlThemeFontFamily"] = fontFamily;
            Resources["AppTextFontWeight"] = fontWeight;
            Resources["AppTextMediumFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 100, 100, 999));
            Resources["AppTextStrongFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 200, 100, 999));

            // 强调色只在这里发布一次：所有界面（任务栏媒体栏、菜单、完整层、设置页与图示）都引用这几个应用级画刷，
            // 因此系统强调色变化后不需要逐个界面刷新，也不会再出现"设置页是粉色、媒体栏是默认蓝"的分裂。
            // The accent is published exactly once, here: every surface (taskbar bar, menus, full panel, settings pages, and
            // diagrams) references these application-level brushes, so an accent change needs no per-surface refresh and the
            // settings-pink / media-blue split cannot come back.
            Resources["AfAccentBrush"] = CreateFrozenBrush(accent.Accent);
            Resources["AfAccentHoverBrush"] = CreateFrozenBrush(accent.Hover);
            Resources["AfAccentPressedBrush"] = CreateFrozenBrush(accent.Pressed);
            Resources["AfAccentTintBrush"] = CreateFrozenBrush(accent.Tint);
            Resources["AfOnAccentBrush"] = CreateFrozenBrush(accent.OnAccent);

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

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 应用未处理异常事件：捕获全局异常以防止应用崩溃。
        /// Application unhandled exception event: catches global exceptions to prevent crashes.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 未处理异常是上报的第一现场：先落盘（含堆栈），再交回 WPF 的默认处理，避免"程序崩了但什么都没有留下"。
            // An unhandled exception is the first thing a report needs: it is written to disk with its stack first, and only then handed back
            // to WPF's default handling, so a crash never leaves nothing behind.
            AppLogService.Current?.CaptureUnhandled("Dispatcher", e.Exception);
            AppLogService.Current?.Flush(TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// 记录后台线程与未观察任务的异常：它们不会走 Dispatcher，但同样会让程序在半坏状态下运行。
        /// Records background-thread and unobserved-task exceptions: they never reach the dispatcher but still leave the app half broken.
        /// </summary>
        /// <param name="sender">事件来源。/ Event source.</param>
        /// <param name="e">异常参数。/ Exception arguments.</param>
        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppLogService.Current?.CaptureUnhandled("AppDomain", exception);
                AppLogService.Current?.Flush(TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>记录未被观察的任务异常。/ Records an unobserved task exception.</summary>
        /// <param name="sender">事件来源。/ Event source.</param>
        /// <param name="e">异常参数。/ Exception arguments.</param>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogService.Current?.CaptureUnhandled("Task", e.Exception);
            e.SetObserved();
        }

        #endregion
    }
}
