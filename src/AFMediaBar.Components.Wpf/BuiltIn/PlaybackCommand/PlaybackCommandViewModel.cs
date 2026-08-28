using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.Playback;

namespace AFMediaBar.Components.Wpf.BuiltIn.PlaybackCommand;

public sealed partial class PlaybackCommandViewModel : ComponentViewModelBase
{
    private readonly Action<PlaybackCommandKind, object?>? _commandRequested;

    public PlaybackCommandViewModel(string instanceId, PlaybackCommandSettings settings, Action<PlaybackCommandKind, object?>? commandRequested = null)
        : base(instanceId)
    {
        Settings = settings;
        _commandRequested = commandRequested;
    }

    public PlaybackCommandSettings Settings { get; }
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string? toolTip;
    public double ButtonSizeDip => Math.Clamp(Settings.ButtonSizeDip, 20, 96);
    public string Glyph => Settings.Command switch
    {
        PlaybackCommandKind.Previous => "\uE892",
        PlaybackCommandKind.Next => "\uE893",
        PlaybackCommandKind.SelectSource => "\uE8F4",
        PlaybackCommandKind.AdjustVolume => "\uE767",
        PlaybackCommandKind.SelectOutputDevice => "\uE7F5",
        PlaybackCommandKind.PlayPause when IsPlaying => "\uE769",
        _ => "\uE768"
    };

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    [RelayCommand]
    private void Invoke(object? anchor) => _commandRequested?.Invoke(Settings.Command, anchor);
}
