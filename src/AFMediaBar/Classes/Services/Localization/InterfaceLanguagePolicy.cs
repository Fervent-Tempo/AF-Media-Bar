using System.Globalization;

namespace AFMediaBar.Classes.Services.Localization;

/// <summary>
/// 界面语言设置的纯策略：把"用户的选项 + 系统 UI 区域性"解析成一个具体语言。
///
/// 它保持无 I/O、无 WPF、无设置访问，因此"跟随系统"的全部规则都可以直接单元测试，而不需要在真实桌面上切换
/// Windows 显示语言来验证。
/// Pure policy for the interface-language setting: it resolves "the user's option plus the system UI culture" into one
/// concrete language.
///
/// It stays free of I/O, WPF, and settings access, so every rule behind "follow the system" is directly unit-testable
/// instead of requiring a real desktop with a switched Windows display language.
/// </summary>
public static class InterfaceLanguagePolicy
{
    /// <summary>
    /// 解析当前生效的语言。<see cref="InterfaceLanguage.System"/> 交给系统区域性决定，其余选项直接采用。
    /// Resolves the language in effect. <see cref="InterfaceLanguage.System"/> is decided by the system culture and the
    /// other options are taken as they are.
    /// </summary>
    /// <param name="setting">用户在设置里选择的项。/ The option chosen in the settings.</param>
    /// <param name="systemCulture">系统 UI 区域性；为 null 时按英文处理。/ The system UI culture, treated as English when null.</param>
    public static LocalizationLanguage Resolve(InterfaceLanguage setting, CultureInfo? systemCulture) => setting switch
    {
        InterfaceLanguage.SimplifiedChinese => LocalizationLanguage.SimplifiedChinese,
        InterfaceLanguage.TraditionalChinese => LocalizationLanguage.TraditionalChinese,
        InterfaceLanguage.English => LocalizationLanguage.English,
        InterfaceLanguage.Vietnamese => LocalizationLanguage.Vietnamese,
        _ => ResolveSystem(systemCulture),
    };

    /// <summary>
    /// 按系统 UI 区域性解析语言。
    ///
    /// 规则：中文系统按正体/简体分成两支——区域名带 <c>Hant</c>、<c>TW</c>、<c>HK</c>、<c>MO</c> 或 <c>CHT</c>
    /// 的是繁体，其余中文（含中性的 <c>zh</c>）是简体；越南语系统解析为越南语；其余非中文系统一律回到英文，
    /// 因为英文面向非中文用户，而"跟随系统"得到不支持的语言时不能把界面留空。
    /// Resolves the language from the system UI culture.
    ///
    /// The rule: a Chinese system splits into the traditional and simplified branches — a region name carrying
    /// <c>Hant</c>, <c>TW</c>, <c>HK</c>, <c>MO</c>, or <c>CHT</c> is traditional and every other Chinese culture
    /// (including the neutral <c>zh</c>) is simplified; a Vietnamese system resolves to Vietnamese; every other
    /// non-Chinese system falls back to English, because English is aimed at non-Chinese users and "follow the system"
    /// must never leave the interface empty when it resolves to a language the application does not carry.
    /// </summary>
    /// <param name="systemCulture">系统 UI 区域性；为 null 时按英文处理。/ The system UI culture, treated as English when null.</param>
    public static LocalizationLanguage ResolveSystem(CultureInfo? systemCulture)
    {
        var name = systemCulture?.Name;
        if (string.IsNullOrEmpty(name))
        {
            return LocalizationLanguage.English;
        }

        if (name.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationLanguage.Vietnamese;
        }

        if (!name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationLanguage.English;
        }

        return name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CHT", StringComparison.OrdinalIgnoreCase)
            ? LocalizationLanguage.TraditionalChinese
            : LocalizationLanguage.SimplifiedChinese;
    }

    /// <summary>
    /// 语言对应的区域性名称。它用于把进程的 UI 区域性对齐到界面语言，因此文字度量与框架自带的文案都跟着同一支语言走。
    /// Culture name for a language. It aligns the process UI culture with the interface language, so text measurement and
    /// the framework's own wording follow the same language as the interface.
    /// </summary>
    /// <param name="language">要取名称的语言。/ Language whose name is wanted.</param>
    public static string ToCultureName(LocalizationLanguage language) => language switch
    {
        LocalizationLanguage.TraditionalChinese => "zh-Hant",
        LocalizationLanguage.English => "en",
        LocalizationLanguage.Vietnamese => "vi",
        _ => "zh-Hans",
    };
}
