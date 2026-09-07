using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 外观页 ViewModel：管理字体、播放器文字、应用主题和窗口材质设置。
/// Appearance-page ViewModel: manages font, player text, application theme, and window material settings.
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
    private LatinFontPreset _latinFont;
    private CjkFontPreset _cjkFont;
    private int _fontWeight;
    private PlayerForegroundMode _playerForegroundMode;
    private bool _enhancedReadability;
    private ApplicationThemeMode _applicationThemeMode;
    private ApplicationBackdropMode _backdropMode;

    public AppearanceViewModel()
    {
        var appearance = SettingsManager.Current.Appearance.Normalize();
        _latinFont = appearance.LatinFont;
        _cjkFont = appearance.CjkFont;
        _fontWeight = appearance.FontWeight;
        _playerForegroundMode = appearance.PlayerForegroundMode;
        _enhancedReadability = appearance.EnhancedReadability;
        _applicationThemeMode = appearance.ApplicationThemeMode;
        _backdropMode = appearance.BackdropMode;
    }

    public LatinFontPreset LatinFont
    {
        get => _latinFont;
        set
        {
            if (SetProperty(ref _latinFont, value))
            {
                Publish();
            }
        }
    }

    public CjkFontPreset CjkFont
    {
        get => _cjkFont;
        set
        {
            if (SetProperty(ref _cjkFont, value))
            {
                Publish();
            }
        }
    }

    public int FontWeight
    {
        get => _fontWeight;
        set
        {
            value = Math.Clamp(value, AppearanceSettings.MinimumFontWeight, AppearanceSettings.MaximumFontWeight);
            if (SetProperty(ref _fontWeight, value))
            {
                Publish();
            }
        }
    }

    public PlayerForegroundMode PlayerForegroundMode
    {
        get => _playerForegroundMode;
        private set
        {
            if (SetProperty(ref _playerForegroundMode, value))
            {
                Publish();
            }
        }
    }

    public bool EnhancedReadability
    {
        get => _enhancedReadability;
        set
        {
            if (SetProperty(ref _enhancedReadability, value))
            {
                Publish();
            }
        }
    }

    public ApplicationThemeMode ApplicationThemeMode
    {
        get => _applicationThemeMode;
        private set
        {
            if (SetProperty(ref _applicationThemeMode, value))
            {
                Publish();
            }
        }
    }

    public ApplicationBackdropMode BackdropMode
    {
        get => _backdropMode;
        private set
        {
            if (SetProperty(ref _backdropMode, value))
            {
                Publish();
            }
        }
    }

    [RelayCommand]
    private void SetPlayerForegroundMode(PlayerForegroundMode mode) => PlayerForegroundMode = mode;

    [RelayCommand]
    private void SetApplicationThemeMode(ApplicationThemeMode mode) => ApplicationThemeMode = mode;

    [RelayCommand]
    private void SetBackdropMode(ApplicationBackdropMode mode) => BackdropMode = mode;

    private void Publish() => SettingsManager.SetAppearanceSettings(new AppearanceSettings(
        LatinFont,
        CjkFont,
        FontWeight,
        PlayerForegroundMode,
        EnhancedReadability,
        ApplicationThemeMode,
        BackdropMode));
}
