using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Components.BuiltIn.Media;

namespace AFMediaBar.Components.Wpf.BuiltIn.MediaText;

public partial class MediaTextViewModel : ComponentViewModelBase
{
    public MediaTextViewModel(string instanceId, MediaTextSettings settings) : base(instanceId) => Settings = settings;
    public MediaTextSettings Settings { get; }

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string artist = string.Empty;
    [ObservableProperty] private bool isVertical;
    [ObservableProperty] private string? marqueeText;

    public bool IsCombined => Settings.TextKind == MediaTextContentKind.TitleAndArtist;
    public string Text => Settings.TextKind == MediaTextContentKind.Artist ? Artist : Title;
    public string DisplayText => MarqueeText ?? Text;
    public double WidthDip => IsVertical ? 68 : IsCombined ? 150 : 210;
    public double FontSizeDip => Math.Clamp(Settings.FontSizeDip, 6, 72);

    public double ArtistFontSizeDip => Math.Max(6, FontSizeDip - 3);

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(DisplayText));
    }
    partial void OnArtistChanged(string value)
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(DisplayText));
    }
    partial void OnMarqueeTextChanged(string? value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnIsVerticalChanged(bool value) => OnPropertyChanged(nameof(WidthDip));
}
