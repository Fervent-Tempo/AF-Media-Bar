using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;
/// <summary>
/// 处理更新检查、下载链接、日志操作、重置和窗口关闭清理。
/// Handles update checks, download links, log actions, reset, and window-close cleanup.
/// </summary>
public partial class SettingsWindow
{
    private CancellationTokenSource? _updateCheckCancellation;
    private UpdateInfo? _displayedRelease;

    private void UpdateCheckSetting_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        _updateService.SetAutomaticChecksEnabled(
            AutomaticUpdateChecksCheckBox.IsChecked == true);
        UpdateStatusText.Text = AutomaticUpdateChecksCheckBox.IsChecked == true
            ? Loc.Get("Settings.Update.AutoCheckEnabled")
            : Loc.Get("Settings.Update.AutoCheckDisabled");
    }

    private async void CheckForUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        // 手动检查使用独立超时，并在窗口关闭时取消，避免异步回调访问已关闭的界面。 / Manual checks use an independent timeout and are canceled on close so callbacks do not touch a closed window.
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _updateCheckCancellation = cancellation;
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = Loc.Get("Settings.Update.Checking");

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(
                force: true,
                cancellation.Token);
            ApplyUpdateCheckResult(result);
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested || IsVisible)
            {
                UpdateStatusText.Text = Loc.Get("Settings.Update.Timeout");
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("manual-update-check", exception);
            if (IsVisible)
            {
                UpdateStatusText.Text = Loc.Get("Settings.Update.FailedFormat", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_updateCheckCancellation, cancellation))
            {
                _updateCheckCancellation = null;
                cancellation.Dispose();
                CheckForUpdatesButton.IsEnabled = true;
            }
        }
    }

    private void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                ShowRelease(update, updateAvailable: true);
                UpdateDetailsPanel.BringIntoView();
                break;
            case UpdateCheckStatus.NoUpdate when result.Update is { } release:
                ShowRelease(release, updateAvailable: false);
                break;
            case UpdateCheckStatus.AlreadyChecking:
                UpdateStatusText.Text = Loc.Get("Settings.Update.InProgress");
                break;
            case UpdateCheckStatus.NotDue:
                UpdateStatusText.Text = Loc.Get("Settings.Update.CheckedToday");
                break;
            default:
                UpdateStatusText.Text = result.ErrorMessage ?? Loc.Get("Settings.Update.Failed");
                break;
        }
    }

    private void UpdateService_OnUpdateAvailable(object? sender, UpdateInfo update)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateService_OnUpdateAvailable(sender, update));
            return;
        }

        ShowRelease(update, updateAvailable: true);
    }

    private void ShowRelease(UpdateInfo release, bool updateAvailable)
    {
        _displayedRelease = release;
        UpdateDetailsPanel.Visibility = Visibility.Visible;
        UpdateTitleText.Text = release.Title;
        var releaseDate = release.ReleaseDate is { } date
            ? $" · {date:yyyy-MM-dd}"
            : string.Empty;
        UpdateVersionText.Text = Loc.Get("Settings.Update.VersionFormat", release.VersionText, releaseDate);
        UpdateChangelogText.Text = release.Changelog.Count == 0
            ? Loc.Get("Settings.Update.NoReleaseNotes")
            : string.Join(
                Environment.NewLine,
                release.Changelog.Select(item => $"• {item}"));

        SetUpdateLink(GitHubDownloadButton, release, "github");
        SetUpdateLink(QuarkDownloadButton, release, "quark");
        SetUpdateLink(BaiduDownloadButton, release, "baidu");
        SetUpdateLink(LanzouDownloadButton, release, "lanzou");
        SetUpdateLink(ReleaseNotesButton, release.ReleaseNotesUri);

        var skipped = updateAvailable &&
            !release.Mandatory &&
            _updateService.IsVersionSkipped(release.Version);
        UpdateStatusText.Text = updateAvailable
            ? skipped
                ? Loc.Get("Settings.Update.IgnoredFormat", release.VersionText)
                : release.Mandatory
                    ? Loc.Get("Settings.Update.MajorAvailable", release.VersionText)
                    : Loc.Get("Settings.Update.NewAvailable", release.VersionText)
            : Loc.Get("Settings.Update.UpToDate", release.VersionText);
        SkipUpdateButton.Visibility = updateAvailable && !release.Mandatory && !skipped
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void SetUpdateLink(Button button, UpdateInfo release, string channel)
    {
        SetUpdateLink(
            button,
            release.DownloadUris.TryGetValue(channel, out var uri) ? uri : null);
    }

    private static void SetUpdateLink(Button button, Uri? uri)
    {
        button.Tag = uri?.AbsoluteUri;
        button.Visibility = uri is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SkipUpdate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_displayedRelease is not { Mandatory: false } release ||
            release.Version <= _updateService.CurrentVersion)
        {
            return;
        }

        _updateService.SkipVersion(release.Version);
        SkipUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = Loc.Get("Settings.Update.IgnoredNowFormat", release.VersionText);
    }

    private void ResetDefaults_OnClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            Loc.Get("Msg.ResetBody"),
            Loc.Get("Msg.ResetTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        TryUpdate(() =>
        {
            _updateService.SetAutomaticChecksEnabled(true);
            _updateService.ClearSkippedVersion();
            _coordinator.ResetAll();
            if (_displayedRelease is { } release)
            {
                ShowRelease(release, release.Version > _updateService.CurrentVersion);
            }
        });
    }

    private void OpenLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-external-link", exception, url);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenLinkFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenLogFile_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = DiagnosticsLogService.EnsureLogFile();
            DiagnosticsLogService.Write("log-file-opened");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-log-file", exception);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenLogFileFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.RequestMediaReconnect();
    }

    private void TryUpdate(Action update)
    {
        try
        {
            update();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("save-settings", exception);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.SaveSettingsFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SyncFromSettings();
        }
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        ClearSkinPreview();
        DisposeLayoutEditorSurfaces();
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        _updateCheckCancellation = null;
        _scaleSaveTimer.Stop();
        _fontSaveTimer.Stop();
        _coordinator.Changed -= Coordinator_OnChanged;
        _updateService.UpdateAvailable -= UpdateService_OnUpdateAvailable;
        if (_systemThemeService is not null)
        {
            _systemThemeService.ThemeApplied -= SystemThemeService_OnThemeApplied;
        }
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        Closed -= SettingsWindow_OnClosed;
    }

}
