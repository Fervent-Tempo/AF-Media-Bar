// 仅向开发者宿主暴露快照和输入意图，仍由现有歌词桥执行生命周期与预算。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Resources;
using Microsoft.Web.WebView2.Core;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    internal DeveloperLyricsHostState CaptureDeveloperLyricsState(string hostId, string monitor) => new(
        hostId, monitor, _lyricsHostLoaded, _lyricsWebRenderer is not null, _lyricsWebRenderer?.IsReady == true,
        _webLyricsGraphicsDisabled || WebLyricsGraphicsRecovery.Session.IsDisabled, _lyricsRendererGeneration,
        _lyricsGraphicsRecoveries, _lyricsCreationFailures, _lyricsRetryTimer.IsEnabled,
        _lyricsWebRenderer?.RuntimeVersion ?? "unknown");

    internal DeveloperActionResult ExecuteDeveloperLyricsAction(string command)
    {
        Dispatcher.VerifyAccess();
        if (!_lyricsHostLoaded || Dispatcher.HasShutdownStarted || _lyricsWebRenderer is null || _webLyricsGraphicsDisabled ||
            WebLyricsGraphicsRecovery.Session.IsDisabled)
            return new(DeveloperActionStatus.Unavailable, Translations.Get("Developer.NoRenderer"));
        AppLogService.Current?.Info("Developer", $"origin=DeveloperInjected, action={command}, generation={_lyricsRendererGeneration}");
        if (command == "webview disable")
            return new(WebLyricsGraphicsRecovery.Session.Request(WebLyricsRecoveryAction.DisableForSession)
                ? DeveloperActionStatus.Queued : DeveloperActionStatus.Unavailable);
        if (command == "webview rebuild")
            return new(OnWebLyricsGraphicsRecoveryRequested(WebLyricsRecoveryAction.Rebuild)
                ? DeveloperActionStatus.Queued : DeveloperActionStatus.Unavailable);
        var kind = command switch
        {
            "webview fail renderer" => CoreWebView2ProcessFailedKind.RenderProcessExited,
            "webview fail unresponsive" => CoreWebView2ProcessFailedKind.RenderProcessUnresponsive,
            "webview fail gpu" => CoreWebView2ProcessFailedKind.GpuProcessExited,
            _ => (CoreWebView2ProcessFailedKind?)null
        };
        if (kind is null) return new(DeveloperActionStatus.Unavailable);
        if (!_lyricsWebRenderer.CanInjectDeveloperFailure(kind.Value))
            return new(DeveloperActionStatus.Unavailable, Translations.Get("Developer.DeliveryPaused"));
        _lyricsWebRenderer.HandleProcessFailure(kind.Value,
            kind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive ? CoreWebView2ProcessFailedReason.Unresponsive : CoreWebView2ProcessFailedReason.Unexpected,
            0, developerInjected: true);
        return new(kind == CoreWebView2ProcessFailedKind.GpuProcessExited ? DeveloperActionStatus.Completed : DeveloperActionStatus.Accepted,
            Translations.Get(kind == CoreWebView2ProcessFailedKind.GpuProcessExited ? "Developer.GpuIgnored" : "Developer.FailureInjected"));
    }
}
