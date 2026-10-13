using AFMediaBar.Classes.Models;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>音量合成器中的单个应用。 / One application row in the volume mixer.</summary>
public partial class ApplicationVolumeItemViewModel : ObservableObject
{
    private readonly Action<ApplicationVolumeItemViewModel, int, bool> _volumeChanged;
    private readonly Func<ApplicationVolumeItemViewModel, Task>? _muteChanged;
    private bool _isSynchronizing;
    private bool _applyImmediately;
    private int _confirmedVolume;
    private bool _confirmedMuted;

    public string ProcessName { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    [NotifyPropertyChangedFor(nameof(MuteToolTip))]
    private string _displayName;
    [ObservableProperty] private bool _isCurrentMedia;
    [ObservableProperty] private byte[]? _iconData;

    [ObservableProperty]
    private int _volumePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteToolTip))]
    private bool _isMuted;

    public string MuteToolTip => AFMediaBar.Resources.Translations.Format(IsMuted ? "Audio.Mute.UnmuteHint" : "Audio.Mute.MuteHint", DisplayName);

    [RelayCommand]
    private async Task ToggleMuteAsync()
    { if (_muteChanged is not null) await _muteChanged(this); }

    public void RefreshLabels() => OnPropertyChanged(nameof(MuteToolTip));

    public void Synchronize(ApplicationVolumeSnapshot snapshot, bool updateMetadata = true)
    {
        _confirmedVolume = snapshot.VolumePercent;
        _confirmedMuted = snapshot.IsMuted;
        SynchronizeVolume(snapshot.VolumePercent);
        IsMuted = snapshot.IsMuted;
        if (updateMetadata)
        { DisplayName = snapshot.DisplayName; IsCurrentMedia = snapshot.IsCurrentMedia; IconData = snapshot.IconData; }
        RefreshLabels();
    }

    public void RestoreConfirmedState()
    { SynchronizeVolume(_confirmedVolume); IsMuted = _confirmedMuted; }

    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    public ApplicationVolumeItemViewModel(
        ApplicationVolumeSnapshot snapshot,
        Action<ApplicationVolumeItemViewModel, int, bool> volumeChanged,
        Func<ApplicationVolumeItemViewModel, Task>? muteChanged = null)
    {
        ProcessName = snapshot.ProcessName;
        _displayName = snapshot.DisplayName;
        _isCurrentMedia = snapshot.IsCurrentMedia;
        _iconData = snapshot.IconData;
        _volumePercent = snapshot.VolumePercent;
        _confirmedVolume = snapshot.VolumePercent;
        _confirmedMuted = snapshot.IsMuted;
        _volumeChanged = volumeChanged;
        _muteChanged = muteChanged;
        _isMuted = snapshot.IsMuted;
    }

    partial void OnVolumePercentChanged(int value)
    {
        if (!_isSynchronizing)
        {
            _volumeChanged(this, Math.Clamp(value, 0, 100), _applyImmediately);
        }
    }

    public void SetApplyImmediately(bool applyImmediately) => _applyImmediately = applyImmediately;

    public void SynchronizeVolume(int value)
    {
        _isSynchronizing = true;
        VolumePercent = value;
        _isSynchronizing = false;
    }

    public void AdjustVolume(int delta)
    {
        _applyImmediately = false;
        VolumePercent = Math.Clamp(VolumePercent + delta, 0, 100);
    }
}
