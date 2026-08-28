using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Abstractions;
using AFMediaBar.Composition;
using AFMediaBar.Models;
using AFMediaBar.Services;
using AFMediaBar.Services.Lyrics;
using AFMediaBar.Services.Players;
using AFMediaBar.Services.Win32Api;
using AFMediaBar.Settings;
// System.Windows.Localization（枚举）与本地化帮助类同名，用别名消歧。
using Loc = AFMediaBar.Services.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private ApplicationServiceHost? _serviceHost;
    private ApplicationExceptionCoordinator? _exceptionCoordinator;
#if DEBUG
    // 实时歌词调试状态：仅在歌词行变化时输出，避免 233ms 轮询刷屏。
    // Debug lyric state: prints only when the active line changes, to avoid spam from the 233ms poll.
    private string? _debugLyricsLrc;
    private IReadOnlyList<LrcLine> _debugLyricsLines = [];
    private int _debugLyricsLastIndex = -1;
#endif

    internal SystemThemeService? ThemeService => _serviceHost?.ThemeService;
    internal SettingsCoordinator SettingsCoordinator { get; private set; } = null!;
    private bool _shutdownRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        _exceptionCoordinator = new ApplicationExceptionCoordinator(
            Dispatcher,
            () => _shutdownRequested,
            reason => (MainWindow as MainWindow)?.RequestEnvironmentRecovery(reason));
        _exceptionCoordinator.Register();
        _singleInstanceMutex = new Mutex(true, "AFMediaBar.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _shutdownRequested = true;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        try
        {
            StartupService.Migrate();
        }
        catch (Exception exception)
        {
            // A locked Run key must not prevent the application from starting.
            DiagnosticsLogService.Write("startup-registration-migration", exception);
        }

        _serviceHost = new ApplicationServiceHost(
            this,
            () => _shutdownRequested,
            window =>
            {
#if DEBUG
                window.MediaSessionService.SnapshotChanged += DebugOutputLyrics;
#endif
            },
            window =>
            {
#if DEBUG
                window.MediaSessionService.SnapshotChanged -= DebugOutputLyrics;
#endif
            });
        SettingsCoordinator = _serviceHost.SettingsCoordinator;
        _serviceHost.ResourceCoordinator.Initialize();
        _serviceHost.MainWindowCoordinator.Show();
        _serviceHost.StartupUpdateCoordinator.Start();
    }

    internal void RequestShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _serviceHost?.MainWindowCoordinator.InvalidateRecovery();
        _serviceHost?.CancelShutdown();
        Shutdown();
    }

    internal void RecreateMainWindow()
    {
        _serviceHost?.MainWindowCoordinator.Recreate();
    }

    internal void ShowSettingsWindow()
    {
        if (_shutdownRequested || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _serviceHost?.SettingsWindowCoordinator.Show();
    }

    internal void RequestMediaReconnect()
    {
        if (MainWindow is MainWindow window)
        {
            window.RequestMediaReconnect();
        }
    }

#if DEBUG
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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _shutdownRequested = true;
        _serviceHost?.MainWindowCoordinator.InvalidateRecovery();
        _serviceHost?.CancelShutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdownRequested = true;
        _serviceHost?.CancelShutdown();
        _serviceHost?.Dispose();
        _serviceHost = null;
        _singleInstanceMutex?.Dispose();
        _exceptionCoordinator?.Dispose();
        _exceptionCoordinator = null;
        base.OnExit(e);
    }
}
