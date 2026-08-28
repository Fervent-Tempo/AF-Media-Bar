using System.Diagnostics;
using System.Windows;
using AFMediaBar.Models;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Services;

/// <summary>Performs the non-blocking startup update check and presents its result.</summary>
internal sealed class StartupUpdateCoordinator(
    UpdateService updateService,
    Func<bool> isShutdown,
    Func<bool> hasSettingsWindow,
    Action activateSettingsWindow,
    CancellationToken cancellationToken) : IDisposable
{
    private Version? _notifiedVersion;
    private bool _disposed;

    internal void Start() => _ = CheckAsync();

    private async Task CheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            if (_disposed || isShutdown()) return;
            var result = await updateService.CheckForUpdatesAsync(false, cancellationToken);
            if (result is not { Status: UpdateCheckStatus.UpdateAvailable, Update: { } update } ||
                (!update.Mandatory && updateService.IsVersionSkipped(update.Version)) ||
                _notifiedVersion == update.Version) return;

            _notifiedVersion = update.Version;
            if (hasSettingsWindow())
            {
                activateSettingsWindow();
                return;
            }

            ShowNotification(update);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("automatic-update-check", exception);
        }
    }

    private static void ShowNotification(UpdateInfo update)
    {
        var changelog = update.Changelog.Count == 0
            ? Loc.Get("Msg.UpdateOpenPageHint")
            : string.Join(Environment.NewLine, update.Changelog.Take(5).Select(item => $"• {item}"));
        var result = MessageBox.Show(
            Loc.Get("Msg.UpdateFoundBody", update.VersionText, changelog),
            update.Mandatory ? Loc.Get("Msg.UpdateMajor") : Loc.Get("Msg.UpdateNew"),
            MessageBoxButton.YesNo,
            update.Mandatory ? MessageBoxImage.Warning : MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes) return;

        var uri = UpdateService.GetPreferredDownloadUri(update);
        if (uri is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-update-download", exception, uri.AbsoluteUri);
            MessageBox.Show(exception.Message, Loc.Get("Msg.OpenDownloadFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Dispose() => _disposed = true;
}
