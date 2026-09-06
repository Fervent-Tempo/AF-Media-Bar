using AFMediaBar.Classes.Models;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>音量合成器中的单个应用。 / One application row in the volume mixer.</summary>
public partial class ApplicationVolumeItemViewModel : ObservableObject
{
    private readonly Action<ApplicationVolumeItemViewModel, int> _volumeChanged;
    private bool _isSynchronizing;

    public string ProcessName { get; }
    public string DisplayName { get; }
    public bool IsCurrentMedia { get; }

    [ObservableProperty]
    private int _volumePercent;

    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    public ApplicationVolumeItemViewModel(
        ApplicationVolumeSnapshot snapshot,
        Action<ApplicationVolumeItemViewModel, int> volumeChanged)
    {
        ProcessName = snapshot.ProcessName;
        DisplayName = snapshot.DisplayName;
        IsCurrentMedia = snapshot.IsCurrentMedia;
        _volumePercent = snapshot.VolumePercent;
        _volumeChanged = volumeChanged;
    }

    partial void OnVolumePercentChanged(int value)
    {
        if (!_isSynchronizing)
        {
            _volumeChanged(this, Math.Clamp(value, 0, 100));
        }
    }

    public void SynchronizeVolume(int value)
    {
        _isSynchronizing = true;
        VolumePercent = value;
        _isSynchronizing = false;
    }
}
