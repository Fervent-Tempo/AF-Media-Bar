using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Resources;
using System.Collections.ObjectModel;
using System.Diagnostics;
using static Lyricify.Lyrics.Providers.Web.Musixmatch.GetTokenResponse;

namespace AFMediaBar.ViewModels.Components;

public partial class AFContextMenuViewModel : ObservableObject
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly UpdateService _updateService;

    [ObservableProperty] private string _updateMenuHeader =
        Translations.Get("Update.Tray.Check");

    [ObservableProperty] private bool _isUpdateMenuEnabled = true;

    [ObservableProperty] private bool _isReloadTaskbarHostEnabled = true;

    [ObservableProperty] private ObservableCollection<MediaSessionOption> _sessions = [];

    public AFContextMenuViewModel(
        MediaSessionService mediaSessionService,
        UpdateService updateService)
    {
        _mediaSessionService = mediaSessionService;
        _updateService = updateService;
    }

    [RelayCommand]
    private void UpdateMenu()
    {
        ExecuteUpdateAction();
    }

    [RelayCommand]
    private void SelectMediaSession(string? key)
    {
        _mediaSessionService.SelectSession(key ?? string.Empty);
    }

    [RelayCommand]
    private async Task ReconnectMediaSession()
    {
        await _mediaSessionService.ReconnectAsync();
    }



    private void ExecuteUpdateAction()
    {
        switch (UpdatePresentationPolicy.ResolveTrayAction(
                    _updateService.CurrentState))
        {
            case UpdateTrayAction.Check:
                _ = CheckForUpdatesAsync();
                break;

            case UpdateTrayAction.Cancel:
                _updateService.CancelDownload();
                break;

            case UpdateTrayAction.InstallAndRestart:
                _updateService.RequestInstallAndExit();
                break;

            case UpdateTrayAction.OpenUpdatePage:
                // Navigation
                break;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await _updateService.CheckAsync(manual: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[Update] Manual check failed: {exception}");
        }
    }
}