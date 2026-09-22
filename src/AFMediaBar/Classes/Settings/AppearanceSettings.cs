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
    Acrylic = 2,

    /// <summary>
    /// 云母 Alt：与云母同源、底色更重的系统材质，只在 Windows 11 22000 之后的 22621+ 提供。
    /// Mica Alt: the heavier-tinted sibling of Mica, available only on Windows 11 22621 and later.
    /// </summary>
    MicaAlt = 3
}

/// <summary>强调色来源。 / Source of the accent color.</summary>
public enum AccentColorMode
{
    /// <summary>跟随系统强调色（Windows 个性化里的那一支）。 / Follow the system accent from Windows personalisation.</summary>
    System = 0,

    /// <summary>使用用户选定的强调色。 / Use the accent the user picked.</summary>
    Custom = 1
}

/// <summary>西文字体预设。 / Latin font preset.</summary>
public enum LatinFontPreset
{
    SegoeUi = 0,
    Arial = 1,
    Calibri = 2,
    Verdana = 3,
    Consolas = 4,
    TimesNewRoman = 5,
    SystemDefault = 6
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
    // 该选项已从 UI 与呈现逻辑移除；字段保留在模型里以保持序列化形态不变，值不再影响任何行为。
    // The option was removed from the UI and the rendering logic; the field stays in the model so the serialized shape does not
    // change, and its value no longer affects anything.
    bool EnhancedReadability,
    ApplicationThemeMode ApplicationThemeMode,
    ApplicationBackdropMode BackdropMode,
    // 以下三项都是纯新增字段：旧设置文件缺字段时反序列化保留这里的默认值，`Normalize()` 再把它夹进合法区间，
    // 因此按 `SettingsPersistenceService` 的规则不升 schema 版本、不写迁移段。
    // The three fields below are purely additive: an older file that lacks them deserializes to the defaults declared here and
    // `Normalize()` clamps them into range, so per the persistence service's rule the schema version does not move and no
    // migration block is written.
    AccentColorMode AccentColorMode,
    string AccentColor,
    // 可空不是装饰：旧设置文件里没有这个字段，反序列化会把"缺字段"给成 null，而 0 是一个会被夹到下限的合法输入。
    // 两者必须分得开，因此"未设置"用 null 表达，取值统一走 `ResolveBackdropTintOpacityPercent`。
    // The nullability is not decoration: an older settings file has no such field, so deserialization hands "missing" over as
    // null, while 0 is a legal input that would merely be clamped to the lower bound. The two have to stay distinguishable, so
    // "not set" is expressed as null and every reader goes through `ResolveBackdropTintOpacityPercent`.
    int? BackdropTintOpacityPercent)
{
    /// <summary>
    /// 字体粗细下限。取 OpenType 的 Thin：WPF 只有 100–900 九个真实字重，界面按 100 步进在三者之间取值，
    /// 因此下限必须落在真实字重上，否则滑杆会停在一个会被就近取整的值上。
    /// Lower font-weight bound: OpenType Thin. WPF has only the nine real weights from 100 to 900, the interface steps by 100
    /// across them, and the bound therefore has to land on a real weight instead of a value that is only rounded to one.
    /// </summary>
    public const int MinimumFontWeight = 100;

    /// <summary>字体粗细上限（OpenType Black）。 / Upper font-weight bound (OpenType Black).</summary>
    public const int MaximumFontWeight = 900;

    /// <summary>
    /// 字体粗细滑杆的步进。间距必须等于真实字重的间隔：100–900 之间只有九个字形族，
    /// 用 50 或 10 步进会产生大量看起来完全相同的取值。
    /// Step of the font-weight slider. The spacing must equal the real weight interval: only nine typeface weights exist between
    /// 100 and 900, so stepping by 50 or 10 would produce many values that look exactly alike.
    /// </summary>
    public const int FontWeightStep = 100;

    /// <summary>
    /// 材质底色浓度的下限。低于它时系统材质几乎完全透明，浅色壁纸上的深色主题文字会失去可读性；
    /// 与上限一起构成滑杆区间，取值区间只有这一处权威。
    /// Lower bound of the material tint concentration. Below it the system material is almost fully transparent and dark-theme
    /// text on a bright wallpaper loses legibility; together with the upper bound this is the only authority for the slider range.
    /// </summary>
    public const int MinimumBackdropTintOpacityPercent = 30;

    /// <summary>材质底色浓度的上限（100% 即不透明底色）。 / Upper bound of the material tint concentration (100% is an opaque surface).</summary>
    public const int MaximumBackdropTintOpacityPercent = 100;

    /// <summary>材质底色浓度的滑杆步进。 / Slider step of the material tint concentration.</summary>
    public const int BackdropTintOpacityStep = 5;

    /// <summary>
    /// 材质底色浓度的默认值（60%）。
    ///
    /// 这一档来自实测：Windows 的 Accent 模糊路径在 80% 底色下，窗口内的亮度标准差只有 2.1（背后壁纸是 42.5），
    /// 模糊几乎看不出来、观感与纯色无异；60% 时标准差升到 3.9 且窗口内能看出背后壁纸的明暗结构，同时深色主题的
    /// 文字仍然落在足够暗的底色上。想回到旧观感的用户把滑杆推到 80% 即可。
    /// Default concentration (60%).
    ///
    /// The value comes from measurement: with an 80% tint the accent blur path leaves a luminance deviation of only 2.1 inside the
    /// window (the wallpaper behind it measures 42.5), so the blur is imperceptible and reads as a solid surface; at 60% the
    /// deviation rises to 3.9 and the wallpaper's light/dark structure becomes visible, while dark-theme text still sits on a
    /// dark enough base. Anyone who wants the old look can push the slider back to 80%.
    /// </summary>
    public const int DefaultBackdropTintOpacityPercent = 60;

    /// <summary>
    /// 自选强调色的出厂值：Windows 默认强调蓝。选到"自定义"但还没挑颜色时用它，界面上因此永远有一个具体颜色可显示。
    /// Factory value of the custom accent: the Windows default accent blue. It is used when "custom" is selected but no colour has
    /// been picked yet, so the interface always has a concrete colour to show.
    /// </summary>
    public const string DefaultAccentColorHex = "#0078D4";

    public static AppearanceSettings Default { get; } = new(
        LatinFontPreset.SystemDefault,
        CjkFontPreset.SystemDefault,
        400,
        PlayerForegroundMode.Automatic,
        false,
        ApplicationThemeMode.Automatic,
        ApplicationBackdropMode.Mica,
        AccentColorMode.System,
        DefaultAccentColorHex,
        DefaultBackdropTintOpacityPercent);

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
            AccentColorMode = Enum.IsDefined(AccentColorMode) ? AccentColorMode : defaults.AccentColorMode,
            // 强调色存的是十六进制文本：旧文件、手改文件或将来格式变化都可能给出无法解析的值，此时 MUST 回退到默认色
            // 而不是把空值写进调色板（那会让整套强调色画刷变成透明）。
            // The accent is stored as hexadecimal text: an older file, a hand-edited file, or a future format can all produce an
            // unparsable value, and that MUST fall back to the default instead of feeding an empty colour into the palette, which
            // would turn every accent brush transparent.
            AccentColor = Classes.Utils.ColorHex.TryParse(AccentColor, out var accent)
                ? Classes.Utils.ColorHex.Format(accent)
                : defaults.AccentColor,
            BackdropTintOpacityPercent = ResolveBackdropTintOpacityPercent(),
            FontWeight = SnapFontWeight(FontWeight)
        };
        return normalized;
    }

    /// <summary>
    /// 材质浓度的生效值：旧设置文件缺字段（null）取文档化默认值，越界值按滑杆区间夹取。
    /// Effective material concentration: a missing field in an older settings file (null) takes the documented default, and an
    /// out-of-range value is clamped to the slider's range.
    /// </summary>
    public int ResolveBackdropTintOpacityPercent() => BackdropTintOpacityPercent is { } percent
        ? Math.Clamp(percent, MinimumBackdropTintOpacityPercent, MaximumBackdropTintOpacityPercent)
        : DefaultBackdropTintOpacityPercent;

    /// <summary>
    /// 把任意粗细吸附到最近的真实字重并夹取。旧设置文件里可能存在 350 或 250 这类值，
    /// 不吸附的话界面会显示一个滑杆位置无法表达的读数。
    /// Snaps any weight onto the nearest real weight and clamps it. Older settings files may hold values such as 350 or 250, and
    /// without this snap the interface would show a reading its own slider position cannot express.
    /// </summary>
    /// <param name="fontWeight">待换算的粗细。/ Weight to normalize.</param>
    public static int SnapFontWeight(int fontWeight)
    {
        var snapped = (int)Math.Round(fontWeight / (double)FontWeightStep, MidpointRounding.AwayFromZero) * FontWeightStep;
        return Math.Clamp(snapped, MinimumFontWeight, MaximumFontWeight);
    }

    /// <summary>生成西文优先、中文和东亚字符回退在后的字体链。 / Builds a Latin-first fallback chain with CJK coverage.</summary>
    public string ResolveFontFamilySource(string systemFontFamily)
    {
        var latin = LatinFont switch
        {
            LatinFontPreset.SystemDefault => systemFontFamily,
            LatinFontPreset.SegoeUi => "Segoe UI Variable Text, Segoe UI",
            LatinFontPreset.Arial => "Arial",
            LatinFontPreset.Calibri => "Calibri",
            LatinFontPreset.Verdana => "Verdana",
            LatinFontPreset.Consolas => "Consolas",
            LatinFontPreset.TimesNewRoman => "Times New Roman",
            _ => systemFontFamily
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
            "Microsoft YaHei UI",
            "Microsoft JhengHei UI",
            "Yu Gothic UI",
            "Malgun Gothic"
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
