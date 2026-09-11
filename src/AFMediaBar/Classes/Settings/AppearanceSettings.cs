namespace AFMediaBar.Classes.Settings;

/// <summary>播放器文字颜色模式。 / Player text color mode.</summary>
public enum PlayerForegroundMode
{
    Automatic = 0,
    LightText = 1,
    DarkText = 2
}

/// <summary>应用主题模式。 / Application theme mode.</summary>
public enum ApplicationThemeMode
{
    Automatic = 0,
    Light = 1,
    Dark = 2
}

/// <summary>应用窗口和菜单使用的背景材质。 / Background material used by application windows and menus.</summary>
public enum ApplicationBackdropMode
{
    FluentSolid = 0,
    Mica = 1,
    Acrylic = 2
}

/// <summary>西文字体预设。 / Latin font preset.</summary>
public enum LatinFontPreset
{
    SegoeUi = 0,
    Arial = 1,
    Calibri = 2,
    Verdana = 3,
    Consolas = 4,
    TimesNewRoman = 5
}

/// <summary>中文字体预设。 / CJK font preset.</summary>
public enum CjkFontPreset
{
    SystemDefault = 0,
    MicrosoftYaHei = 1,
    DengXian = 2,
    SimSun = 3,
    SimHei = 4,
    KaiTi = 5,
    FangSong = 6
}

/// <summary>
/// 集中保存外观页设置，并生成稳定的 WPF 字体回退链。
/// Stores appearance-page settings and builds a stable WPF font fallback chain.
/// </summary>
public readonly record struct AppearanceSettings(
    LatinFontPreset LatinFont,
    CjkFontPreset CjkFont,
    int FontWeight,
    PlayerForegroundMode PlayerForegroundMode,
    bool EnhancedReadability,
    ApplicationThemeMode ApplicationThemeMode,
    ApplicationBackdropMode BackdropMode)
{
    public const int MinimumFontWeight = 300;
    public const int MaximumFontWeight = 900;

    /// <summary>
    /// 调用 new，提供 API。
    /// Provides the public new entry point required by this component.
    /// </summary>
    public static AppearanceSettings Default { get; } = new(
        LatinFontPreset.SegoeUi,
        CjkFontPreset.SystemDefault,
        400,
        PlayerForegroundMode.Automatic,
        false,
        ApplicationThemeMode.Automatic,
        ApplicationBackdropMode.Mica);

    /// <summary>
    /// 调用 Normalize，提供 API。
    /// Provides the public Normalize entry point required by this component.
    /// </summary>
    public AppearanceSettings Normalize()
    {
        var defaults = Default;
        var normalized = this with
        {
            LatinFont = Enum.IsDefined(LatinFont) ? LatinFont : defaults.LatinFont,
            CjkFont = Enum.IsDefined(CjkFont) ? CjkFont : defaults.CjkFont,
            PlayerForegroundMode = Enum.IsDefined(PlayerForegroundMode) ? PlayerForegroundMode : defaults.PlayerForegroundMode,
            ApplicationThemeMode = Enum.IsDefined(ApplicationThemeMode) ? ApplicationThemeMode : defaults.ApplicationThemeMode,
            BackdropMode = Enum.IsDefined(BackdropMode) ? BackdropMode : defaults.BackdropMode,
            FontWeight = Math.Clamp(FontWeight, MinimumFontWeight, MaximumFontWeight)
        };
        return normalized;
    }

    /// <summary>生成西文优先、中文和东亚字符回退在后的字体链。 / Builds a Latin-first fallback chain with CJK coverage.</summary>
    public string ResolveFontFamilySource(string systemFontFamily)
    {
        var latin = LatinFont switch
        {
            LatinFontPreset.Arial => "Arial",
            LatinFontPreset.Calibri => "Calibri",
            LatinFontPreset.Verdana => "Verdana",
            LatinFontPreset.Consolas => "Consolas",
            LatinFontPreset.TimesNewRoman => "Times New Roman",
            _ => "Segoe UI Variable Text, Segoe UI"
        };
        var cjk = CjkFont switch
        {
            CjkFontPreset.MicrosoftYaHei => "Microsoft YaHei UI",
            CjkFontPreset.DengXian => "DengXian",
            CjkFontPreset.SimSun => "SimSun",
            CjkFontPreset.SimHei => "SimHei",
            CjkFontPreset.KaiTi => "KaiTi",
            CjkFontPreset.FangSong => "FangSong",
            _ => systemFontFamily
        };

        return string.Join(", ", new[]
        {
            latin,
            cjk,
            "Microsoft JhengHei UI",
            "Yu Gothic UI",
            "Malgun Gothic"
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
