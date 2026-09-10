using AFMediaBar.Classes.Models;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>音量合成器中的单个应用。 / One application row in the volume mixer.</summary>
public partial class ApplicationVolumeItemViewModel : ObservableObject
{
    private readonly Action<ApplicationVolumeItemViewModel, int, bool> _volumeChanged;
    private bool _isSynchronizing;
    private bool _applyImmediately;

    public string ProcessName { get; }
    public string DisplayName { get; }
    public bool IsCurrentMedia { get; }
    public byte[]? IconData { get; }

    [ObservableProperty]
    private int _volumePercent;

    /// <summary>
    /// 调用 IsNullOrWhiteSpace，提供 API。
    /// Provides the public IsNullOrWhiteSpace entry point required by this component.
    /// </summary>
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    /// <summary>
    /// 调用 ApplicationVolumeItemViewModel，提供 API。
    /// Provides the public ApplicationVolumeItemViewModel entry point required by this component.
    /// </summary>
    public ApplicationVolumeItemViewModel(
        ApplicationVolumeSnapshot snapshot,
        Action<ApplicationVolumeItemViewModel, int, bool> volumeChanged)
    {
        ProcessName = snapshot.ProcessName;
        DisplayName = snapshot.DisplayName;
        IsCurrentMedia = snapshot.IsCurrentMedia;
        IconData = snapshot.IconData;
        _volumePercent = snapshot.VolumePercent;
        _volumeChanged = volumeChanged;
    }

    partial void OnVolumePercentChanged(int value)
    {
        if (!_isSynchronizing)
        {
            _volumeChanged(this, Math.Clamp(value, 0, 100), _applyImmediately);
        }
    }

    /// <summary>
    /// 调用 SetApplyImmediately，提供 API。
    /// Provides the public SetApplyImmediately entry point required by this component.
    /// </summary>
    public void SetApplyImmediately(bool applyImmediately) => _applyImmediately = applyImmediately;

    /// <summary>
    /// 调用 SynchronizeVolume，提供 API。
    /// Provides the public SynchronizeVolume entry point required by this component.
    /// </summary>
    public void SynchronizeVolume(int value)
    {
        _isSynchronizing = true;
        VolumePercent = value;
        _isSynchronizing = false;
    }

    /// <summary>
    /// 调用 AdjustVolume，提供 API。
    /// Provides the public AdjustVolume entry point required by this component.
    /// </summary>
    public void AdjustVolume(int delta)
    {
        _applyImmediately = false;
        VolumePercent = Math.Clamp(VolumePercent + delta, 0, 100);
    }
}
