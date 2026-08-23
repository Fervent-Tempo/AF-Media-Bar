using AFMediaBar.Abstractions;
using AFMediaBar.Models;

namespace AFMediaBar.WinUI;

/// <summary>
/// Strongly scoped shell strings. Product settings text remains owned by the WPF shell
/// until the settings-center migration batch.
/// </summary>
internal sealed class WinUiStringLocalizer(AppLanguage language) : IStringLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> EnUs = new Dictionary<string, string>
    {
        ["Shell.Title"] = "AF Media Bar",
        ["Shell.Tagline"] = "WinUI 3 application shell",
        ["Shell.StatusReady"] = "Ready in floating mode",
        ["Shell.StatusHighContrast"] = "High contrast is enabled",
        ["Shell.OpenSettings"] = "Settings",
        ["Shell.Exit"] = "Exit",
        ["Shell.SettingsTitle"] = "Shell settings",
        ["Shell.SettingsDescription"] = "Choose the shell language and appearance.",
        ["Shell.Theme"] = "Theme",
        ["Shell.ThemeAutomatic"] = "Follow Windows",
        ["Shell.ThemeLight"] = "Light",
        ["Shell.ThemeDark"] = "Dark",
        ["Shell.Language"] = "Language",
        ["Shell.LanguageFollowSystem"] = "Follow system",
        ["Shell.LanguageZhCn"] = "Simplified Chinese",
        ["Shell.LanguageZhTw"] = "Traditional Chinese",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "Back",
        ["Shell.Close"] = "Close"
    };

    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>
    {
        ["Shell.Title"] = "AF Media Bar",
        ["Shell.Tagline"] = "WinUI 3 应用外壳",
        ["Shell.StatusReady"] = "悬浮模式已就绪",
        ["Shell.StatusHighContrast"] = "高对比度已启用",
        ["Shell.OpenSettings"] = "设置",
        ["Shell.Exit"] = "退出",
        ["Shell.SettingsTitle"] = "外壳设置",
        ["Shell.SettingsDescription"] = "选择外壳语言和外观。",
        ["Shell.Theme"] = "主题",
        ["Shell.ThemeAutomatic"] = "跟随 Windows",
        ["Shell.ThemeLight"] = "浅色",
        ["Shell.ThemeDark"] = "深色",
        ["Shell.Language"] = "语言",
        ["Shell.LanguageFollowSystem"] = "跟随系统",
        ["Shell.LanguageZhCn"] = "简体中文",
        ["Shell.LanguageZhTw"] = "繁体中文",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "返回",
        ["Shell.Close"] = "关闭"
    };

    private static readonly IReadOnlyDictionary<string, string> ZhTw = new Dictionary<string, string>
    {
        ["Shell.Title"] = "AF Media Bar",
        ["Shell.Tagline"] = "WinUI 3 應用程式外殼",
        ["Shell.StatusReady"] = "懸浮模式已就緒",
        ["Shell.StatusHighContrast"] = "高對比度已啟用",
        ["Shell.OpenSettings"] = "設定",
        ["Shell.Exit"] = "結束",
        ["Shell.SettingsTitle"] = "外殼設定",
        ["Shell.SettingsDescription"] = "選擇外殼語言與外觀。",
        ["Shell.Theme"] = "主題",
        ["Shell.ThemeAutomatic"] = "跟隨 Windows",
        ["Shell.ThemeLight"] = "淺色",
        ["Shell.ThemeDark"] = "深色",
        ["Shell.Language"] = "語言",
        ["Shell.LanguageFollowSystem"] = "跟隨系統",
        ["Shell.LanguageZhCn"] = "簡體中文",
        ["Shell.LanguageZhTw"] = "繁體中文",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "返回",
        ["Shell.Close"] = "關閉"
    };

    private AppLanguage _language = language;

    public AppLanguage Language
    {
        get => _language;
        set => _language = value;
    }

    public string Get(string key, params object[] args)
    {
        var dictionary = ResolveDictionary();
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = EnUs.TryGetValue(key, out var fallback)
                ? fallback
                : key;
        }

        return args.Length == 0
            ? value
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, value, args);
    }

    private IReadOnlyDictionary<string, string> ResolveDictionary()
    {
        return ResolveLanguage() switch
        {
            AppLanguage.ZhCn => ZhCn,
            AppLanguage.ZhTw => ZhTw,
            _ => EnUs
        };
    }

    private AppLanguage ResolveLanguage()
    {
        if (_language != AppLanguage.FollowSystem)
        {
            return _language;
        }

        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        return culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
              culture.Name is "zh-TW" or "zh-HK" or "zh-MO"
                ? AppLanguage.ZhTw
                : AppLanguage.ZhCn
            : AppLanguage.EnUs;
    }
}
