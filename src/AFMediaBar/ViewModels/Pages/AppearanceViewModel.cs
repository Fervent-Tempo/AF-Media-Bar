using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 外观页 ViewModel：管理字体、播放器文字、应用主题和窗口材质设置。
/// Appearance-page ViewModel: manages font, player text, application theme, and window material settings.
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private LatinFontPreset _latinFont;
    private CjkFontPreset _cjkFont;
    private string _latinFontFamily;
    private string _cjkFontFamily;
    private IReadOnlyList<FontFamilyChoice> _latinFontChoices;
    private IReadOnlyList<FontFamilyChoice> _cjkFontChoices;
    private int _fontWeight;
    private PlayerForegroundMode _playerForegroundMode;
    private ApplicationThemeMode _applicationThemeMode;
    private ApplicationBackdropMode _backdropMode;
    private AccentColorMode _accentColorMode;
    private string _accentColorHex;
    private int _backdropTintOpacityPercent;
    private bool _isRefreshing;

    /// <summary>
    /// 创建外观页视图模型，并订阅设置变更与界面语言变化。
    ///
    /// 下拉框的选项名由 XAML 的动态资源提供，页面自己就会换字；本视图模型产出的动效读数是代码拼出来的文案，因此必须
    /// 订阅语言变化并让 WPF 重读全部绑定。视图模型是单例，两个订阅都与进程同寿命，不需要退订。
    /// Creates the appearance view model and subscribes to settings changes and interface-language changes.
    ///
    /// The drop-down option names come from XAML dynamic resources and follow a language change on their own, while the motion
    /// reading this view model produces is text built in code, so it has to subscribe and make WPF re-read every binding. The
    /// view model is a singleton, so both subscriptions live as long as the process and no unsubscription is needed.
    /// </summary>
    /// <param name="localization">界面语言服务：本页在它变化后刷新自己产出的文案。/ The interface-language service, whose change this page follows to refresh its own text.</param>
    public AppearanceViewModel(LocalizationService localization)
    {
        _localization = localization;

        var appearance = SettingsManager.Current.Appearance.Normalize();
        _latinFontChoices = InstalledFontCatalog.GetChoices("Appearance.LatinFont.FollowSystem", cjk: false);
        _cjkFontChoices = InstalledFontCatalog.GetChoices("Appearance.CjkFont.FollowSystem", cjk: true);
        _latinFont = appearance.LatinFont;
        _cjkFont = appearance.CjkFont;
        _latinFontFamily = InstalledFontCatalog.MatchSelection(appearance.SelectedLatinFontFamily, _latinFontChoices);
        _cjkFontFamily = InstalledFontCatalog.MatchSelection(appearance.SelectedCjkFontFamily, _cjkFontChoices);
        _fontWeight = appearance.FontWeight;
        _playerForegroundMode = appearance.PlayerForegroundMode;
        _applicationThemeMode = appearance.ApplicationThemeMode;
        _backdropMode = appearance.BackdropMode;
        _accentColorMode = appearance.AccentColorMode;
        _accentColorHex = appearance.AccentColor;
        _backdropTintOpacityPercent = appearance.ResolveBackdropTintOpacityPercent();
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public LatinFontPreset LatinFont
    {
        get => _latinFont;
        set
        {
            if (SetProperty(ref _latinFont, value))
            {
                if (!_isRefreshing) Publish();
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
                if (!_isRefreshing) Publish();
            }
        }
    }

    public IReadOnlyList<FontFamilyChoice> LatinFontChoices => _latinFontChoices;
    public IReadOnlyList<FontFamilyChoice> CjkFontChoices => _cjkFontChoices;

    public string LatinFontFamily
    {
        get => _latinFontFamily;
        set
        {
            if (value is not null && SetProperty(ref _latinFontFamily, value) && !_isRefreshing) Publish();
        }
    }

    public string CjkFontFamily
    {
        get => _cjkFontFamily;
        set
        {
            if (value is not null && SetProperty(ref _cjkFontFamily, value) && !_isRefreshing) Publish();
        }
    }

    public void RefreshInstalledFonts()
    {
        var latin = LatinFontFamily;
        var cjk = CjkFontFamily;
        _isRefreshing = true;
        try
        {
            _latinFontChoices = InstalledFontCatalog.GetChoices("Appearance.LatinFont.FollowSystem", cjk: false);
            _cjkFontChoices = InstalledFontCatalog.GetChoices("Appearance.CjkFont.FollowSystem", cjk: true);
            OnPropertyChanged(nameof(LatinFontChoices));
            OnPropertyChanged(nameof(CjkFontChoices));
            LatinFontFamily = InstalledFontCatalog.MatchSelection(latin, _latinFontChoices);
            CjkFontFamily = InstalledFontCatalog.MatchSelection(cjk, _cjkFontChoices);
            OnPropertyChanged(nameof(LatinFontFamily));
            OnPropertyChanged(nameof(CjkFontFamily));
        }
        finally { _isRefreshing = false; }
    }

    public int FontWeight
    {
        get => _fontWeight;
        set
        {
            value = Math.Clamp(value, AppearanceSettings.MinimumFontWeight, AppearanceSettings.MaximumFontWeight);
            if (SetProperty(ref _fontWeight, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>
    /// 播放器文字颜色模式。写入即发布设置，因此下拉框可以直接双向绑定；
    /// 原有的 <c>SetPlayerForegroundModeCommand</c> 仍走同一条写入路径，两条入口行为一致。
    /// Player text colour mode. Writing publishes the setting, so a select can bind two-way; the existing
    /// <c>SetPlayerForegroundModeCommand</c> still runs through the same write path, so both entries behave alike.
    /// </summary>
    public PlayerForegroundMode PlayerForegroundMode
    {
        get => _playerForegroundMode;
        set
        {
            if (SetProperty(ref _playerForegroundMode, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>应用主题模式。写入即发布设置，供下拉框双向绑定。/ Application theme mode. Writing publishes the setting for a two-way select.</summary>
    public ApplicationThemeMode ApplicationThemeMode
    {
        get => _applicationThemeMode;
        set
        {
            if (SetProperty(ref _applicationThemeMode, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>窗口背景材质。写入即发布设置，供下拉框双向绑定。/ Window backdrop material. Writing publishes the setting for a two-way select.</summary>
    public ApplicationBackdropMode BackdropMode
    {
        get => _backdropMode;
        set
        {
            if (SetProperty(ref _backdropMode, value))
            {
                Publish();
            }
        }
    }

    /// <summary>强调色来源：跟随系统或使用自选色。写入即发布设置。/ Accent source: follow the system or use a custom colour. Writing publishes the setting.</summary>
    public AccentColorMode AccentColorMode
    {
        get => _accentColorMode;
        set
        {
            if (SetProperty(ref _accentColorMode, value))
            {
                OnPropertyChanged(nameof(IsCustomAccent));
                Publish();
            }
        }
    }

    /// <summary>是否正在使用自选强调色，供色板与十六进制输入框决定显隐。/ Whether a custom accent is in use, which the swatch strip and the hexadecimal box follow.</summary>
    public bool IsCustomAccent => _accentColorMode == AccentColorMode.Custom;

    /// <summary>
    /// 自选强调色的十六进制文本。
    ///
    /// 文本随时可以处于"打了一半"的状态，因此这里保留原文而只在能解析时发布设置：
    /// 每敲一个字符就写一次设置会让中途的非法值（例如 <c>#12</c>）把强调色刷掉，界面随即闪回默认色。
    /// Hexadecimal text of the custom accent.
    ///
    /// The text can be half-typed at any moment, so the raw text is kept and the setting is published only when it parses:
    /// writing on every keystroke would let an intermediate invalid value such as <c>#12</c> wipe the accent, and the interface
    /// would flash back to the default colour.
    /// </summary>
    public string AccentColorHex
    {
        get => _accentColorHex;
        set
        {
            if (!SetProperty(ref _accentColorHex, value ?? string.Empty))
                return;

            if (_isRefreshing || !ColorHex.TryParse(_accentColorHex, out _))
                return;

            Publish();
        }
    }

    /// <summary>材质底色浓度（0–100）。写入即发布设置，供滑杆双向绑定。/ Material tint concentration (0-100). Writing publishes the setting for a two-way slider.</summary>
    public int BackdropTintOpacityPercent
    {
        get => _backdropTintOpacityPercent;
        set
        {
            value = Math.Clamp(
                value,
                AppearanceSettings.MinimumBackdropTintOpacityPercent,
                AppearanceSettings.MaximumBackdropTintOpacityPercent);
            if (SetProperty(ref _backdropTintOpacityPercent, value))
            {
                Publish();
            }
        }
    }

    /// <summary>
    /// 静置层媒体文字（标题、歌手、歌词）的字号缩放百分比。该值存在任务栏体验设置里，但按界面归属由本页承载：
    /// 「恢复本页默认设置」因此会连同它一起复位。
    /// Font-size scale percentage for the rest-layer media text (title, artist, lyrics). The value lives in the taskbar
    /// experience settings but this page owns it in the interface, so "restore this page's defaults" resets it as well.
    /// </summary>
    public int MediaFontSizePercent
    {
        get => SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        set
        {
            if (_isRefreshing || value == MediaFontSizePercent)
                return;

            SettingsManager.SetTaskbarExperienceSettings(
                SettingsManager.Current.TaskbarExperience with { MediaFontSizePercent = value });
            OnPropertyChanged();
        }
    }

    /// <summary>当前桌面环境的动效级别。/ Current motion level for the desktop environment.</summary>
    public string MotionModeText => MotionPolicy.ResolveCurrent().Mode switch
    {
        MotionMode.Full => Translations.Get("Appearance.Motion.Mode.Full"),
        MotionMode.Reduced => Translations.Get("Appearance.Motion.Mode.Reduced"),
        _ => Translations.Get("Appearance.Motion.Mode.Instant")
    };

    /// <summary>当前动效策略的简短说明。/ Short explanation of the current motion policy.</summary>
    public string MotionDetailText => MotionPolicy.ResolveCurrent().Mode switch
    {
        MotionMode.Full => Translations.Get("Appearance.Motion.Detail.Full"),
        MotionMode.Reduced => Translations.Get("Appearance.Motion.Detail.Reduced"),
        _ => Translations.Get("Appearance.Motion.Detail.Instant")
    };

    /// <summary>界面语言变化后让 WPF 重读全部绑定，本页由代码产出的读数因此一起换语言。/ Makes WPF re-read every binding after a language change, so the readings this page builds in code change language with it.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshInstalledFonts();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void SetPlayerForegroundMode(PlayerForegroundMode mode) => PlayerForegroundMode = mode;

    [RelayCommand]
    private void SetApplicationThemeMode(ApplicationThemeMode mode) => ApplicationThemeMode = mode;

    [RelayCommand]
    private void SetBackdropMode(ApplicationBackdropMode mode) => BackdropMode = mode;

    /// <summary>
    /// 从色板取一个强调色：写入文本并切到"自定义"，因此点一下色块就等于"用这个颜色"。
    /// Picks an accent from the swatch strip: it writes the text and switches to "custom", so tapping a swatch means "use this".
    /// </summary>
    /// <param name="hex">色板上的十六进制颜色文本。/ Hexadecimal colour text of the swatch.</param>
    [RelayCommand]
    private void SetAccentColor(string? hex)
    {
        if (!ColorHex.TryParse(hex, out var color))
            return;

        AccentColorMode = AccentColorMode.Custom;
        AccentColorHex = ColorHex.Format(color);
    }

    private void Publish() => SettingsManager.SetAppearanceSettings(new AppearanceSettings(
        LatinFont,
        CjkFont,
        FontWeight,
        PlayerForegroundMode,
        false,
        ApplicationThemeMode,
        BackdropMode,
        AccentColorMode,
        AccentColorHex,
        BackdropTintOpacityPercent,
        LatinFontFamily,
        CjkFontFamily));

    public void ResetAppearance() => SettingsManager.ResetAppearance();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 媒体文字大小存在任务栏体验设置里，因此它的外部变化（例如显示模式页的重置）也要回写本页读数。
        // The media text size lives in the taskbar experience settings, so an external change to it (the display-mode page's
        // reset, for example) must be reflected in this page's reading too.
        if (e.ResetScope is not (SettingsResetScope.Appearance or SettingsResetScope.All) &&
            e.PropertyName != nameof(AppSettings.TaskbarExperience))
            return;

        var appearance = SettingsManager.Current.Appearance;
        _isRefreshing = true;
        try
        {
            LatinFont = appearance.LatinFont;
            CjkFont = appearance.CjkFont;
            LatinFontFamily = InstalledFontCatalog.MatchSelection(appearance.SelectedLatinFontFamily, _latinFontChoices);
            CjkFontFamily = InstalledFontCatalog.MatchSelection(appearance.SelectedCjkFontFamily, _cjkFontChoices);
            FontWeight = appearance.FontWeight;
            PlayerForegroundMode = appearance.PlayerForegroundMode;
            ApplicationThemeMode = appearance.ApplicationThemeMode;
            BackdropMode = appearance.BackdropMode;
            AccentColorMode = appearance.AccentColorMode;
            AccentColorHex = appearance.AccentColor;
            BackdropTintOpacityPercent = appearance.ResolveBackdropTintOpacityPercent();
            MediaFontSizePercent = SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        }
        finally { _isRefreshing = false; }
    }
}
