// 平台宿主的开发者输入与快照；独立歌曲预览窗口由宿主关闭，不转发到媒体服务。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using AFMediaBar.Resources;

namespace AFMediaBar.Views.Windows;

public partial class MainWindow : IDeveloperHostActions
{
    private TrackChangeNotificationWindow? _developerTrackPreview;

    IReadOnlyList<DeveloperLyricsHostState> IDeveloperHostActions.GetLyricsHosts() => _isClosing ? [] :
        _taskbarWindows.Select(window => window.CaptureDeveloperLyricsState()).ToArray();

    DeveloperActionResult IDeveloperHostActions.ExecuteLyricsAction(string command, string? hostId)
    {
        Dispatcher.VerifyAccess();
        if (_isClosing || TaskbarEnvironmentRecovering) return new(DeveloperActionStatus.Unavailable);
        var window = _taskbarWindows.FirstOrDefault(window => window.DeveloperHostId == hostId);
        return window?.ExecuteDeveloperLyricsAction(command) ?? new(DeveloperActionStatus.Unavailable, Translations.Get("Developer.NoRenderer"));
    }

    DeveloperActionResult IDeveloperHostActions.ReloadTaskbar()
    {
        if (_isClosing || TaskbarEnvironmentRecovering) return new(DeveloperActionStatus.Busy);
        RequestTaskbarHostReload();
        return new(DeveloperActionStatus.Queued);
    }

    DeveloperActionResult IDeveloperHostActions.ShowTrackPreview()
    {
        if (_isClosing) return new(DeveloperActionStatus.Canceled);
        var settings = SettingsManager.Current.TrackChangeNotification.Normalize();
        var monitor = _displayMonitorService.ResolveNotificationMonitor(settings.TargetMode, settings.FixedMonitorDeviceId);
        if (monitor is null) return new(DeveloperActionStatus.Unavailable);
        _developerTrackPreview ??= _trackChangeNotificationFactory();
        var sample = MediaSnapshot.Disconnected with
        {
            IsConnected = true, IsPlaying = true, Title = Translations.Get("Developer.Sample.Title"),
            Artist = Translations.Get("Developer.Sample.Artist"), SourceName = "AF Media Bar"
        };
        _developerTrackPreview.ShowNotification(new(sample, settings, monitor));
        return new(DeveloperActionStatus.Accepted);
    }

    string IDeveloperHostActions.CaptureState()
    {
        Dispatcher.VerifyAccess();
        var header = $"AF {typeof(App).Assembly.GetName().Version}; .NET {Environment.Version}; " +
            $"{Translations.Get("Developer.Build")}: {BuildConfiguration}";
        var monitors = string.Join(Environment.NewLine, _displayMonitorService.GetMonitors().Select(monitor =>
            $"{monitor.DeviceName}: DPI={monitor.DpiX}/{monitor.DpiY}, bounds={monitor.MonitorArea}, primary={monitor.IsPrimary}"));
        var hosts = string.Join(Environment.NewLine, _taskbarWindows.Select(window => window.CaptureDeveloperLyricsState())
            .Select(host => $"{host.Monitor} [{host.HostId}]: loaded={host.Loaded}, renderer={host.HasRenderer}, ready={host.Ready}, " +
                $"disabled={host.Disabled}, generation={host.Generation}, rebuilds={host.Recoveries}/3, failures={host.Failures}, retry={host.RetryPending}, runtime={host.RuntimeVersion}"));
        // 来源显示名只用于诊断；路径形式不复制，避免带出用户目录。
        var source = _mediaSessionService.SelectedSourceName;
        if (source.Contains('\\') || source.Contains('/')) source = Translations.Get("Developer.Redacted");
        return header + Environment.NewLine + Translations.Format("Developer.State.Host", TaskbarEnvironmentRecovering,
            WebLyricsGraphicsRecovery.Session.IsDisabled, WebLyricsGraphicsRecovery.Session.HasDegraded, source) +
            Environment.NewLine + monitors + Environment.NewLine + hosts;
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    void IDeveloperHostActions.ClosePreviews() => CloseDeveloperPreviews();

    private void CloseDeveloperPreviews()
    {
        _developerTrackPreview?.Close();
        _developerTrackPreview = null;
    }
}
