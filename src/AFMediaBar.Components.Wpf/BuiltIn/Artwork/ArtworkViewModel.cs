using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.BuiltIn.Media;

namespace AFMediaBar.Components.Wpf.BuiltIn.Artwork;

public partial class ArtworkViewModel : ComponentViewModelBase
{
    private readonly Action<object?>? _sourceRequested;

    public ArtworkViewModel(string instanceId, ArtworkSettings settings, Action<object?>? sourceRequested = null)
        : base(instanceId)
    {
        Settings = settings;
        _sourceRequested = sourceRequested;
    }

    public ArtworkSettings Settings { get; }

    [ObservableProperty]
    private ImageSource? artwork;

    [ObservableProperty]
    private Brush? background;

    public double CornerRadiusDip => Math.Clamp(Settings.CornerRadiusDip, 0, 32);
    public bool HasArtwork => Artwork is not null;

    partial void OnArtworkChanged(ImageSource? value) => OnPropertyChanged(nameof(HasArtwork));

    [RelayCommand]
    private void OpenSource(object? anchor)
    {
        if (Settings.OpenSourceOnClick) _sourceRequested?.Invoke(anchor);
    }
}
