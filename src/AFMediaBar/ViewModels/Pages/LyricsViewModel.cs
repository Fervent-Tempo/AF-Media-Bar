using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>歌词呈现与对齐设置。 / Lyric presentation and alignment settings.</summary>
public partial class LyricsViewModel : ObservableObject
{
    public bool LyricsEnabled { get => SettingsManager.Current.LyricsEnabled; set { SettingsManager.SetLyricsEnabled(value); RaiseAll(); } }
    public bool TwoLineLyricsEnabled { get => SettingsManager.Current.TwoLineLyricsEnabled; set { SettingsManager.SetTwoLineLyricsEnabled(value); RaiseAll(); } }
    public LyricsSecondaryLineMode SecondaryLineMode { get => SettingsManager.Current.LyricsSecondaryLineMode; set { SettingsManager.SetLyricsSecondaryLineMode(value); OnPropertyChanged(); } }
    public LyricsTextAlignment TextAlignment { get => SettingsManager.Current.LyricsTextAlignment; set { SettingsManager.SetLyricsTextAlignment(value); OnPropertyChanged(); } }
    public bool CanConfigureTwoLine => LyricsEnabled;
    public bool CanConfigureSecondary => LyricsEnabled && TwoLineLyricsEnabled;

    public LyricsViewModel() => SettingsManager.SettingsChanged += OnSettingsChanged;
    public void ResetLyrics() => SettingsManager.ResetLyrics();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.Lyrics or SettingsResetScope.All) RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(LyricsEnabled)); OnPropertyChanged(nameof(TwoLineLyricsEnabled));
        OnPropertyChanged(nameof(SecondaryLineMode)); OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(CanConfigureTwoLine)); OnPropertyChanged(nameof(CanConfigureSecondary));
    }
}
