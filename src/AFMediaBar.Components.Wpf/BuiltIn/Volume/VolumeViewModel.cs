using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.Audio;

namespace AFMediaBar.Components.Wpf.BuiltIn.Volume;

public partial class VolumeViewModel : ComponentViewModelBase
{
    private readonly Action<object?>? _popupRequested;
    private readonly Action<int, object?>? _wheelRequested;

    public VolumeViewModel(
        string instanceId,
        VolumeSettings settings,
        Action<object?>? popupRequested = null,
        Action<int, object?>? wheelRequested = null) : base(instanceId)
    {
        Settings = settings;
        _popupRequested = popupRequested;
        _wheelRequested = wheelRequested;
    }

    public VolumeSettings Settings { get; }
    public double ButtonSizeDip => Math.Clamp(Settings.ButtonSizeDip, 20, 96);
    [ObservableProperty] private int volumePercent;
    [ObservableProperty] private bool isAvailable = true;

    [RelayCommand]
    private void OpenPopup(object? anchor) => _popupRequested?.Invoke(anchor);

    public void RequestWheel(int delta, object? anchor) => _wheelRequested?.Invoke(delta, anchor);
}
