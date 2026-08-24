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
        ["Shell.OpenDetailedSettings"] = "Open detailed settings...",
        ["Shell.Exit"] = "Exit",
        ["Shell.SettingsTitle"] = "Shell settings",
        ["Shell.SettingsDescription"] = "Choose the shell language and appearance.",
        ["Shell.Theme"] = "Theme",
        ["Shell.ThemeDescription"] = "Match Windows or use a fixed appearance",
        ["Shell.ThemeAutomatic"] = "Follow Windows",
        ["Shell.ThemeLight"] = "Light",
        ["Shell.ThemeDark"] = "Dark",
        ["Shell.Language"] = "Language",
        ["Shell.LanguageDescription"] = "Language used by the WinUI shell",
        ["Shell.LanguageFollowSystem"] = "Follow system",
        ["Shell.LanguageZhCn"] = "Simplified Chinese",
        ["Shell.LanguageZhTw"] = "Traditional Chinese",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "Back",
        ["Shell.Close"] = "Close",
        ["Main.Placeholder.Title"] = "Waiting for media",
        ["Main.Placeholder.Subtitle"] = "Play audio or video in any app",
        ["Main.Menu.Sources"] = "Media sources",
        ["Main.Menu.NoSessions"] = "No media sessions available",
        ["Main.Control.Previous"] = "Previous",
        ["Main.Control.Play"] = "Play",
        ["Main.Control.Pause"] = "Pause",
        ["Main.Control.Next"] = "Next",
        ["Main.Media.UnknownSource"] = "Unknown media",
        ["Main.Media.UnknownArtist"] = "Unknown artist",
        ["Main.Media.LoadingArtist"] = "Loading media...",
        ["Msg.MediaControlFailed"] = "Media control failed",
        ["Msg.SessionAccessFailed"] = "Media access is unavailable"
    };

    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>
    {
        ["Shell.Title"] = "AF Media Bar",
        ["Shell.Tagline"] = "WinUI 3 应用外壳",
        ["Shell.StatusReady"] = "悬浮模式已就绪",
        ["Shell.StatusHighContrast"] = "高对比度已启用",
        ["Shell.OpenSettings"] = "设置",
        ["Shell.OpenDetailedSettings"] = "打开详细设置...",
        ["Shell.Exit"] = "退出",
        ["Shell.SettingsTitle"] = "外壳设置",
        ["Shell.SettingsDescription"] = "选择外壳语言和外观。",
        ["Shell.Theme"] = "主题",
        ["Shell.ThemeDescription"] = "跟随 Windows 或使用固定外观",
        ["Shell.ThemeAutomatic"] = "跟随 Windows",
        ["Shell.ThemeLight"] = "浅色",
        ["Shell.ThemeDark"] = "深色",
        ["Shell.Language"] = "语言",
        ["Shell.LanguageDescription"] = "WinUI 外壳使用的显示语言",
        ["Shell.LanguageFollowSystem"] = "跟随系统",
        ["Shell.LanguageZhCn"] = "简体中文",
        ["Shell.LanguageZhTw"] = "繁体中文",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "返回",
        ["Shell.Close"] = "关闭",
        ["Main.Placeholder.Title"] = "等待媒体播放",
        ["Main.Placeholder.Subtitle"] = "在任意应用播放音频或视频",
        ["Main.Menu.Sources"] = "媒体来源",
        ["Main.Menu.NoSessions"] = "暂无可用媒体会话",
        ["Main.Control.Previous"] = "上一首",
        ["Main.Control.Play"] = "播放",
        ["Main.Control.Pause"] = "暂停",
        ["Main.Control.Next"] = "下一首",
        ["Main.Media.UnknownSource"] = "未知媒体",
        ["Main.Media.UnknownArtist"] = "未知创作者",
        ["Main.Media.LoadingArtist"] = "正在加载媒体...",
        ["Msg.MediaControlFailed"] = "媒体控制失败",
        ["Msg.SessionAccessFailed"] = "媒体访问不可用"
    };

    private static readonly IReadOnlyDictionary<string, string> ZhTw = new Dictionary<string, string>
    {
        ["Shell.Title"] = "AF Media Bar",
        ["Shell.Tagline"] = "WinUI 3 應用程式外殼",
        ["Shell.StatusReady"] = "懸浮模式已就緒",
        ["Shell.StatusHighContrast"] = "高對比度已啟用",
        ["Shell.OpenSettings"] = "設定",
        ["Shell.OpenDetailedSettings"] = "開啟詳細設定...",
        ["Shell.Exit"] = "結束",
        ["Shell.SettingsTitle"] = "外殼設定",
        ["Shell.SettingsDescription"] = "選擇外殼語言與外觀。",
        ["Shell.Theme"] = "主題",
        ["Shell.ThemeDescription"] = "跟隨 Windows 或使用固定外觀",
        ["Shell.ThemeAutomatic"] = "跟隨 Windows",
        ["Shell.ThemeLight"] = "淺色",
        ["Shell.ThemeDark"] = "深色",
        ["Shell.Language"] = "語言",
        ["Shell.LanguageDescription"] = "WinUI 外殼使用的顯示語言",
        ["Shell.LanguageFollowSystem"] = "跟隨系統",
        ["Shell.LanguageZhCn"] = "簡體中文",
        ["Shell.LanguageZhTw"] = "繁體中文",
        ["Shell.LanguageEnUs"] = "English",
        ["Shell.Back"] = "返回",
        ["Shell.Close"] = "關閉",
        ["Main.Placeholder.Title"] = "等待媒體播放",
        ["Main.Placeholder.Subtitle"] = "在任何應用程式播放音訊或影片",
        ["Main.Menu.Sources"] = "媒體來源",
        ["Main.Menu.NoSessions"] = "目前沒有可用的媒體工作階段",
        ["Main.Control.Previous"] = "上一首",
        ["Main.Control.Play"] = "播放",
        ["Main.Control.Pause"] = "暫停",
        ["Main.Control.Next"] = "下一首",
        ["Main.Media.UnknownSource"] = "未知媒體",
        ["Main.Media.UnknownArtist"] = "未知創作者",
        ["Main.Media.LoadingArtist"] = "正在載入媒體...",
        ["Msg.MediaControlFailed"] = "媒體控制失敗",
        ["Msg.SessionAccessFailed"] = "媒體存取無法使用"
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
