using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.Audio;

namespace AFMediaBar.Components.Wpf.BuiltIn.OutputDevice;

public partial class OutputDeviceViewModel : ComponentViewModelBase
{
    private readonly Action<object?>? _selectionRequested;
    private readonly Action<int, object?>? _wheelRequested;

    public OutputDeviceViewModel(
        string instanceId,
        OutputDeviceSettings settings,
        Action<object?>? selectionRequested = null,
        Action<int, object?>? wheelRequested = null) : base(instanceId)
    {
        Settings = settings;
        _selectionRequested = selectionRequested;
        _wheelRequested = wheelRequested;
    }

    public OutputDeviceSettings Settings { get; }
    public double ButtonSizeDip => Math.Clamp(Settings.ButtonSizeDip, 20, 96);
    [ObservableProperty] private string deviceName = string.Empty;

    [RelayCommand]
    private void Select(object? anchor) => _selectionRequested?.Invoke(anchor);

    public void RequestWheel(int delta, object? anchor) => _wheelRequested?.Invoke(delta, anchor);
}
