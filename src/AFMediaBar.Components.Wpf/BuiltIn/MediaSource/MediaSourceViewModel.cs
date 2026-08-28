using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.Media;

namespace AFMediaBar.Components.Wpf.BuiltIn.MediaSource;

public partial class MediaSourceViewModel : ComponentViewModelBase
{
    private readonly Action<object?>? _sourceRequested;

    public MediaSourceViewModel(string instanceId, MediaSourceSettings settings, Action<object?>? sourceRequested = null)
        : base(instanceId)
    {
        Settings = settings;
        _sourceRequested = sourceRequested;
    }

    public MediaSourceSettings Settings { get; }
    [ObservableProperty] private string sourceName = string.Empty;
    [ObservableProperty] private bool isVertical;
    public double WidthDip => IsVertical ? 68 : 210;
    public double FontSizeDip => Math.Clamp(Settings.FontSizeDip, 6, 72);

    partial void OnIsVerticalChanged(bool value) => OnPropertyChanged(nameof(WidthDip));

    [RelayCommand]
    private void SelectSource(object? anchor) => _sourceRequested?.Invoke(anchor);
}
