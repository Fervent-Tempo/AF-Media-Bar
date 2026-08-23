using System.Globalization;
using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 界面语言的注册表持久化与词典名称解析。
/// </summary>
public static class LanguageSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    public static AppLanguage Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return AppLanguage.FollowSystem;
            }

            var language = key.GetValue("AppLanguage") is int value &&
                Enum.IsDefined(typeof(AppLanguage), value)
                    ? (AppLanguage)value
                    : AppLanguage.FollowSystem;
            return language;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("language-settings-read", exception);
            return AppLanguage.FollowSystem;
        }
    }

    public static void Save(AppLanguage language)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("AppLanguage", (int)language, RegistryValueKind.DWord);
    }

    /// <summary>
    /// 将设置解析为实际使用的词典名称；FollowSystem 按系统 UI 语言推断。
    /// </summary>
    public static string ResolveDictionaryName(AppLanguage language)
    {
        var effective = language switch
        {
            AppLanguage.ZhCn => "zh-CN",
            AppLanguage.ZhTw => "zh-TW",
            AppLanguage.EnUs => "en-US",
            _ => ResolveSystemLanguage()
        };
        return effective;
    }

    private static string ResolveSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("Hant") || name is "zh-TW" or "zh-HK" or "zh-MO"
                ? "zh-TW"
                : "zh-CN";
        }

        return "en-US";
    }
}
