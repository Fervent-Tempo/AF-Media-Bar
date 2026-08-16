using Microsoft.Win32;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal static class FontSettingsService
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";

    internal static FontSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            if (key is null)
            {
                return FontSettings.Default;
            }

            var latin = key.GetValue("LatinFontPreset") is int latinValue &&
                Enum.IsDefined(typeof(LatinFontPreset), latinValue)
                    ? (LatinFontPreset)latinValue
                    : (LatinFontPreset?)null;
            var cjk = key.GetValue("CjkFontPreset") is int cjkValue &&
                Enum.IsDefined(typeof(CjkFontPreset), cjkValue)
                    ? (CjkFontPreset)cjkValue
                    : (CjkFontPreset?)null;
            if (latin is not null && cjk is not null)
            {
                return new FontSettings(latin.Value, cjk.Value);
            }

            // 新键缺失时迁移旧版单一字体预设（v1.1 及以前）
            if (key.GetValue("FontPreset") is int legacyPreset)
            {
                return MigrateFromLegacy(legacyPreset);
            }

            return FontSettings.Default;
        }
        catch
        {
            return FontSettings.Default;
        }
    }

    internal static void Save(FontSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue("LatinFontPreset", (int)settings.Latin, RegistryValueKind.DWord);
        key.SetValue("CjkFontPreset", (int)settings.Cjk, RegistryValueKind.DWord);
        key.DeleteValue("FontPreset", throwOnMissingValue: false);
    }

    /// <summary>
    /// 旧版 FontPreset：0=Segoe UI 系统默认，1=微软雅黑，2=等线，3=宋体，4=黑体，5=楷体，6=仿宋。
    /// 旧实现是"中文优先"链，英文实际使用中文字体自带西文字形，因此迁移为 FollowCjk + 对应中文。
    /// </summary>
    private static FontSettings MigrateFromLegacy(int preset) => preset switch
    {
        0 => new FontSettings(LatinFontPreset.SegoeUi, CjkFontPreset.SystemDefault),
        1 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.MicrosoftYaHei),
        2 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.DengXian),
        3 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.SimSun),
        4 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.SimHei),
        5 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.KaiTi),
        6 => new FontSettings(LatinFontPreset.FollowCjk, CjkFontPreset.FangSong),
        _ => FontSettings.Default
    };
}